namespace Sfe.Core.Files;

/// <summary>What the disk says of a file or folder, beyond what a listing shows.</summary>
/// <param name="Hidden">Hidden by the system: a name starting with a dot, or the hidden attribute (Windows).</param>
/// <param name="IsLink">A symbolic link or junction (its target is not told: it may be outside the library).</param>
public sealed record ItemDetails(
    string Path,
    string Name,
    bool IsFolder,
    long? Size,
    DateTimeOffset? Created,
    DateTimeOffset Modified,
    DateTimeOffset? Accessed,
    bool Hidden,
    bool ReadOnly,
    bool IsLink
);
