namespace Sfe.Core.FileTracking;

/// <param name="Summary">Totals of the last comparison; only on the first page, null before any comparison.</param>
/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="TrickplayEnabled">Trickplay is made in the background: videos get the fingerprints compared
/// here.</param>
public sealed record SimilarVideoResults(
    string Path,
    SimilarVideoStatus Status,
    SimilarVideoSummary? Summary,
    IReadOnlyList<SimilarVideoGroup> Groups,
    bool HasMore,
    string? Next,
    bool TrickplayEnabled
);
