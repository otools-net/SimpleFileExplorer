namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// A tagged file that a scan no longer finds is remembered as missing, with its tags, instead of being forgotten.
/// When a scan then indexes the same file somewhere else (moved, renamed, or back after a drive was missing), its
/// tags move over to it.
/// </summary>
public sealed class RelinkOptions
{
    /// <summary>When off, files no longer found are removed from the index with their tags.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Files without a fingerprint are matched on their name, size and modification time instead. When off, only
    /// fingerprinted files are relinked.
    /// </summary>
    public bool MatchByName { get; set; } = true;

    /// <summary>Days a missing file is remembered; 0 = until it is found or forgotten by hand.</summary>
    public int RetentionDays { get; set; } = 30;
}
