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
- Entries **protected by a password** (a lock): see [below](#archives-with-a-password).
- Entries whose name would lead **out of the archive** (`../`, a drive, a root) or that are nested too deep are not
  listed at all; a line above the list says how many there are.

If the list stops (an archive cut off, or one with more entries than the server allows), a line above it says so:
what is shown is only the first part.

## Archives with a password

An archive whose list itself is protected (a 7z or rar made with "encrypt file names") asks for its password as it
opens. One whose files only are protected shows its list with a lock on those files: click one, or **Enter the
password**, to give it.

- Type the password and **Open** (**Show** shows what you type). A wrong one says so; after 10 tries in a minute, wait
  a minute.
- Once given, the archive's files open as the others do, for 15 minutes after you last use it. It is asked again after
  that, after signing out, or when the archive changes.
- The password stays on the server, in its memory, for you only: it is never written anywhere nor sent back.
- A **zip**'s password protects only its files' content: their names, sizes and dates stay readable by anyone with
  the file. A 7z or rar can hide its list too.
- Zips encrypted with AES (128 or 256 bits) and with the older ZipCrypto open, as do 7z and rar archives (rar 4 and 5)
  with a password.

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
| `MaxEncryptedListEntries` | 500 000 | Entries listed at most for an archive whose list is protected (kept in the server's memory, never on its disk, while the password is) |
| `MaxPreviewMegabytes` | 64 | Largest file opened from an archive in a tab; larger ones only download |
| `MaxCompressionRatio` | 10 000 | A file claiming to be larger than this many times its packed size is refused (a "zip bomb") |
| `CatalogBuilds` | 1 | Archives' lists read at once by the whole server; others wait their turn |
| `CatalogMegabytes` | 1024 | Disk space the lists read may take per library, in the server's cache folder; the least recently used are deleted past it, and read again when needed |
