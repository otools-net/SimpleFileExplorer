using System.Text;

namespace Sfe.Core.Files;

/// <summary>
/// The names file management gives files and folders (agents/docs/features/file-names.md): one name, never a path, and
/// one every system that may hold the library can store. A Windows server always refuses what Windows refuses; a Linux
/// server too when names must be portable (<see cref="Options.FileManagementOptions.PortableNames"/>, the default),
/// otherwise only what Linux refuses.
/// </summary>
public static class FileNames
{
    /// <summary>Longest name: 255 UTF-16 units on NTFS, 255 bytes (UTF-8) on most Linux file systems.</summary>
    public const int MaxLength = 255;

    /// <summary>Characters Windows refuses in a name (besides control characters and the separators).</summary>
    public const string WindowsInvalidCharacters = "<>:\"\\|?*";

    /// <summary>Names of devices on Windows, with or without an extension (<c>CON.txt</c> too).</summary>
    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Numbered("COM"), .. Numbered("LPT")],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Names tried by <see cref="KeepBoth"/> before giving up.</summary>
    public const int MaxKeepBothAttempts = 1000;

    /// <summary>
    /// Why <paramref name="name"/> can't be the name of a file or folder; null when it can.
    /// </summary>
    /// <param name="portable">Also refuse what Windows refuses, on another system (always on Windows).</param>
    public static string? Check(string? name, bool portable)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";
        if (name is "." or "..")
            return "“.” and “..” are not names.";
        if (name.Contains('/') || (OperatingSystem.IsWindows() && name.Contains('\\')))
            return "A name can't contain “/” or “\\”: it is one name, not a path.";
        if (name.Any(char.IsControl))
            return "A name can't contain control characters (line breaks, tabs…).";

        var windows = portable || OperatingSystem.IsWindows();
        if (windows)
        {
            if (name.IndexOfAny(WindowsInvalidCharacters.ToCharArray()) >= 0)
                return $"A name can't contain any of {string.Join(' ', WindowsInvalidCharacters.ToCharArray())}" + (OperatingSystem.IsWindows() ? "." : " (Windows refuses them).");
            if (name.EndsWith('.') || name.EndsWith(' '))
                return "A name can't end with a dot or a space" + (OperatingSystem.IsWindows() ? "." : " (Windows removes them).");
            if (IsReservedOnWindows(name))
                return $"“{name}” is the name of a device on Windows: pick another one.";
            if (name.Length > MaxLength)
                return $"The name is too long: at most {MaxLength} characters.";
        }
        if ((portable || !OperatingSystem.IsWindows()) && Encoding.UTF8.GetByteCount(name) > MaxLength)
            return $"The name is too long: at most {MaxLength} bytes (fewer characters for accented letters and other scripts).";
        return null;
    }

    /// <summary>
    /// <paramref name="name"/> is a device on Windows: <c>CON</c>, <c>NUL</c>, <c>COM1</c>, <c>LPT¹</c>… with or without
    /// an extension and spaces before it (<c>con .txt</c>).
    /// </summary>
    public static bool IsReservedOnWindows(string name)
    {
        var dot = name.IndexOf('.');
        return ReservedNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
    }

    /// <summary>
    /// The first free name for a copy of <paramref name="name"/> next to the original, as Explorer and the picture
    /// editor make them: <c>beach (2).jpg</c>, <c>beach (3).jpg</c>…, folders <c>Photos (2)</c>; null when
    /// <see cref="MaxKeepBothAttempts"/> names are all taken.
    /// </summary>
    /// <param name="exists">Whether a file or folder of that name is already there.</param>
    public static string? KeepBoth(string name, bool isFolder, Func<string, bool> exists)
    {
        var extension = isFolder ? string.Empty : Path.GetExtension(name);
        // A name that is only an extension (".profile") has no stem to number.
        if (extension.Length == name.Length)
            extension = string.Empty;
        var stem = name[..^extension.Length];
        for (var attempt = 2; attempt <= MaxKeepBothAttempts; attempt++)
        {
            var candidate = $"{stem} ({attempt}){extension}";
            if (!exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> Numbered(string device) =>
        Enumerable.Range(0, 10).Select(n => device + n).Concat(new[] { "¹", "²", "³" }.Select(n => device + n));
}
