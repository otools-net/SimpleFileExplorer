namespace Otools.Net.Sfe.Core.Trickplay;

/// <summary>A video the job finished with.</summary>
/// <param name="Error">Why there is no trickplay, when <paramref name="Succeeded"/> is false.</param>
/// <param name="Seconds">Time ffmpeg took.</param>
public sealed record TrickplayResult(
    string Path,
    bool Succeeded,
    string? Error,
    DateTimeOffset Finished,
    double Seconds
);
