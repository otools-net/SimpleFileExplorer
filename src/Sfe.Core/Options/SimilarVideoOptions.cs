namespace Sfe.Core.Options;

/// <summary>
/// Videos with the same pictures but other bytes (another resolution, bitrate or codec, trimmed), found from the
/// fingerprints made with their trickplay (<see cref="TrickplayOptions"/>): videos without trickplay are not compared.
/// </summary>
public sealed class SimilarVideoOptions
{
    /// <summary>
    /// Share of matching frames (out of the longer video) from which two videos are listed as similar. Applies from
    /// the next comparison.
    /// </summary>
    public int MinimumPercent { get; set; } = 80;
}
