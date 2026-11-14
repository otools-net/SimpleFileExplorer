namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// A folder rule kept while its folder is out of the library (in the trash); its path relative to that folder.
/// </summary>
public sealed record KeptFolderRule(
    string Path,
    int? GroupId,
    int? UserId,
    FolderLevel Level,
    DateTime Created,
    int? CreatedById
);
