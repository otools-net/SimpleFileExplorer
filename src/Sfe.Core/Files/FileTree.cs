namespace Sfe.Core.Files;

/// <summary>The trash (or a restore) would have to copy a folder holding links to another drive: never done.</summary>
public sealed class LinksInsideException(string message) : IOException(message);

/// <summary>
/// Moving and deleting files and folders on the disk for file management (agents/docs/features/trash.md). A symbolic
/// link or junction is always acted on as a link: moved or deleted itself, never what it leads to, and never followed.
/// </summary>
public static class FileTree
{
    /// <summary>
    /// Moves <paramref name="from"/> to <paramref name="to"/> (which must not exist). On the same drive it is a rename;
    /// to another drive a file is copied by the system, a folder copied here then deleted (refused with
    /// <see cref="LinksInsideException"/> when it holds links, leaving nothing behind).
    /// </summary>
    public static void Move(string from, string to, bool isFolder)
    {
        if (!isFolder || IsLink(from))
        {
            MoveEntry(from, to, isFolder);
            return;
        }
        try
        {
            Directory.Move(from, to);
        }
        // Only into a place that is free: the clean-up of a failed copy deletes it.
        catch (IOException ex) when ((IsOtherDrive(ex) || !SameRoot(from, to)) && !Directory.Exists(to) && !File.Exists(to))
        {
            MoveByCopy(from, to);
        }
    }

    /// <summary>A folder's move to another drive: copied, then deleted; a failed copy leaves nothing behind.</summary>
    internal static void MoveByCopy(string from, string to)
    {
        try
        {
            CopyFolder(from, to);
        }
        catch
        {
            if (Directory.Exists(to))
                Delete(to);
            throw;
        }
        Delete(from);
    }

    /// <summary>Deletes a file or a folder with everything in it; a link is deleted as a link.</summary>
    public static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            // A link or junction to a folder: only the link goes (a non-recursive delete removes the link itself).
            if (IsLink(path))
                Directory.Delete(path);
            else
                DeleteFolder(path);
        }
        else
        {
            File.Delete(path);
        }
    }

    /// <summary>The path is a symbolic link or a junction (to a file or a folder).</summary>
    public static bool IsLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is not null;
    }

    /// <summary>The files below <paramref name="folder"/> (links not followed), for a walk over what is deleted.</summary>
    public static IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder) && !IsLink(folder)
            ? Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
            : [];

    /// <summary>Every entry, hidden and system ones too, links not followed.</summary>
    private static readonly EnumerationOptions All = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

    /// <summary>
    /// A folder and what is in it. .NET's recursive delete refuses a folder holding a junction ("access denied"), so
    /// the walk is done here: links removed as links, read-only files made writable first.
    /// </summary>
    private static void DeleteFolder(string folder)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", All))
        {
            if (entry.LinkTarget is not null)
            {
                if (entry is DirectoryInfo)
                    Directory.Delete(entry.FullName);
                else
                    File.Delete(entry.FullName);
            }
            else if (entry is DirectoryInfo)
            {
                DeleteFolder(entry.FullName);
            }
            else
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
            }
        }
        Directory.Delete(folder);
    }

    private static void MoveEntry(string from, string to, bool isFolder)
    {
        if (isFolder)
            Directory.Move(from, to);
        else
            File.Move(from, to, overwrite: false);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos("*", All))
        {
            var target = Path.Combine(to, entry.Name);
            if (entry.LinkTarget is not null)
                throw new LinksInsideException($"“{entry.Name}” is a link: a folder holding links is not copied to another drive.");
            if (entry is DirectoryInfo)
                CopyFolder(entry.FullName, target);
            else
                File.Copy(entry.FullName, target, overwrite: false);
        }
    }

    /// <summary>.NET refuses a folder move between two roots (drive letters) before asking the system.</summary>
    private static bool SameRoot(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>The move crosses drives: Windows' ERROR_NOT_SAME_DEVICE (17), EXDEV (18) elsewhere.</summary>
    private static bool IsOtherDrive(IOException ex) => (ex.HResult & 0xFFFF) is 17 or 18;
}
