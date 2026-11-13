using Microsoft.AspNetCore.Mvc;
using Sfe.Core.Files;

namespace Sfe.Api.Endpoints;

/// <param name="Kind"><c>folder</c>, <c>file</c>, <c>link</c> (never created when extracting) or <c>other</c>.</param>
/// <param name="Packed">0 when the format doesn't tell it per entry (a solid 7z or rar).</param>
/// <param name="Folders">A folder's folders and entries right inside it, and its files in all.</param>
/// <param name="Where">In search results: the path of the folder it is in, its last folders after "…/" when
/// deep.</param>
/// <param name="In">In search results: the id of the folder it is in (to open it).</param>
/// <param name="Media">A file the library calls a picture, video or audio (<c>image</c>, <c>video</c>, <c>audio</c>:
/// <c>Sfe:FileTypes</c>), else null.</param>
public sealed record ArchiveItem(
    string Kind,
    long Id,
    string Name,
    long Size,
    long Packed,
    DateTime? Modified,
    bool Encrypted,
    long? Folders = null,
    long? Files = null,
    long? AllFiles = null,
    string? Where = null,
    string? Media = null,
    long? In = null
);

/// <param name="Items">Folders, then files, whose name holds the text; <c>Where</c> is the path of the folder each is
/// in.</param>
/// <param name="Next">Where to go on (a page cut short by its time has fewer items); null once all was looked
/// through.</param>
public sealed record ArchiveSearch(IReadOnlyList<ArchiveItem> Items, string? Next);

public sealed record ArchiveCrumb(long Id, string Name);

/// <param name="Skipped">Folders between the root and <paramref name="Last"/> (deep trees: the way is shown
/// collapsed).</param>
public sealed record ArchiveTrail(int Skipped, IReadOnlyList<ArchiveCrumb> Last);

/// <param name="Complete">False when the list stopped (a limit, damage): <paramref name="Stopped"/> says why.</param>
/// <param name="NeedsPassword">The archive's list is encrypted and no unlock was given (or it expired): ask for
/// it.</param>
/// <param name="Protected">Some of its files are encrypted: opening them needs the password.</param>
/// <param name="Unlocked">The unlock given opens this archive (its list was read with the password, or its files
/// open).</param>
/// <param name="Folder">The folder listed: its totals (<c>AllFiles</c>, <c>Size</c>: its whole tree).</param>
/// <param name="Next">The cursor of the next page; null on the last one.</param>
/// <param name="Path">The folder's whole path in the archive ("" at its top): on a first page, for a folder at most
/// <see cref="ExploreEndpoints.MaxPathDepth"/> deep (links between its files are resolved from it); else null.</param>
public sealed record ArchiveListing(
    string Format,
    bool Complete,
    string? Stopped,
    bool NeedsPassword,
    long Entries,
    long LeftOut,
    ArchiveItem Folder,
    ArchiveTrail Trail,
    IReadOnlyList<ArchiveItem> Items,
    string? Next,
    bool Protected = false,
    bool Unlocked = false,
    string? Path = null
);

public sealed record ArchiveUnlockRequest(string? Path, string? Password);

/// <param name="Unlock">Sent back with later requests about this archive (header <c>X-SFE-Archive-Unlock</c>, or
/// <c>unlock</c> in an entry's address); 15 minutes unused, a sign-out or a change of the archive end it.</param>
public sealed record ArchiveUnlocked(string Unlock);

/// <param name="Read">Entries read so far.</param>
public sealed record ArchiveReading(long Read);

/// <param name="Archive">The archive's path in the library.</param>
/// <param name="Target">The folder to extract into.</param>
/// <param name="NewFolder">Into a new folder named after the archive (or after its one top folder); else straight into
/// <paramref name="Target"/>.</param>
/// <param name="Folders">Folders of the archive to extract (ids of its listing), with what they hold; none with
/// <paramref name="Files"/>: all of it.</param>
/// <param name="Files">Files of the archive to extract (ids of its listing).</param>
/// <param name="Unlock">The unlock of the archive (from <c>explore/unlock</c>), or its
/// <paramref name="Password"/>.</param>
public sealed record ExtractRequest(
    string? Archive,
    string? Target,
    bool NewFolder = true,
    IReadOnlyList<long>? Folders = null,
    IReadOnlyList<long>? Files = null,
    ConflictChoice Conflicts = ConflictChoice.Ask,
    string? Unlock = null,
    string? Password = null
);

/// <summary>A page of a folder of an archive (<c>GET api/files/explore</c>).</summary>
public sealed record ArchiveListQuery(
    [FromQuery(Name = "path")] string? Path,
    [FromQuery(Name = "folder")] long? Folder,
    [FromQuery(Name = "at")] string? At,
    [FromQuery(Name = "after")] string? After,
    [FromQuery(Name = "limit")] int? Limit,
    [FromHeader(Name = ExploreEndpoints.UnlockHeader)] string? Unlock
);

/// <summary>A search in an archive (<c>GET api/files/explore/search</c>).</summary>
public sealed record ArchiveSearchQuery(
    [FromQuery(Name = "path")] string? Path,
    [FromQuery(Name = "q")] string? Q,
    [FromQuery(Name = "folder")] long? Folder,
    [FromQuery(Name = "after")] string? After,
    [FromQuery(Name = "limit")] int? Limit,
    [FromHeader(Name = ExploreEndpoints.UnlockHeader)] string? Unlock
);

/// <summary>
/// How a file of an archive is served (<c>GET api/files/explore/entry/…</c>): as a download, and with which unlock (a
/// query value, for the links of a document).
/// </summary>
public sealed record ArchiveEntryQuery(
    [FromQuery(Name = "download")] bool? Download,
    [FromQuery(Name = "unlock")] string? Unlock
);

/// <summary>A text file of an archive (<c>GET api/files/explore/text</c>).</summary>
public sealed record ArchiveTextQuery(
    [FromQuery(Name = "path")] string? Path,
    [FromQuery(Name = "id")] long? Id,
    [FromQuery(Name = "at")] string? At,
    [FromQuery(Name = "encoding")] string? Encoding,
    [FromHeader(Name = ExploreEndpoints.UnlockHeader)] string? Unlock
);
