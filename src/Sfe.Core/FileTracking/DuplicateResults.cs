namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="Summary">Totals over every page; only on the first page.</param>
/// <param name="Fingerprinting">Scans fingerprint files (<c>Scan:ComputeHashes</c>); without it no new file can be
/// compared.</param>
/// <param name="MinimumSize">Smaller files are left out (bytes).</param>
/// <param name="CanRemove">Copies can be removed (<c>POST /api/duplicates/remove</c>, see
/// <see cref="DuplicateRemover"/>).</param>
/// <param name="RemovesPermanently">A copy removed is deleted for good, not moved to the trash
/// (<c>Files:DeletePermanently</c>).</param>
public sealed record DuplicateResults(
    string Path,
    IReadOnlyList<DuplicateGroup> Groups,
    bool HasMore,
    string? Next,
    DuplicateSummary? Summary,
    bool Fingerprinting,
    long MinimumSize,
    bool CanRemove,
    bool RemovesPermanently = false
);
