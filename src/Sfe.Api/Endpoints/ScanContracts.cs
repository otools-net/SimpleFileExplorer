namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Path">Folder to index, relative to the library root; empty for the whole library.</param>
public sealed record ScanRequest(string? Path);

/// <param name="Path">The folder, relative to the library root.</param>
/// <param name="Excluded">The folder is in the scans' excluded folders (<c>Scan:ExcludedFolders</c>).</param>
public sealed record IndexedFolder(
    string Path,
    bool Indexed,
    int Directories,
    int Files,
    int TaggedFiles,
    bool Excluded
);

/// <param name="Directories">Folders removed from the index (the folder and its sub-folders).</param>
/// <param name="TaggedFiles">Of the files, those kept as missing files with their tags (when relinking is on).</param>
/// <param name="Excluded">The folder was added to the excluded folders.</param>
public sealed record RemovedFromIndex(string Path, int Directories, int Files, int TaggedFiles, bool Excluded);

/// <summary>
/// Puts a folder on the lists that keep it out of scans, trickplay or thumbnails (true), or takes it off (false); null
/// leaves a list alone.
/// </summary>
/// <param name="Path">The folder, relative to the library root (not the root itself).</param>
public sealed record FolderExclusionChange(string? Path, bool? Scan, bool? Trickplay, bool? Thumbnails);

/// <summary>A folder on the lists that keep folders out of scans, trickplay or thumbnails, and which ones.</summary>
/// <param name="Path">The folder as listed, relative to the library root.</param>
/// <param name="Exists">The folder is on the disk (a folder renamed or deleted outside SFE stays listed until
/// removed).</param>
public sealed record ExcludedFolder(string Path, bool Scan, bool Trickplay, bool Thumbnails, bool Exists);
