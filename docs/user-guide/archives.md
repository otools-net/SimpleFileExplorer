# Archives

Clicking a zip, 7z, rar or tar file opens it in the [viewer](viewer.md) like a folder of its own: its folders and
files, their sizes and dates, without unpacking anything. Archives of any size open the same way, even with millions of
entries or folders nested thousands deep.

![An archive in the viewer](../images/archive-viewer.png)

## Moving around

- Click a folder (or double-click its row, or select it and press **Enter**) to open it. The bar at the top shows the
  way to it: click a folder of the way to go back to it. Deep down, the folders in between are shown as **…**.
- **Up** (or **Backspace**) goes to the folder above.
- **Go to folder**: type a folder's path inside the archive, with its folders separated by `/`
  (`Day 2 - Summit/Notes`), then **Enter**.
- ↑ / ↓ select a row. The bottom line counts the folder's folders and files, and the size of everything below it.

![A folder inside an archive](../images/archive-viewer-folder.png)

The first time an archive is opened, SFE reads its list of entries once (big archives say "Reading the list of
entries…" and how many so far); after that every folder opens at once, however big the archive is. A changed archive
is read again.

## Files inside an archive

With the right to download files, a file's name opens it in a new tab: pictures, videos, audio and plain text show
in the browser, anything else downloads. A right click on it offers **Open in new tab** and **Download**. Very large
files only download.

Some entries are only listed:

- **Links** (shown in italics): an archive made on Linux or macOS can hold them; they are never created when
  extracting.
- Entries **protected by a password** (a lock): opening an archive with its password comes later.
- Entries whose name would lead **out of the archive** (`../`, a drive, a root) or that are nested too deep are not
  listed at all; a line above the list says how many there are.

If the list stops (an archive cut off, or one with more entries than the server allows), a line above it says so:
what is shown is only the first part.

## What can be explored

| Kind | Explored |
|---|---|
| `.zip`, `.7z`, `.rar`, `.tar` | Yes |
| `.tar.gz` / `.tgz`, `.tar.bz2`, `.tar.xz` | Up to 2 GB (configurable): these have no list of their entries, so listing one means decompressing all of it |
| `.gz`, `.bz2`, `.xz` holding one file | No: they open in a new tab or download, as other files |

A file whose content isn't the archive its name says (a renamed file) answers that it can't be explored.

## Settings

Set in the configuration only ([configuration](../configuration.md)), under `Sfe:Files:Archives`:

| Key | Default | |
|---|---|---|
| `MaxEntries` | 10 000 000 | Entries listed at most; past it the list stops and says so |
| `MaxDepth` | 10 000 | Folders nested at most; deeper entries are left out |
| `MaxCompressedTarMegabytes` | 2048 | Largest `.tar.gz` / `.tar.bz2` / `.tar.xz` explored |
| `MaxPreviewMegabytes` | 64 | Largest file opened from an archive in a tab; larger ones only download |
| `MaxCompressionRatio` | 10 000 | A file claiming to be larger than this many times its packed size is refused (a "zip bomb") |
| `CatalogBuilds` | 1 | Archives' lists read at once by the whole server; others wait their turn |
| `CatalogMegabytes` | 1024 | Disk space the lists read may take per library, in the server's cache folder; the least recently used are deleted past it, and read again when needed |
