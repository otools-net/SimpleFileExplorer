using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Core.Paths;

/// <summary>
/// Converts between API paths (relative to the library root, '/' separated, no leading slash)
/// and physical paths, refusing anything that would escape the root.
/// </summary>
public sealed class LibraryPaths
{
    /// <summary>
    /// How the host file system compares paths: case-insensitive on Windows and macOS, case-sensitive elsewhere
    /// (Linux), where "photo.jpg" and "Photo.jpg" are two different files.
    /// </summary>
    public static readonly StringComparison PathComparison =
        IsCaseInsensitiveFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static readonly StringComparer PathComparer =
        IsCaseInsensitiveFileSystem ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool IsCaseInsensitiveFileSystem => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public LibraryPaths(IOptions<SfeOptions> options)
        : this(options.Value.Root)
    {
    }

    public LibraryPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"'{SfeOptions.SectionName}:{nameof(SfeOptions.Root)}' is not configured.");

        Root = NormalizeRoot(root);
    }

    /// <summary>Absolute root, always ending with a directory separator.</summary>
    public string Root { get; }

    /// <summary>Display name of the root folder.</summary>
    public string RootName => Path.GetFileName(Path.TrimEndingDirectorySeparator(Root)) is { Length: > 0 } name
        ? name
        : Path.TrimEndingDirectorySeparator(Root);

    /// <summary>Resolves an API path to a physical path inside the root.</summary>
    /// <remarks>The root itself resolves without its trailing separator (unless it is a drive or file system root).</remarks>
    public bool TryResolve(string? relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        var normalized = NormalizeRelative(relativePath);
        if (normalized is null)
            return false;

        var candidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar))));
        if (!IsInsideRoot(candidate))
            return false;

        fullPath = candidate;
        return true;
    }

    public string Resolve(string? relativePath) =>
        TryResolve(relativePath, out var fullPath)
            ? fullPath
            : throw new ArgumentException($"'{relativePath}' is not a valid library path.", nameof(relativePath));

    /// <summary>Converts a physical path inside the root to its API path; null when outside the root.</summary>
    public string? ToRelative(string fullPath)
    {
        var candidate = Path.GetFullPath(fullPath);
        if (!IsInsideRoot(candidate))
            return null;

        return candidate.Length <= Root.Length
            ? string.Empty
            : candidate[Root.Length..].Replace(Path.DirectorySeparatorChar, '/');
    }

    public bool IsInsideRoot(string fullPath) =>
        fullPath.StartsWith(Root, PathComparison)
        || string.Equals(fullPath + Path.DirectorySeparatorChar, Root, PathComparison);

    /// <summary>Cleans an API path: unifies separators, trims slashes and rejects '.'/'..' segments.</summary>
    /// <remarks>
    /// On Windows '\' is also a separator and ':' (drive letters, alternate data streams) is rejected.
    /// Elsewhere both are ordinary file name characters.
    /// </remarks>
    public static string? NormalizeRelative(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        if (OperatingSystem.IsWindows())
            relativePath = relativePath.Replace('\\', '/');

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".." || s.Contains('\0') || (OperatingSystem.IsWindows() && s.Contains(':'))))
            return null;

        return string.Join('/', segments);
    }

    /// <summary>A root folder as typed (<c>~</c>, <c>V:</c>...) as a full path ending with a directory separator.</summary>
    public static string NormalizeRoot(string root)
    {
        root = ExpandHome(root.Trim());
        // "V:" means "current directory of drive V" for Windows APIs; as a root it can only mean the drive root.
        if (OperatingSystem.IsWindows() && root.Length == 2 && root[1] == ':')
            root += Path.DirectorySeparatorChar;

        var full = Path.GetFullPath(root);
        return Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
    }

    private const int MaxLinkDepth = 40;

    /// <summary>
    /// Where a path really is: every symbolic link or junction along it replaced by its final target, and on Windows
    /// short (8.3) names by long ones, so that two spellings of one place compare equal. Parts that don't exist are kept as
    /// written.
    /// </summary>
    public static string RealPath(string path) => LongPath(RealPath(path, 0));

    private static string RealPath(string path, int depth)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Join(current, part);
            FileSystemInfo info = new FileInfo(current);
            if (!info.Exists)
                info = new DirectoryInfo(current);
            if (info.LinkTarget is null || depth >= MaxLinkDepth)
                continue;
            try
            {
                if (info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                    current = RealPath(target.FullName, depth + 1);   // the target's own folders can be links too
            }
            catch (IOException)
            {
                // A broken or looping link: it leads nowhere that can be read.
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>On Windows, the long names of the parts of <paramref name="path"/> that exist (<c>PROGRA~1</c> → <c>Program Files</c>).</summary>
    private static string LongPath(string path)
    {
        if (!OperatingSystem.IsWindows() || !path.Contains('~'))
            return path;

        // The longest part that exists, then the rest as written.
        var existing = path;
        while (!File.Exists(existing) && !Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
            existing = parent;
        var buffer = new char[32768];
        var length = NativeMethods.GetLongPathNameW(existing, buffer, (uint)buffer.Length);
        return length == 0 || length > buffer.Length ? path : new string(buffer, 0, (int)length) + path[existing.Length..];
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern uint GetLongPathNameW(string shortPath, char[] longPath, uint bufferLength);
    }

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or inside it (both full paths, compared as the file system does).</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        // A drive or file system root keeps its separator.
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        var prefix = Path.EndsInDirectorySeparator(trimmed) ? trimmed : trimmed + Path.DirectorySeparatorChar;
        return string.Equals(Path.TrimEndingDirectorySeparator(path), trimmed, PathComparison) || path.StartsWith(prefix, PathComparison);
    }

    /// <summary>Expands a leading <c>~</c> to the user's home folder (e.g. <c>~/Videos</c>).</summary>
    public static string ExpandHome(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return path.StartsWith("~/", StringComparison.Ordinal) || (OperatingSystem.IsWindows() && path.StartsWith(@"~\", StringComparison.Ordinal))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
    }
}
