# Scanning

**Administration › Scanning**: what [scans](../user-guide/scanning.md) leave out, which files are photos, videos and
audio, and what scans read from them. Right: `settings.scanning`.

## What to scan

![Scanning › What to scan](../images/admin-scanning-skipped.png)

| Setting | |
|---|---|
| **Ignored folders** | Folder names skipped wherever they are, one per line (e.g. `$RECYCLE.BIN`) (`Sfe:Scan:IgnoredDirectories`) |
| **Ignored files** | File names skipped, one per line (e.g. `Thumbs.db`) (`Sfe:Scan:IgnoredFiles`) |
| **Excluded folders** | Folders of this space, as paths from the library root (e.g. `Downloads`, `Movies/Old`), left out of scans (`Sfe:Scan:ExcludedFolders`). **Remove from index…** with **Skip this folder in future scans** adds to it. A folder added by hand is removed from the index by the next scan of its parent |
| **Show hidden files and folders** | Off by default: hidden and system files (`Thumbs.db`, `desktop.ini`) are neither listed nor indexed. On Linux, hidden means a name starting with `.` (`Sfe:ShowHiddenItems`) |

Excluded folders are a setting of each space only.

## File types

![Scanning › File types](../images/admin-scanning-fileTypes.png)

**Photos**, **Videos** and **Audio**: the extensions of each kind (`Sfe:FileTypes:Images`, `Videos`, `Audio`). They
decide the previews, the viewer, what gets thumbnails and trickplay, and what `r:hr` / `r:lr` consider. Other files
are listed and searched as plain files.

## Indexing

![Scanning › Indexing](../images/admin-scanning-indexing.png)

| Setting | Default | |
|---|---|---|
| **Fingerprint files** | on | Reads a fingerprint of each file (its first megabyte) for [duplicates](duplicates-and-moves.md) and [moved files](duplicates-and-moves.md#moved-files) (`Sfe:Scan:ComputeHashes`) |
| **Read resolutions and durations** | on | Photos from their first bytes (JPEG, PNG, GIF, BMP, WebP; other formats through ffmpeg), videos and audio through ffmpeg (`Sfe:Scan:ReadResolution`) |
| **Folders scanned at once** | 4 | A server setting (`Sfe:Scan:Parallelism`) |

Reading durations makes the first scan of many videos or songs slower. Files indexed before it was on are read by the
next scan; without ffmpeg, videos and audio are read once it is set up.
