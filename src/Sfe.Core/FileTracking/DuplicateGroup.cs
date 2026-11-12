using Sfe.Core.Library;

namespace Sfe.Core.FileTracking;

/// <param name="Fingerprint">The files' partial SHA1 (see <see cref="Indexing.FileFingerprint"/>).</param>
/// <param name="Count">Files in the group; <paramref name="Files"/> lists at most
/// <see cref="DuplicateFinder.MaxFilesPerGroup"/>.</param>
/// <param name="WastedBytes">Space taken by all copies but one.</param>
public sealed record DuplicateGroup(
    long Size,
    string Fingerprint,
    int Count,
    long WastedBytes,
    IReadOnlyList<LibraryItem> Files
);
