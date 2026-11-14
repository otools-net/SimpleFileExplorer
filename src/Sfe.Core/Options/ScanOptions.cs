namespace Otools.Net.Sfe.Core.Options;

public sealed class ScanOptions
{
    /// <summary>Folder names skipped by the indexer.</summary>
    public List<string> IgnoredDirectories { get; set; } = [];

    /// <summary>File names skipped by the indexer.</summary>
    public List<string> IgnoredFiles { get; set; } = [];

    /// <summary>
    /// Library folders (relative to the root, with their sub-folders) that scans skip: what is already indexed there
    /// (files, tags) stays as it is, never refreshed, until taken off the list. Removing a folder from the index
    /// (<c>DELETE api/index/folder</c>) forgets it.
    /// </summary>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>Directories indexed concurrently.</summary>
    public int Parallelism { get; set; } = 4;

    /// <summary>Compute the partial SHA1 fingerprint of each file (reads up to 1 MB per new/changed file).</summary>
    public bool ComputeHashes { get; set; } = true;

    /// <summary>
    /// Read the resolution of each new or changed image and video, and the duration of videos and audio files, for the
    /// listings and the <c>w:</c>, <c>h:</c>, <c>r:</c> and <c>d:</c> search terms. Images are read from their first
    /// bytes; videos and audio need ffmpeg, which makes scans of new ones slower. Files indexed before it was turned on
    /// (and videos and audio indexed before durations were read) are read by the next scan.
    /// </summary>
    public bool ReadResolution { get; set; } = true;
}
