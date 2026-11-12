using Sfe.Core.Access;
using Sfe.Core.Files;

namespace Sfe.Api.Endpoints;

/// <param name="Path">The folder to create the new one in (root-relative; empty for the root).</param>
/// <param name="Name">The new folder's name.</param>
public sealed record CreateFolderRequest(string? Path, string? Name);

public sealed record CreateFileRequest(string? Path, string? Name);

/// <param name="Path">The file or folder to rename (root-relative).</param>
/// <param name="Name">Its new name, in the same folder.</param>
public sealed record RenameRequest(string? Path, string? Name);

/// <param name="Paths">The files and folders to delete (root-relative).</param>
/// <param name="Permanent">Skip the trash (always, when <c>Files:DeletePermanently</c> is on).</param>
public sealed record DeleteRequest(IReadOnlyList<string>? Paths, bool Permanent = false);

/// <param name="TrashId">Its entry in the trash, for an Undo; null when deleted for good or not deleted.</param>
public sealed record DeletedItem(string Path, FileOperationStatus Status, string? Message = null, int? TrashId = null);

public sealed record DeleteResults(IReadOnlyList<DeletedItem> Items, bool Permanent);

public sealed record EmptiedTrash(int Purged);

/// <param name="Paths">What would move.</param>
/// <param name="Target">The folder it would go into.</param>
public sealed record MoveCheckRequest(IReadOnlyList<string>? Paths, string? Target);

/// <summary>
/// Someone who would see or change more of a moved folder than before (its new parent gives more access).
/// </summary>
/// <param name="Who">The group's or account's name ("Visitors" for the Guests group).</param>
public sealed record AccessGain(string Folder, string Who, bool IsGroup, FolderLevel Before, FolderLevel After);

public sealed record MoveCheck(IReadOnlyList<AccessGain> Gains);

public sealed record JobAnswer(ConflictChoice Choice, bool ForAll = false);

/// <param name="Paths">Files and folders to compress (root-relative).</param>
/// <param name="Target">The folder the zip goes into.</param>
/// <param name="Name">The zip's name (<c>.zip</c> added); null: its only item's, else their folder's.</param>
/// <param name="Level">0 (stored) to 9 (smallest, slowest); 6 by default.</param>
/// <param name="SkipCompressed">Pictures, videos, archives… stored whatever the level.</param>
/// <param name="Password">Encrypts the files with AES-256 (the names stay readable); held by the job only while it
/// runs.</param>
public sealed record CompressRequest(
    IReadOnlyList<string>? Paths,
    string? Target,
    string? Name = null,
    int Level = 6,
    bool SkipCompressed = true,
    string? Password = null,
    ConflictChoice Conflicts = ConflictChoice.Ask
);
