namespace Sfe.Core.Access;

/// <summary>
/// Files and folders holding the server's secrets (setup code, recovery codes, the keys signing cookies), kept from
/// the other accounts of the computer where the system has file modes (Linux, macOS). On Windows a file has the
/// rights of its folder: who may read the data folder is decided where SFE is installed.
/// </summary>
public static class SecretFiles
{
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerFolder = OwnerFile | UnixFileMode.UserExecute;
    private const UnixFileMode Others =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/>, readable by the server's account only from the
    /// moment the file exists: never for an instant with the process's default mode.
    /// </summary>
    /// <exception cref="IOException">The folder is not writable.</exception>
    public static void WriteAllText(string path, string text)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerFile;

        using var stream = new FileStream(path, options);
        // A file that was there keeps the mode it had: narrowed while it is still empty.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(stream.SafeFileHandle, OwnerFile);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }

    /// <summary>
    /// Creates the folder <paramref name="path"/> (and those above it that are missing) for the server's account only.
    /// A folder that exists is left as it is: its administrator may have opened it on purpose (<see cref="IsOpenToOthers"/>).
    /// </summary>
    /// <exception cref="IOException">The folder cannot be created.</exception>
    public static void CreatePrivateDirectory(string path)
    {
        if (Directory.Exists(path))
            return;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, OwnerFolder);
    }

    /// <summary>
    /// Other accounts of the computer may read or write the folder <paramref name="path"/> (file modes only: false on
    /// Windows, and for a folder that is missing or cannot be examined).
    /// </summary>
    public static bool IsOpenToOthers(string path)
    {
        if (OperatingSystem.IsWindows())
            return false;
        try
        {
            return Directory.Exists(path) && (File.GetUnixFileMode(path) & Others) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
