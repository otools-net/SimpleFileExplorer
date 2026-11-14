namespace Otools.Net.Sfe.Core.Indexing;

/// <param name="FilesIndexed">New or changed files written to the index.</param>
/// <param name="Removed">Files and folders removed from the index because they no longer exist.</param>
/// <param name="DirectoriesQueued">Folders found and still waiting to be scanned.</param>
/// <param name="Relinked">Missing (tagged) files found again after the scan, moved or renamed, and given back their
/// tags.</param>
/// <param name="Missing">Tagged files no longer found, waiting to be relinked (after the scan; null before).</param>
public sealed record ScanStatus(
    ScanState State,
    string? Path,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int DirectoriesScanned,
    int DirectoriesQueued,
    int FilesIndexed,
    int Removed,
    int Errors,
    string? CurrentDirectory,
    string? Message,
    int Relinked = 0,
    int? Missing = null
);
