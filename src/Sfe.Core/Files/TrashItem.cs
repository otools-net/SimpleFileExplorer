namespace Otools.Net.Sfe.Core.Files;

/// <summary>A file or folder in the trash, as the trash page lists it.</summary>
/// <param name="Path">Where it was, relative to the library root.</param>
/// <param name="Size">Bytes as the index knew them; null when it was not indexed.</param>
/// <param name="HasRules">A folder that had folder access rules (kept in the entry): its access isn't the
/// parent's.</param>
/// <param name="PreviousVersion">A copy of a file's content before it was saved over (the file is still where it
/// was).</param>
public sealed record TrashItem(
    int Id,
    string Path,
    string Name,
    bool IsFolder,
    long? Size,
    int? Files,
    DateTimeOffset DeletedAt,
    string? DeletedBy,
    int? DeletedById,
    bool HasRules = false,
    bool PreviousVersion = false
);
