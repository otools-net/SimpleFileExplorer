# Installation

SFE runs on Windows, Linux or macOS. Pick one way to run it, then follow [First start](first-start.md) to create the
administrator.

- [Portable packages](#portable-packages): nothing else to install.
- [From source](#from-source): needs the .NET SDK and Node.js.
- As a service: [IIS](#iis), [Windows service](#windows-service), [Linux (systemd)](#linux-systemd).

Optional, for every way:

- [ffmpeg](https://ffmpeg.org/) on the `PATH` (`apt install ffmpeg` on Debian/Ubuntu): video thumbnails,
  [trickplay](administration/trickplay.md), [similar videos](administration/duplicates-and-moves.md#similar-videos),
  video and audio durations. Without it, listings show photos from the files themselves and videos with an icon;
  everything else works.
- SQL Server, instead of the built-in SQLite database (see [Database](configuration.md#database)).

## Portable packages

Self-contained packages that need no .NET install: `SFE-<version>-win-x64.zip` and `SFE-<version>-x86_64.AppImage`.
They start with neutral settings: library root `~` (your home folder), SQLite index, `ffmpeg` from the `PATH`, system
commands off, listening on **`http://localhost:5080` only** (set `"Urls": "http://*:5080"` in their
`appsettings.json` to open it to the network).

Before the first start, add a setup code to their `appsettings.json` (`"Setup": { "Code": "…" }` in the `Sfe`
section): see [First start](first-start.md).

- **Windows:** unzip, run `SFE\Sfe.Api.exe`. Settings (`appsettings.json`) and data (`data\`: index, thumbnails, saved
  settings) stay in that folder, which must be writable.
- **Linux:** `chmod +x SFE-*.AppImage` and run it. Settings and data live in `~/.config/sfe` (or `$SFE_HOME`); the first
  start copies the default `appsettings.json` there: edit it to set the library root. To keep everything next to the
  AppImage, create a `SFE-….AppImage.config` folder beside it. Without FUSE, run it with `--appimage-extract-and-run`.
  Without ICU (`libicu`), the app runs culture-invariant.
- Any setting can be given on the command line, e.g. `Sfe.Api.exe --Sfe:Root=D:\Media --urls http://*:8080`.

## From source

Needs the .NET 10 SDK and [Node.js](https://nodejs.org/) 22.22 or 24.15+ (npm builds the web client).

```sh
dotnet run --project src/Sfe.Api          # http://localhost:5080, Development settings
```

The Development settings browse your home folder (`"Root": "~"`) and keep the index in SQLite
(`src/Sfe.Api/data/sfe-dev.db`). Point them elsewhere with `Sfe__Root=/srv/media`.

## IIS

1. `dotnet publish src/Sfe.Api -c Release` (Node.js needed to build).
2. Install the [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) and point a site at
   the output.
3. The app pool identity needs read access to the library and write access to the thumbnail cache.

The application folder is usually not writable under IIS: point the saved settings elsewhere, e.g.
`Sfe__SettingsFile=D:\sfe-data\settings.json` (see [Configuration](configuration.md#the-settings-file)). To let the
[picture editor](user-guide/picture-editor.md) save pictures over 30 MB, raise IIS's own request limit
(`maxAllowedContentLength`).

## Windows service

```bat
sc.exe create SFE binPath= "C:\sfe\Sfe.Api.exe" start= auto
```

Use a service account that can read the library drives.

## Linux (systemd)

`dotnet publish src/Sfe.Api -c Release -o /opt/sfe` (add `-r linux-x64 --self-contained` without the .NET runtime),
then `/etc/systemd/system/sfe.service`:

```ini
[Unit]
Description=Simple File Explorer
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/sfe
ExecStart=/opt/sfe/Sfe.Api
User=sfe
Environment=Sfe__Root=/srv/media
Environment=Sfe__Database__Provider=Sqlite
Environment=ConnectionStrings__Sfe="Data Source=/var/lib/sfe/sfe.db"
Environment=Sfe__Thumbnails__CacheDirectory=/var/lib/sfe/thumbs
Environment=Sfe__SettingsFile=/var/lib/sfe/settings.json
# For the first start only: the code the setup page asks for (remove it once the administrator exists).
Environment=Sfe__Setup__Code=pick-a-long-code-nobody-guesses
StateDirectory=sfe
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Then `systemctl enable --now sfe`; it logs to the journal. The `sfe` user needs read access to the library.

## Network address

The default listening address is `http://*:5080` (`Urls` in `appsettings.json`), except for the portable packages
(localhost only).

## Fully local

The application never calls an external service. The web client and the API reference are served by the app itself,
with no CDN, web fonts, telemetry or online validator. The only outbound connections are the ones you configure: the
database server, and the [data server](administration/playback.md#streaming-data-server) when it is set.
