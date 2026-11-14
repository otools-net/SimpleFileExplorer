namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Groups">Groups with a video in the folder asked for.</param>
/// <param name="Videos">Indexed videos in the whole library when the comparison ran.</param>
/// <param name="Compared">Of those, the videos that had a fingerprint (trickplay) and were compared.</param>
/// <param name="MinimumPercent">Match the comparison listed groups from.</param>
public sealed record SimilarVideoSummary(
    int Groups,
    int Files,
    long WastedBytes,
    int Videos,
    int Compared,
    int MinimumPercent
);
