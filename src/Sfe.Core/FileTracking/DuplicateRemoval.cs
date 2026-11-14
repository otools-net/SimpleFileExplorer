using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Field">Request field an <see cref="DuplicateRemovalStatus.Invalid"/> error is about.</param>
/// <param name="Kept">Tags of the copy kept, now including those of the deleted one.</param>
/// <param name="TrashId">The trash entry of the copy removed, for an Undo; null when it was deleted for good.</param>
public sealed record DuplicateRemoval(
    DuplicateRemovalStatus Status,
    string? Error = null,
    string? Field = null,
    FileTags? Kept = null,
    int? TrashId = null
)
{
    public static DuplicateRemoval Fail(DuplicateRemovalStatus status, string error, string? field = null) =>
        new(status, error, field);
}
