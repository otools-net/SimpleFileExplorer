namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Paths inside a connection: the rules of <see cref="Paths.LibraryPaths"/> for a tree that is not on the disk.
/// Relative to the connection's root folder, separated by <c>/</c>, empty for the root. Everything a route or a job
/// receives goes through <see cref="Normalize"/> before a provider sees it, so no path leaves the root folder.
/// </summary>
public static class RemotePaths
{
    /// <summary>Longest name accepted, in characters: what the strictest known provider takes.</summary>
    public const int MaxNameLength = 255;

    /// <summary>Longest path stored (folder rules, the root folder): SQL Server indexes cap there.</summary>
    public const int MaxPathLength = 1000;

    /// <summary>
    /// The path as stored and compared: no empty part, never <c>.</c> or <c>..</c>, no backslash (it would be a
    /// separator for some providers and a character for others), no control character. Null when it is none of that.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => !IsPathPart(part)))
            return null;

        var normalized = string.Join('/', parts);
        return normalized.Length > MaxPathLength ? null : normalized;
    }

    /// <summary>
    /// A name a new file or folder may be given in any connection: one part of a path, with no ending the
    /// providers fold away (a trailing dot or space), no surrounding blanks.
    /// </summary>
    public static bool IsValidName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength }
        && IsPathPart(name)
        && !name.Contains('/')
        && name.Trim() == name
        && !name.EndsWith('.');

    /// <summary><paramref name="path"/> below <paramref name="folder"/>; both normalised.</summary>
    public static string Combine(string folder, string path) =>
        folder.Length == 0 ? path
        : path.Length == 0 ? folder
        : $"{folder}/{path}";

    /// <summary>The folder holding <paramref name="path"/>; the root (empty) for an item of the root.</summary>
    public static string Parent(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "";

    /// <summary>The last part of <paramref name="path"/>; empty for the root.</summary>
    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or inside it, by the provider's rule for
    /// case.</summary>
    public static bool IsInTree(string path, string folder, StringComparison comparison) =>
        folder.Length == 0
        || path.StartsWith(folder, comparison) && (path.Length == folder.Length || path[folder.Length] == '/');

    /// <summary>How names compare at a provider.</summary>
    public static StringComparison ComparisonOf(ProviderCapabilities capabilities) =>
        capabilities.CaseInsensitiveNames ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <inheritdoc cref="ComparisonOf"/>
    public static StringComparer ComparerOf(ProviderCapabilities capabilities) =>
        StringComparer.FromComparison(ComparisonOf(capabilities));

    private static bool IsPathPart(string part) =>
        part is not ("." or "..") && !part.Contains('\\') && !part.Any(char.IsControl) && part.Length <= MaxNameLength;
}
