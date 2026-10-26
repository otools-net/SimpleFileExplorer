# Simple File Explorer (SFE)

A self-hosted web file explorer for a home media library: browse folders, view photos, videos, audio, documents and
archives, search an index of the whole library, tag files, manage them, and find duplicates and similar videos. It
runs on Windows, Linux or macOS, on one machine or a private network, with accounts, groups and several libraries
("spaces").

> [!WARNING]
> **Alpha version.** SFE is still being tested and hasn't been proven in production yet. Keep a backup of your files
> and of SFE's data, and expect changes between versions.

![SFE showing a folder of photos](docs/images/library-grid.png)

## Why SFE

- **Free and open source** (MIT): no account to create, no subscription, no limits on files or users.
- **Your folders stay as they are**: SFE shows the folders you already have, on a local disk or a network share. No
  import, no copies, no hidden storage format: other programs keep working on the same files, and uninstalling SFE
  leaves them untouched.
- **Up in a few minutes**: one portable download (no installer, no database server: SQLite is built in), then a setup
  page in the browser; nothing to edit by hand. Or a Docker image for PCs, NASes and Raspberry Pi 4/5 (amd64, arm64).
- **Works offline and stays private**: nothing is sent anywhere, and no part of the page comes from the internet.
- **For the whole family**: accounts and groups, guests browsing without an account where you allow it, rights down
  to a single folder, and several libraries on one server.
- **On every device**: the same app on a desktop, a tablet or a phone, in the browser; no app to install.
- **Plays almost anything**: videos the browser can't read are converted while they play (with ffmpeg), or open in
  VLC or another player on your device.
- **Fast on big libraries, light on the server**: millions of files and huge archives open at once; the server takes
  around 150 MB of memory.
- **Hard to lose files with**: deletes go to a trash you can restore from, and nothing is ever left half copied.

## Features

- **Browse** folders in grid, list or large-preview layouts, with folder pictures, resolutions, durations and
  previews of videos on hover; quick links; light and dark themes.
- **View** every common format full screen (below), each with its own tools: a video player with trickplay previews,
  find and go to line in text, an outline for Markdown, a table for CSV, page layouts for books.
- **Search** the index with a small query language (size, type, resolution, duration, tags, paths), explained as you
  type.
- **Tag** files, selections, whole folders or every search result; tags with parents, aliases and colors; default
  tags given to what enters a folder.
- **Manage files**: upload (resumable, whole folders), move, copy, rename one or many at once, delete to a trash,
  compress into a zip, extract archives, edit text files and crop pictures.
- **Keep the library tidy**: duplicates, similar videos, tagged files that were moved or deleted.
- **Administer** accounts, groups, rights per space and per folder, settings by level; schedule scans and other
  background tasks; read the server's logs; download a backup.

## Supported formats

| Kind | Formats | How |
|---|---|---|
| Pictures | JPEG, PNG, GIF (animated too), WebP, AVIF, BMP, TIFF | Viewer with zoom, thumbnails, crop in the browser |
| Videos | MP4, WebM, MKV, MOV, AVI, M4V, WMV, MPEG, TS, FLV, 3GP | The app's player, trickplay; converted by ffmpeg while playing when the browser can't read them; external players (VLC…) |
| Audio | MP3, FLAC, M4A, AAC, OGG, Opus, WAV | Player in the viewer |
| Text and code | Plain text, logs, configuration, over 100 programming languages highlighted | Read, find, follow a growing log, edit and save (encodings and line endings kept) |
| Documents | Markdown, JSON, CSV / TSV, SRT / VTT subtitles, PDF, EPUB | Rendered (Markdown), a tree (JSON), a table (CSV), cues played in their video (subtitles), pages (PDF, EPUB) |
| Flash | SWF | Played by Ruffle, bundled |
| Archives | ZIP (Zip64, AES and ZipCrypto passwords; Deflate, Deflate64, BZip2, LZMA, Zstandard, XZ, PPMd), 7Z, RAR 4 and 5, TAR, TAR.GZ / .BZ2 / .XZ | Explored without unpacking (millions of entries), their files opened in place; extracted; GZ, BZ2 and XZ files extracted |
| Containers | CBZ / CBR, EPUB, DOCX / XLSX / PPTX, ODT, JAR, APK, NUPKG… | **Open as archive**: the files they are made of |

Which extensions count as pictures, videos, audio and text is a setting of each server. Zips are made with Compress
(levels 0–9, AES-256 passwords).

## Tools

- **Index and scans**: the library's files, sizes, dates, resolutions and durations in a database, kept current by
  scans (scheduled, or folder by folder); folders can be kept out of scans, thumbnails or trickplay.
