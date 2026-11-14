namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Checked">Videos whose fingerprint the comparison running (or the last one) has read.</param>
/// <param name="Total">Indexed videos that comparison looks at.</param>
/// <param name="Message">Why the last comparison failed.</param>
public sealed record SimilarVideoStatus(
    SimilarVideoState State,
    int Checked,
    int Total,
    DateTimeOffset? Started,
    DateTimeOffset? Finished,
    string? Message
);
