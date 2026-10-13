# Configuration

Most settings can be changed while SFE runs, on the [administration page](administration/README.md). This page is
about the configuration files, which also hold what the web page may never change.

## Where settings come from

Everything lives in `src/Sfe.Api/appsettings.json` (next to the executable in a package). Any value can be overridden
with environment variables (`Sfe__Root=D:\Media`, `ConnectionStrings__Sfe=...`) or on the command line
(`--Sfe:Root=D:\Media`).

The shipped `appsettings.json` describes the original Windows host (`V:\`, SQL Express); on Linux override at least
`Sfe:Root`, the connection string and `Sfe:Thumbnails:CacheDirectory`. Relative paths (thumbnail cache, SQLite file)
are relative to the application folder; a leading `~` is the home folder.

Values saved on the administration page take precedence over these files, at several levels (server, space, user):
see [Settings by level](reference/settings-levels.md).

**Only in the configuration**, because they decide which programs run or where data lives: the library root, the
thumbnail cache folder, the database, the ffmpeg path, shutdown/dismount, and everything about the setup, sign-in and
spaces' storage.

## Main settings

| Setting | Notes |
|---|---|
| `Sfe:Root` | The library folder of the first space. `V:` is treated as `V:\`; `/srv/media` or `~/Videos` on Linux |
| `ConnectionStrings:Sfe` | The first space's index. Add `TrustServerCertificate=True` for SQL Express |
| `Sfe:Database:Provider` | `SqlServer` or `Sqlite` (see [Database](#database)) |
| `Sfe:Thumbnails:CacheDirectory` | Thumbnails and trickplay |
| `Sfe:Thumbnails:FfmpegPath` | Default `ffmpeg` (found on the `PATH`) |
| `Sfe:DataServer` | Optional web server already publishing the library ([Playback › Streaming](administration/playback.md#streaming-data-server)) |
| `Sfe:SettingsFile` | Where the administration page saves (see [below](#the-settings-file)) |
| `Sfe:System:EnableShutdown`, `ShutdownCommand`, `DismountCommand` | Server commands, opt-in (see [System](administration/system.md)) |
| `Sfe:Backup:SqlServerFolder` | SQL Server only: where the database server writes the [backups](administration/system.md#backup) (as it sees it); empty = its default backup folder |
| `Urls` | Listening address, default `http://*:5080` |

## Sign-in, accounts and spaces

| Setting | Notes |
|---|---|
| `Sfe:Setup:Code` | The setup code the first administrator is created with (at least 12 characters), and that confirms each [update](upgrading.md#the-update-page-137-and-later). See [First start](first-start.md) |
| `Sfe:Setup:Recover` | `true` opens the setup page again to reset a lost administrator password (with the code); turn it off afterwards |
| `Sfe:Security:AnonymousGuests` | Default `true`: spaces may let visitors in without an account; `false` forbids it everywhere |
| `Sfe:Security:SessionDays`, `MinimumPasswordLength`, `MaxFailedSignIns`, `LockoutMinutes`, `SignInsPerMinute` | Sign-in: 30 days, 8 characters, 5 failures lock an account for 15 minutes, 10 attempts per minute and address |
| `Sfe:Security:KeysDirectory` | Keys signing the sign-in cookies and the external players' links (default `data/keys`); keep it, or everyone is signed out |
| `Sfe:Security:Directories` | Directories checking passwords, in order (empty = local accounts only; see [Directories](administration/spaces-and-accounts.md#directories-ldap)) |
| `Sfe:Rights:Disabled` | Rights nobody gets, not even administrators (e.g. `tracking.duplicatesRemove`, `files.*`); see [Rights](reference/rights.md) |
| `Sfe:Locked` | Settings nobody can change from the web, at any level (e.g. `player.*`); see [Settings by level](reference/settings-levels.md) |
| `Sfe:Tenants:AllowedRoots` | Folders new spaces may be created in (default: `Sfe:Root`) |
| `Sfe:Tenants:ConnectionString` | Index of a new space, `{tenant}` standing for its short name; default next to the first space's |

## Settings also on the administration page

These have a default in `appsettings.json` and can be changed on the page named; the page describes each one.

| Setting | Page |
|---|---|
| `Sfe:Appearance:Theme` (`System`, `Dark`, `Light`) | [Interface › Appearance](administration/interface.md#appearance) |
| `Sfe:Browse:DefaultPageSize`, `MaxPageSize`, `FolderPictures`, `CopyPaths`, `ShowResolution`, `ShowDuration`, `ResolutionMark`, `ImageHighResolutionPixels`, `VideoHighResolutionPixels` | [Interface › Browsing](administration/interface.md#browsing) |
| `Sfe:Search:DefaultPageSize`, `MaxPageSize`, `Explain`, `HistorySize` | [Interface › Search](administration/interface.md#search) |
| `Sfe:Player:SeekSeconds`, `Loop`, `ExternalUrl`, `ExternalName`, `LinkLifetimeMinutes` | [Playback](administration/playback.md) |
| `Sfe:Streaming:Mode` (`Fallback`, `Always`, `Off`), `MaxStreams`, `Quality`, `MaxHeight`, `CopyVideo` | [Playback › Videos converted by the server](administration/playback.md#videos-converted-by-the-server) |
| `Sfe:Scan:IgnoredDirectories`, `IgnoredFiles`, `ExcludedFolders`, `Sfe:ShowHiddenItems` | [Scanning › What to scan](administration/scanning.md#what-to-scan) |
| `Sfe:FileTypes:Images`, `Videos`, `Audio` | [Scanning › File types](administration/scanning.md#file-types) |
| `Sfe:Scan:ComputeHashes`, `ReadResolution`, `Parallelism` | [Scanning › Indexing](administration/scanning.md#indexing) |
| `Sfe:Tags:SidebarCount`, `RecordHistory` | [Tags › Settings](administration/tags.md#settings) |
| `Sfe:FileTracking:…` | [Duplicates & moves › Settings](administration/duplicates-and-moves.md#settings) |
| `Sfe:Thumbnails:Enabled`, `Width`, `VideoSeekSeconds`, `LargeImageKilobytes`, `SingleViewOriginals`, `MaxConcurrency`, `TimeoutSeconds` | [Thumbnails](administration/thumbnails.md) |
| `Sfe:Trickplay:…` | [Trickplay › Settings](administration/trickplay.md#settings) |
| `Sfe:Editor:OutputFolder`, `Quality` | [Picture editor](administration/picture-editor.md) |
| `Sfe:Tasks:LogRetentionDays` | [Background tasks › Settings](administration/background-tasks.md#settings) |

## The settings file

The administration page saves to `Sfe:SettingsFile` (`data/settings.json` in the application folder). When that
folder isn't writable (IIS under Program Files, `/opt/sfe`), point it elsewhere, e.g.
`Sfe__SettingsFile=/var/lib/sfe/settings.json`. Values of a space and of each user are kept in the database.

## Database

- **SQLite** (`"Provider": "Sqlite"`, `"ConnectionStrings:Sfe": "Data Source=data/sfe.db"`): the simplest choice,
  especially on Linux.
- **SQL Server:** the database is created or upgraded on startup (`Sfe:Database:MigrateOnStartup`).
- **Upgrades:** an existing index gets new tables and indexes on the next start, which can take a moment on a large
  library.
- **Case sensitivity:** names are searched case-insensitively. Full paths follow the host file system: on Linux
  `photo.jpg` and `Photo.jpg` are two files. SQL Server's default collation can't index both (the scan reports an error
  for that folder), and it limits paths to 260 characters (longer ones are skipped). SQLite has neither limit.
- **Moving an index between operating systems** is not supported (it stores physical paths): rescan instead.
- Each further space has its own index: see [Spaces](administration/spaces-and-accounts.md#creating-a-space).
