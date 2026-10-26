namespace Sfe.Core.Files.Compression;

/// <summary>
/// The one check of the names inside an archive, used to list and to extract: names are written by strangers, and a
/// name such as <c>../../x</c> or <c>C:\Windows\x</c> is how an archive writes outside its folder (zip-slip). A refused
/// name is left out and listed, never fixed into another place.
/// </summary>
public static class ArchiveEntryNames
{
    /// <summary>Longest segment kept for listing (names are checked again against the system when extracted).</summary>
    public const int MaxSegmentLength = 1024;

    /// <summary>Longest whole path kept.</summary>
    public const int MaxPathLength = 32 * 1024;

    /// <summary>
    /// The segments of <paramref name="name"/> (<c>\</c> read as <c>/</c>; empty and <c>.</c> segments dropped), or
    /// null with <paramref name="reason"/> when the name is refused: <c>..</c>, a root or drive (<c>/x</c>,
    /// <c>C:</c>, <c>\\server</c>), NUL or other control characters, too long or too deep.
    /// </summary>
    public static string[]? Split(string name, int maxDepth, out string? reason)
    {
        reason = null;
        if (name.Length > MaxPathLength)
            return Refuse("the path is too long", out reason);
        if (name.Any(char.IsControl))
            return Refuse("the name holds control characters", out reason);
        var path = name.Replace('\\', '/');
        if (path.StartsWith('/'))
            return Refuse("the path starts at a root", out reason);
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
            return Refuse("the path starts with a drive", out reason);

        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
                return Refuse("the path goes up (“..”)", out reason);
            if (segment.Length > MaxSegmentLength)
                return Refuse("a name is too long", out reason);
            // Trailing dots and spaces vanish on Windows: "..." or ". ." would be ".." there.
            if (segment.TrimEnd('.', ' ').Length == 0)
                return Refuse("a name is only dots and spaces", out reason);
            segments.Add(segment);
            if (segments.Count > maxDepth)
                return Refuse("the folders are nested too deep", out reason);
        }
        if (segments.Count == 0)
            return Refuse("the name is empty", out reason);
        return [.. segments];
    }

    private static string[]? Refuse(string why, out string? reason)
    {
        reason = why;
        return null;
    }
}