- **Thumbnails and trickplay**: pictures of files and folders, frames along each video (played on hover).
- **Duplicates** (by content) and **similar videos** (by their frames), with removal; **missing files**: tagged files
  moved or deleted outside SFE, relinked.
- **Editors**: text files (saved over, or as a copy; the previous version kept in the trash), pictures (cropped, saved
  as a new file or over the original).
- **Files**: properties with folder totals and checksums, batch rename with a preview, operations panel for moves,
  copies, uploads, extractions and compressions running in the background.
- **Server**: background tasks with history and logs, schedules, the server's log searched by text and severity,
  backups, a keep-alive for library folders on network shares, a startup page and version check after upgrades.
- **API**: everything the web client does is a documented JSON API (OpenAPI).

## Interface

- **One page, any screen**: an Angular single-page app that works the same with a mouse, a keyboard or a finger. On
  phones, menus and popups become sheets at the bottom of the screen, and rows of buttons that don't fit fold into a
  "⋯" menu rather than wrapping.
- **One right-click menu** everywhere (files, folders, the viewer, archive entries), offering what the item's
  buttons and keys already do, including **Open in new tab**; every button says what it does and its shortcut.
- **Big folders stay fast**: listings and archives load in pages as you scroll, and only the rows on screen are drawn.
- **Live**: folders changed by someone else refresh in place; long operations show their progress and can be
  cancelled.
- **Light on the device**: viewers are loaded only when a file needs them; nothing polls while the tab is hidden; the
  theme is applied before the first paint (no flash).
- **Readable**: reading settings (fonts, size, width, colours) for text and books, per profile.

## Principles

- **Fully local and private**: no CDN, web fonts, telemetry or external call; every library the client uses is
  bundled. Nothing leaves your network.
- **Built for big libraries**: millions of files and archives with millions of entries; no request costs in
  proportion to a folder or the index (paged, keyset queries, streamed downloads).
- **Your files are safe**: deletes go to a trash, an operation cut short leaves nothing half written, a saved text
  keeps its previous version, archives can't write outside their folder, and zip bombs are refused.
- **Content is untrusted**: library files are served sandboxed and never run as pages of the app; hostile archive
  names and links are left out, not fixed.
- **Every action has a right**, checked on the server, per space and per folder; the client only hides what the
  server would refuse.
- **Upgrades keep installs working**: each change to the database, settings or stored files ships with its tested
  migration, on SQLite and SQL Server alike.
- **Runs anywhere**: portable packages for Windows and Linux, a Docker image, IIS, a service; Local mode for one
  computer, External for a network.

## Quick start

1. Get SFE: a portable package (`SFE-<version>-win-x64.zip` or `SFE-<version>-x86_64.AppImage`), or from source with the
   .NET 10 SDK and Node.js:

   ```sh
   dotnet run --project src/Sfe.Api          # http://localhost:5080
   ```

2. Start it: `SFE\Sfe.Api.exe` on Windows (it opens your browser), the AppImage on Linux, or the command above.
3. Open `http://localhost:5080`. On the computer SFE runs on, no code is asked; from another device, enter the
   **setup code** shown in SFE's window (also kept in `data/setup-code.txt`).
4. Choose how SFE is used (**Local**: on this computer, for its administrators; **External**: opened by others), pick
   the folder of your library, then create the administrator. No file to edit; services and containers can still set
   everything in the configuration (a [Docker image](docs/docker.md) is External).
5. ⋮ menu › **Scan this folder** to index the library.

Optional: [ffmpeg](https://ffmpeg.org/) (on the `PATH`, or its folder picked on the setup page) for video thumbnails,
trickplay, similar videos, durations and playing videos the browser can't read; SQL Server instead of the built-in
SQLite database.

## Documentation

| | |
|---|---|
| [Installation](docs/installation.md) | Portable packages, from source, IIS, Windows service, systemd |
| [Docker](docs/docker.md) | The container image |
| [First start](docs/first-start.md) | The setup code, the first administrator, a lost password |
| [Configuration](docs/configuration.md) | `appsettings.json`, environment variables, the database |
| [Upgrading](docs/upgrading.md) | What each version changes on an existing install |
| [User guide](docs/README.md#using-sfe) | Browsing, viewer, text files, archives, player, editors, managing files, search, tags, scanning |
| [Administration](docs/administration/README.md) | Every page of the administration page |
| [Reference](docs/README.md#reference) | Rights, settings levels, search syntax, API |

All documentation: [docs/](docs/README.md). Building, testing and the code: [AGENTS.md](AGENTS.md).
