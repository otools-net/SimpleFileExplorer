namespace Sfe.Core.Files;

/// <param name="Paths">Files and folders to move or copy (root-relative); the archive to extract.</param>
/// <param name="Target">The folder they go into.</param>
/// <param name="KeepAccess">Moved folders keep the access each group and account had (folder rules pinning it).</param>
/// <param name="KeepTags">Copies get the tags of their originals.</param>
/// <param name="Entries">Extract: the paths inside the archive to extract (each with what is below it); null: all of
/// it.</param>
/// <param name="NewFolder"> Extract: into a new folder named after the archive (or after its one top folder), rather
/// than straight into the target.
/// </param>
/// <param name="Name">Compress: the zip's name (<c>.zip</c> added); null: its only item's, else their folder's.</param>
/// <param name="Level">Compress: the deflate level, 0 (stored) to 9.</param>
/// <param name="SkipCompressed">Compress: files already compressed (pictures, videos, archives…) stored whatever the
/// level.</param>
public sealed record FileJobRequest(
    FileJobKind Kind,
    IReadOnlyList<string> Paths,
    string Target,
    ConflictChoice Conflicts = ConflictChoice.Ask,
    bool KeepAccess = false,
    bool KeepTags = false,
    IReadOnlyList<string>? Entries = null,
    bool NewFolder = false,
    string? Name = null,
    int Level = 6,
    bool SkipCompressed = true
);
