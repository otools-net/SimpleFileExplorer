using System.Text;
using Sfe.Core.Paths;

namespace Sfe.Core.KeepAlive;

/// <summary>
/// Whether a folder is on a network share, so that <see cref="KeepAliveMode.Auto"/> keeps only those alive. Links and
/// junctions are followed first (<see cref="LibraryPaths.RealPath"/>): a local folder leading to a share counts.
/// </summary>
/// <remarks>It touches the file system (links, a share's root): call it off the request and loop threads.</remarks>
public static class NetworkFolders
{
    /// <summary>File system types of <c>/proc/self/mountinfo</c> served by another computer.</summary>
    private static readonly HashSet<string> NetworkTypes = new(StringComparer.Ordinal)
    {
        "cifs", "smb3", "smbfs", "nfs", "nfs4", "9p", "afs", "ncpfs", "ceph", "glusterfs", "davfs",
        "fuse.sshfs", "fuse.rclone", "fuse.davfs2", "fuse.glusterfs", "fuse.ceph", "fuse.s3fs",
    };

    public static bool IsNetwork(string path)
    {
        var real = LibraryPaths.RealPath(path);
        if (OperatingSystem.IsWindows())
            return IsUncPath(real) || IsNetworkDrive(real);
        if (OperatingSystem.IsLinux() && ReadMountInfo() is { } mounts)
            return FileSystemTypeAt(mounts, real) is { } type && NetworkTypes.Contains(type);
        return false;
    }

    /// <summary><c>\\server\share</c> or <c>\\?\UNC\server\share</c>; not <c>\\?\C:\</c> nor the device namespace <c>\\.\</c>.</summary>
    internal static bool IsUncPath(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal));

    /// <summary>The file system type of the mount holding <paramref name="path"/> (the deepest mount point containing it).</summary>
    /// <param name="mountInfo">The text of <c>/proc/self/mountinfo</c>.</param>
    internal static string? FileSystemTypeAt(string mountInfo, string path)
    {
        string? best = null;
        var bestLength = -1;
        foreach (var line in mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "36 35 98:0 /mnt1 /mnt/parent rw,noatime master:1 - ext3 /dev/root rw": mount point 5th, type after "-".
            var fields = line.Split(' ');
            var separator = Array.IndexOf(fields, "-", 6);
            if (fields.Length < 5 || separator < 0 || separator + 1 >= fields.Length)
                continue;
            var mountPoint = Unescape(fields[4]);
            // ">=": a later mount over the same point hides the earlier one.
            if (Contains(mountPoint, path) && mountPoint.Length >= bestLength)
            {
                best = fields[separator + 1];
                bestLength = mountPoint.Length;
            }
        }

        return best;
    }

    private static bool IsNetworkDrive(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? ReadMountInfo()
    {
        try
        {
            return File.ReadAllText("/proc/self/mountinfo");
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Contains(string mountPoint, string path) =>
        mountPoint == "/"
        || path == mountPoint
        || (path.StartsWith(mountPoint, StringComparison.Ordinal) && path[mountPoint.Length] == '/');

    /// <summary>mountinfo writes space, tab, newline and backslash as octal escapes (<c>\040</c>…).</summary>
    private static string Unescape(string field)
    {
        if (!field.Contains('\\'))
            return field;
        var text = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length && IsOctal(field[i + 1]) && IsOctal(field[i + 2]) && IsOctal(field[i + 3]))
            {
                text.Append((char)((field[i + 1] - '0') * 64 + (field[i + 2] - '0') * 8 + (field[i + 3] - '0')));
                i += 3;
            }
            else
            {
                text.Append(field[i]);
            }
        }

        return text.ToString();
    }

    private static bool IsOctal(char c) => c is >= '0' and <= '7';
}
