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

    private static string NormalizeRoot(string root)
    {
        root = ExpandHome(root.Trim());
        // "V:" means "current directory of drive V" for Windows APIs; as a root it can only mean the drive root.
        if (OperatingSystem.IsWindows() && root.Length == 2 && root[1] == ':')
            root += Path.DirectorySeparatorChar;

        var full = Path.GetFullPath(root);
        return Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
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
