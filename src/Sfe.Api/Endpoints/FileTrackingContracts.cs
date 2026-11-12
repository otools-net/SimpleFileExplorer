using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Path">File of the library (relative to the root) that the missing file became.</param>
public sealed record RelinkRequest(string? Path);

/// <param name="Path">Copy to delete from the disk, relative to the library root.</param>
/// <param name="Keep">Identical copy that is kept and gets the tags of the deleted one.</param>
public sealed record RemoveDuplicateRequest(string? Path, string? Keep);

/// <param name="Kept">Tags of the copy kept, now including those of the deleted one.</param>
public sealed record RemovedDuplicate(string Removed, FileTags Kept, int? TrashId = null);

/// <param name="Forgotten">Missing files forgotten.</param>
public sealed record ForgetResult(int Forgotten);
