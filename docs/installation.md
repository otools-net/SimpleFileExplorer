# Installation

SFE runs on Windows, Linux or macOS. Pick one way to run it, then follow [First start](first-start.md) to create the
administrator.

- [Portable packages](#portable-packages): nothing else to install.
- [From source](#from-source): needs the .NET SDK and Node.js.
- As a service: [IIS](#iis), [Windows service](#windows-service), [Linux (systemd)](#linux-systemd).

Optional, for every way:

- [ffmpeg](https://ffmpeg.org/) on the `PATH` (`apt install ffmpeg` on Debian/Ubuntu), or unzipped anywhere and its
  folder picked on the setup page: video thumbnails,
  [trickplay](administration/trickplay.md), [similar videos](administration/duplicates-and-moves.md#similar-videos),
  video and audio durations. Without it, listings show photos from the files themselves and videos with an icon;
  everything else works.
- SQL Server, instead of the built-in SQLite database (see [Database](configuration.md#database)).

## Portable packages

Self-contained packages that need no .NET install: `SFE-<version>-win-x64.zip` and `SFE-<version>-x86_64.AppImage`.
Nothing needs editing before the first start: the [setup page](first-start.md#the-setup-page) asks for the library
folder, who may open SFE and ffmpeg. They start with neutral settings: library root `~` (your home folder) until you
pick one, SQLite index, `ffmpeg` from the `PATH`, system commands off, listening on **`http://localhost:5080` only**
(the setup page, or later Administration › System › Installation, opens it to the network).

- **Windows:** unzip, run `SFE\Sfe.Api.exe`. It opens the app in your browser, and its window shows the
  [setup code](first-start.md#the-setup-code). Settings (`appsettings.json`) and data (`data\`: index, thumbnails, saved
  settings, setup code, installation settings) stay in that folder, which must be writable. To start SFE with the
  computer, run `install-service.cmd` (see [Windows service](#windows-service)).
- **Linux:** `chmod +x SFE-*.AppImage` and run it. Settings and data live in `~/.config/sfe` (or `$SFE_HOME`); the first
  start copies the default `appsettings.json` there. To keep everything next to the AppImage, create a
  `SFE-….AppImage.config` folder beside it. Without FUSE, run it with `--appimage-extract-and-run`. Without ICU
  (`libicu`), the app runs culture-invariant.
- Any setting can still be given on the command line, e.g. `Sfe.Api.exe --Sfe:Root=D:\Media --urls http://*:8080`
  (it then wins over the setup page). `--no-browser` keeps the browser closed (or switch it off in
  [System › Start-up](administration/system.md#start-up)).

If SFE doesn't start, its window says why: a configuration file that isn't valid JSON (with the line), or a port used by
another program.

## From source

Needs the .NET 10 SDK and [Node.js](https://nodejs.org/) 22.22 or 24.15+ (npm builds the web client).

```sh
dotnet run --project src/Sfe.Api          # http://localhost:5080, Development settings
```

The shipped `appsettings.json` browses your home folder (`"Root": "~"`) and keeps the index in SQLite
(`src/Sfe.Api/data/`); pick another folder on the setup page, or with `Sfe__Root=/srv/media`. Your own machine's
values can go in `src/Sfe.Api/appsettings.Local.json` (not in git), read after `appsettings.json`.

## IIS

1. `dotnet publish src/Sfe.Api -c Release` (Node.js needed to build).
2. Install the [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) and point a site at
   the output.
3. The app pool identity needs read access to the library and write access to the thumbnail cache.

The application folder is usually not writable under IIS: point the saved settings elsewhere, e.g.
`Sfe__SettingsFile=D:\sfe-data\settings.json` (see [Configuration](configuration.md#the-settings-file)), and the same
for the setup code and the installation settings: `Sfe__Setup__CodeFile=D:\sfe-data\setup-code.txt`,
`Sfe__InstallationFile=D:\sfe-data\installation.json`. To let the [picture editor](user-guide/picture-editor.md) save
pictures over 30 MB, raise IIS's own request limit (`maxAllowedContentLength`).

## Windows service

With the portable package, run `install-service.cmd` in SFE's folder (it asks for administrator rights): SFE becomes the
Windows service **SFE**, starts with the computer and keeps that folder's settings and data. `uninstall-service.cmd`
removes it (the data stays). The same from a console opened as administrator: `Sfe.Api.exe --install-service` (or
`--uninstall-service`; `--name` picks another service name).

The service runs as the Local System account: it reads local drives, not the network drives a user mapped. Local
System can do anything on the computer, and SFE runs ffmpeg on every video of the library: for a server others can
reach, run it under its own account instead (next section). Pick the
library folder (setup page) before installing it, or change it afterwards in Administration › System › Installation. To
run it under another account (e.g. one that can read a NAS share), change it in the Services console. By hand:

```bat
sc.exe create SFE binPath= "C:\sfe\Sfe.Api.exe" start= auto
```

### Its own account (recommended)

Windows gives every service a virtual account of its own, `NT SERVICE\SFE`, with no password and no rights but those
granted. From a console opened as administrator, with SFE in `C:\sfe` and the library in `D:\Media`:

```bat
sc.exe stop SFE
rem SFE's folder: read; its data folder (databases, keys, thumbnails, settings): change
icacls "C:\sfe" /grant "NT SERVICE\SFE:(OI)(CI)RX"
icacls "C:\sfe\data" /grant "NT SERVICE\SFE:(OI)(CI)M"
rem The library: read (add M only to let the picture editor save and duplicates be deleted)
icacls "D:\Media" /grant "NT SERVICE\SFE:(OI)(CI)RX"
sc.exe config SFE obj= "NT SERVICE\SFE"
sc.exe start SFE
```

Keep `C:\sfe\data` out of reach of other users: it holds the keys that sign the sessions and the setup code.

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
Environment=Sfe__Setup__CodeFile=/var/lib/sfe/setup-code.txt
Environment=Sfe__InstallationFile=/var/lib/sfe/installation.json
Environment=Sfe__Security__KeysDirectory=/var/lib/sfe/keys
Environment=Sfe__Logs__Directory=/var/lib/sfe/logs
# Optional: a setup code of your own (else SFE makes one, shown by journalctl -u sfe and kept in the file above).
Environment=Sfe__Setup__Code=pick-a-long-code-nobody-guesses
StateDirectory=sfe
StateDirectoryMode=0700
Restart=on-failure
# Hardening: SFE needs to read the library and write /var/lib/sfe, nothing else.
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=read-only
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
LockPersonality=yes
# Add the library here if the picture editor saves pictures or duplicates are deleted:
# ReadWritePaths=/srv/media

[Install]
WantedBy=multi-user.target
```

Then `systemctl enable --now sfe`; it logs to the journal. The `sfe` user needs read access to the library.

The hardening lines make `/opt/sfe` read-only for SFE: everything it writes must be under `/var/lib/sfe`, as above. On
an install that kept its keys or logs in `/opt/sfe/data`, move `keys/` and `logs/` to `/var/lib/sfe` before adding
them (without the keys, everyone has to sign in again).

## Network address

The default listening address is `http://*:5080` (`Urls` in `appsettings.json`), except for the portable packages
(localhost only). The setup page and Administration › System › Installation change it (this computer only, or the
network, and the port) unless the environment or the command line sets it.

## Reaching it from outside: https and a reverse proxy

SFE itself speaks plain http, which is fine on a home network. Anywhere else, put it behind a reverse proxy that
provides https (Caddy, nginx, Traefik, IIS, your NAS's): passwords and the sign-in cookie must not travel in clear.

- A proxy **on the same computer** is trusted as it is: SFE reads the visitor's address and scheme from its
  `X-Forwarded-For` and `X-Forwarded-Proto` headers, so the sign-in limits count each visitor, and the cookie is marked
  secure when the visitor uses https.
- A proxy **on another computer** must be named: `Sfe:Security:TrustedProxies`, its address or network (e.g.
  `"TrustedProxies": [ "192.168.1.2" ]`, or `Sfe__Security__TrustedProxies__0=192.168.1.2`). Without it, every
  visitor seems to be the proxy, and a few wrong passwords from anyone slow everyone's sign-ins down.
- Once the app is only used over https: `Sfe:Security:RequireHttps` = `true`. The cookie is then never sent over
  plain http, and browsers are told to use https only.
- Name the addresses people use: `AllowedHosts` (e.g. `"AllowedHosts": "photos.example.com;nas.local;localhost"`).
  SFE then refuses requests for any other host name, which keeps a web page elsewhere from reaching it through a
  name it controls (DNS rebinding).

## Fully local

The application never calls an external service. The web client and the API reference are served by the app itself,
with no CDN, web fonts, telemetry or online validator. The only outbound connections are the ones you configure: the
database server, and the [data server](administration/playback.md#data-server) when it is set.
