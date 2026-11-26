using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Item">As listings show it, with what the index knows (tags, resolution, duration).</param>
/// <param name="CanChecksum">The caller may read the file whole for a checksum (right `files.download`).</param>
/// <param name="Access">What the caller may do with it and what the disk allows (Properties' Access section).</param>
public sealed record ItemProperties(
    LibraryItem Item,
    ItemDetails Details,
    bool CanChecksum,
    ItemAccess? Access = null
);

/// <param name="Actions">The actions that apply to the item (a file, a folder, the top), each allowed or not.</param>
/// <param name="RuleFolder">
/// The folder whose rule decides what the caller may do here, root-relative (empty: the top); null without folder
/// rules, or when only a group's default level applies.
/// </param>
/// <param name="Disk">
/// Why the disk takes no change where the item is (the folder itself, a file's folder): `link`, `diskReadOnly` or
/// `diskRefused`; null when it takes them. Asked whatever the caller's rights.
/// </param>
public sealed record ItemAccess(IReadOnlyList<ActionAccess> Actions, string? RuleFolder, ReadOnlyReason? Disk);

/// <param name="Reason">The first reason refusing it, SFE's before the disk's; null when allowed.</param>
public sealed record ActionAccess(ItemAction Action, bool Allowed, ReadOnlyReason? Reason);

/// <summary>What Properties tells the caller it may do with an item.</summary>
public enum ItemAction
{
    /// <summary>A file opened in the viewer (`files.view`), a folder browsed (`library.browse`).</summary>
    Open,

    /// <summary>Downloaded (`files.download`; a folder as a zip).</summary>
    Download,

    /// <summary>
    /// A file saved over (the Replace rule: `files.delete` at it, `files.createFiles` in its folder).
    /// </summary>
    SaveOver,

    /// <summary>Folders, files or uploads put in a folder (any of the rights making them).</summary>
    CreateInside,

    /// <summary>Renamed (`files.rename`).</summary>
    Rename,

    /// <summary>Moved elsewhere (`files.move`).</summary>
    Move,

    /// <summary>Deleted, to the trash (`files.delete`).</summary>
    Delete,
}

public sealed record CountRequest(string? Path);

public sealed record ChecksumRequest(string? Path, ChecksumAlgorithm Algorithm = ChecksumAlgorithm.Sha256);
