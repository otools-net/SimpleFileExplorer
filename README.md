# Simple File Explorer (SFE)

A self-hosted web file explorer for a home media library: browse folders, preview photos, video and audio, search an
index of the whole library, tag files, and find duplicates and similar videos. It runs on Windows, Linux or macOS, on
one machine or a private network, with accounts, groups and several libraries ("spaces").

![SFE showing a folder of photos](docs/images/library-grid.png)

## Features

- **Browse** folders in grid, list or large-preview layouts, with folder pictures, resolutions and durations.
- **View** photos, videos, audio and text files full screen (text and code highlighted, with find); a video player
  with trickplay previews; open files in VLC and other players.
- **Search** the index with a small query language (size, type, resolution, duration, tags, paths), explained as you
  type.
- **Tag** files, selections, whole folders or every search result; tags with parents, aliases and colors.
- **Crop** pictures in the browser and save them as new files.
- **Find** duplicate files, similar videos, and tagged files that were moved.
- **Administer** accounts, groups, rights and spaces; schedule scans and other background tasks.
- **Fully local:** no CDN, web fonts or telemetry; nothing leaves your network.

## Quick start

1. Get SFE: a portable package (`SFE-<version>-win-x64.zip` or `SFE-<version>-x86_64.AppImage`), or from source with the
   .NET 10 SDK and Node.js:

   ```sh
   dotnet run --project src/Sfe.Api          # http://localhost:5080
   ```

2. Start it: `SFE\Sfe.Api.exe` on Windows (it opens your browser), the AppImage on Linux, or the command above.
3. Open `http://localhost:5080`. On the computer SFE runs on, no code is asked; from another device, enter the
   **setup code** shown in SFE's window (also kept in `data/setup-code.txt`).
4. Pick the folder of your library, who may open SFE (this computer or your whole network), then create the
   administrator. No file to edit; services and containers can still set everything in the configuration.
5. ⋮ menu › **Scan this folder** to index the library.

Optional: [ffmpeg](https://ffmpeg.org/) (on the `PATH`, or its folder picked on the setup page) for video thumbnails, trickplay, similar videos and durations;
SQL Server instead of the built-in SQLite database.

## Documentation

| | |
|---|---|
| [Installation](docs/installation.md) | Portable packages, from source, IIS, Windows service, systemd |
| [First start](docs/first-start.md) | The setup code, the first administrator, a lost password |
| [Configuration](docs/configuration.md) | `appsettings.json`, environment variables, the database |
| [Upgrading](docs/upgrading.md) | What each version changes on an existing install |
| [User guide](docs/README.md#using-sfe) | Browsing, viewer, player, editor, search, tags, scanning |
| [Administration](docs/administration/README.md) | Every page of the administration page |
| [Reference](docs/README.md#reference) | Rights, settings levels, search syntax, API |

All documentation: [docs/](docs/README.md). Building, testing and the code: [AGENTS.md](AGENTS.md).
