# Simple File Explorer (.NET 10)

A web file explorer for a home media library: browse folders, preview photos, video and audio, search an index of
the whole library, tag files, and find duplicates. It is a port of the ASP.NET MVC 5 / .NET Framework 4.8
`SimpleFileExplorer` to **.NET 10**: a JSON API plus a single-page web client. The running version shows at the
bottom of the ⋮ menu and in Administration › System.

Building, testing and the code itself are described in [AGENTS.md](AGENTS.md).

## Run it

- **Portable package** (no .NET needed): see [Portable packages](#portable-packages).
- **From source:** needs the .NET 10 SDK and [Node.js](https://nodejs.org/) 22.22 or 24.15+ (npm builds the web
  client), on Windows, Linux or macOS.

  ```sh
  dotnet run --project src/Sfe.Api          # http://localhost:5080, Development settings
  ```

  The Development settings browse your home folder (`"Root": "~"`) and keep the index in SQLite
  (`src/Sfe.Api/data/sfe-dev.db`). Point them elsewhere with `Sfe__Root=/srv/media`.

Optional: SQL Server, and [ffmpeg](https://ffmpeg.org/) on the `PATH` for thumbnails, trickplay and similar videos
(`apt install ffmpeg` on Debian/Ubuntu).

API reference: `http://localhost:5080/api/docs` (Swagger UI, for accounts with the API documentation right);
OpenAPI document: `/openapi/v1.json`.

**First start:** SFE has accounts, and nothing works until its first administrator exists. Give the server a
**setup code** of at least 12 characters in its environment (`Sfe__Setup__Code=…`, or `"Setup": { "Code": "…" }` in
the `Sfe` section of `appsettings.json`), restart it, open the app and enter the code to create the administrator.
Without a code the page explains how to set one for the way SFE runs. See
[Accounts, libraries and rights](#accounts-libraries-and-rights).

### Fully local

The application never calls an external service. The web client and the Swagger UI are served by the app itself,
with no CDN, web fonts, telemetry or online validator. The only outbound connections are the ones you configure:
the database server, and `Sfe:DataServer` when it is set.

## Configuration

Everything lives in `src/Sfe.Api/appsettings.json` (next to the executable in a package). Any value can be
overridden with environment variables (`Sfe__Root=D:\Media`, `ConnectionStrings__Sfe=...`) or on the command line
(`--Sfe:Root=D:\Media`). The shipped `appsettings.json` describes the original Windows host (`V:\`, SQL Express); on
Linux override at least `Sfe:Root`, the connection string and `Sfe:Thumbnails:CacheDirectory`. Relative paths
(thumbnail cache, SQLite file) are relative to the application folder; a leading `~` is the home folder.

| Setting                                       | Notes |
|-----------------------------------------------|-------|
| `Sfe:Root`                                    | The library folder. `V:` is treated as `V:\`; `/srv/media` or `~/Videos` on Linux |
| `Sfe:DataServer`                              | Optional web server already publishing the library (Administration › Playback › Streaming). A base address gets the file's path appended; or write a URL template: `{url}` is the path (each part encoded, `/` kept), `{encodedUrl}` the whole path encoded (for a query string), e.g. `http://nas/files/{url}?raw=1` or `http://nas/get?file={encodedUrl}`. When empty, files are streamed by the app, with range support |
| `ConnectionStrings:Sfe`                       | Add `TrustServerCertificate=True` for SQL Express |
| `Sfe:Thumbnails:CacheDirectory`               | Thumbnails and trickplay |
| `Sfe:Thumbnails:VideoSeekSeconds`             | Second of a video used for its thumbnail |
| `Sfe:Browse:DefaultPageSize`, `Sfe:Search:DefaultPageSize` | The client loads pages while you scroll |
| `Sfe:Search:MaxPageSize`                      | |
| `Sfe:Search:HistorySize`                      | Searches each browser remembers and suggests in the search box (default 10, 0 = off) |
| `Sfe:Scan:IgnoredDirectories`/`IgnoredFiles`  | Names skipped by scans |
| `Sfe:Scan:ExcludedFolders`                    | Library folders (paths from the root) skipped by scans |
| `Sfe:Tasks:LogRetentionDays`                  | Days the runs of background tasks are kept (default 30, 0 = until cleared) |
| `Sfe:FileTypes:Images`/`Videos`/`Audio`       | Plain lists of extensions |
| `Sfe:System:EnableShutdown`, `Sfe:System:ShutdownCommand`, `Sfe:System:DismountCommand` | Shutdown/dismount are opt-in |
| `Sfe:Setup:Code`                              | The setup code the first administrator is created with (at least 12 characters); not needed afterwards |
| `Sfe:Setup:Recover`                           | `true` opens the setup page again to reset a lost administrator password (with the code); turn it off afterwards |
| `Sfe:Security:AnonymousGuests`                | Default `true`: libraries may let visitors in without an account; `false` forbids it everywhere |
| `Sfe:Security:SessionDays`, `MinimumPasswordLength`, `MaxFailedSignIns`, `LockoutMinutes`, `SignInsPerMinute` | Sign-in: 30 days, 8 characters, 5 failures lock an account for 15 minutes, 10 attempts per minute and address |
| `Sfe:Security:KeysDirectory`                  | Keys signing the sign-in cookies and the external players' links (default `data/keys`); keep it, or everyone is signed out |
| `Sfe:Rights:Disabled`                         | Rights nobody gets, not even administrators (e.g. `tracking.duplicatesRemove`, `files.*`); see [Rights](#rights) |
| `Sfe:Locked`                                  | Settings nobody can change from the web, at any level (e.g. `player.*`); see [Settings by level](#settings-by-level) |
| `Sfe:Tenants:AllowedRoots`                    | Folders new libraries may be created in (default: `Sfe:Root`) |
| `Sfe:Tenants:ConnectionString`                | Index of a new library, `{tenant}` standing for its short name; default next to the first library's |
| `Sfe:Player:LinkLifetimeMinutes`              | How long a link given to an external player works (default 360) |
| `Sfe:Editor:OutputFolder`                     | Library folder (path from the root) where edited pictures are saved (default `Edited`; empty = next to the originals); see [Editing pictures](#editing-pictures) |
| `Sfe:Editor:Quality`                          | Quality of the JPEG and WebP pictures the editor saves, 10–100 (default 92) |

Other settings: `Sfe:Database:Provider` (`SqlServer` or `Sqlite`), `Sfe:ShowHiddenItems` (on Linux, hidden means a
name starting with `.`), `Sfe:Scan:Parallelism`, `Sfe:Scan:ComputeHashes`, `Sfe:Scan:ReadResolution`, `Sfe:Browse:ShowResolution|ShowDuration|ResolutionMark|ImageHighResolutionPixels|VideoHighResolutionPixels`, `Sfe:Thumbnails:FfmpegPath|Width|MaxConcurrency`,
`Sfe:Thumbnails:LargeImageKilobytes` (default 512: larger photos are previewed through their thumbnail; 0 = always),
`Sfe:Thumbnails:SingleViewOriginals` (default off: the one-per-row view shows photos themselves, see [Browsing](#browsing)),
`Sfe:Tags:RecordHistory|SidebarCount`, `Sfe:Player:SeekSeconds` (default 10), `Sfe:Player:Loop` (default off), `Sfe:Appearance:Theme`
(`System`, `Dark` or `Light`; see [Themes](#themes)), `Sfe:SettingsFile`.

### Video player

Videos play in SFE's own player: seek bar with trickplay previews, back/forward buttons (`Sfe:Player:SeekSeconds`),
speed (its button lists the rates, 0.5× to 2×), loop, mute, fullscreen and a camera button that saves the frame on screen as a PNG at the video's resolution,
named after the video and the position (e.g. `Holiday 1-23.png`). The loop button starts the video over when it ends;
whether it is on when a video opens is the **Loop videos** setting (`Sfe:Player:Loop`, off by default). Keys: Space/K
play or pause, J/L jump back/forward, F fullscreen, M mute, R loop, S save the frame; on a touch screen, double-tap the left or right of the picture to jump (keep
tapping to jump further), and hold a finger on the right of a playing video for 1.5 s to play it at 2× until you lift it. **Open in a new window** (W) moves the video to a window of its own, going on from the same position.

#### External player

The viewer's ▶ button (also offered when the browser can't play a format) opens the video or audio file in a player
app installed on the device, through a link the app registered. In Administration › Playback, pick your
player from the **External player** list (the players in the table below) and the link and name are filled in; pick
“Custom…” to write your own link. With “None (no button)” the link and name fields are greyed out. In the
configuration files, set `Sfe:Player:ExternalUrl` (default `vlc://{url}`; empty = no button) and `Sfe:Player:ExternalName` (default `VLC`, the
button's "Open in …"). In the link, `{url}` is the file's full address and `{encodedUrl}` the same address
percent-encoded (for a query string); a link with neither just gets the address appended, so `vlc://` works too. The
app streams the file from SFE (or from `Sfe:DataServer`), so the device must reach that address.

The player app doesn't have your browser's sign-in, so the address it gets is a **link of its own** to that one file
(`api/files/shared/<token>/<name>`). It works without signing in for `Sfe:Player:LinkLifetimeMinutes` (6 hours by
default, enough for a film: the player asks again at every seek), and stops earlier when your account's password
changes, the account is disabled, or it loses the right to use external players. Anyone holding the link can play
the file until then: share it as you would the file. With `Sfe:DataServer`, the player gets that server's address as
before (it has no sign-in of its own).

| Player | Link | Notes |
|---|---|---|
| VLC (iPhone, iPad) | `vlc://{url}` | Built in. |
| VLC (Windows, macOS, Linux) | `vlc://{url}` | Desktop VLC does not register `vlc://`: install a handler such as [vlc-protocol](https://github.com/stefansundin/vlc-protocol). |
| VLC (Android) | `intent:{url}#Intent;package=org.videolan.vlc;type=video/*;end` | Android uses intents; Chrome-based browsers and Firefox open these. |
| MX Player (Android) | `intent:{url}#Intent;package=com.mxtech.videoplayer.ad;type=video/*;end` | `.pro` for the paid edition. |
| PotPlayer (Windows) | `potplayer://{url}` | Registered by the installer. |
| Infuse (iPhone, iPad, Mac) | `infuse://x-callback-url/play?url={encodedUrl}` | Not on Apple TV. |
| IINA (macOS) | `iina://weblink?url={encodedUrl}` | |
| mpv | `mpv://…` | Only through a third-party handler ([mpv-handler](https://github.com/akiirui/mpv-handler)), with its own link format. |

The setting applies to every browser: pick the player of the device you use most.

### Trickplay

Trickplay is a set of small frames taken along each video (one every `IntervalSeconds`, tiled `Columns` × `Rows` on
JPEG sheets), shown above the player's seek bar while you hover or drag it, and played over a video's thumbnail
while the mouse rests on its tile or row (on a phone: while you hold it). It also powers
[similar videos](#duplicates-and-moved-files). It is off by default:

- `Sfe:Trickplay:Enabled` turns on the background job (it uses ffmpeg, independently of thumbnails).
- `Sfe:Trickplay:Folders` limits it to some library folders (sub-folders included); empty = the whole library.
  Folders skipped by scans and hidden items are skipped too.
- The job goes over the folders at start-up, when the trickplay settings change, every `RecheckHours` (0 = never)
  and on **Look for new videos now** in Administration › Trickplay › Settings. A video opened in the player without trickplay goes first.
- `IntervalSeconds`, `Width`, `Columns`, `Rows` and `KeyFramesOnly` decide the pictures: changing them makes every
  trickplay again (the old frames are shown until then). `KeyFramesOnly` (default on) is much faster, but a frame
  can be a few seconds off. `Parallelism` and `TimeoutMinutes` (per video) bound the load; a video that fails is
  skipped until it or the settings change.
- Progress: a video waiting for trickplay shows a clock in the listings, one being processed a spinner. Administration ›
  Trickplay › **Activity** lists the videos being processed, waiting and finished, with the reason of failures. The
  next periodic check, and checks at set times, are in [Background tasks](#background-tasks).
- `HoverPreview` (default on, Administration › Trickplay › Settings › **Play on hover**): resting the mouse on a video in a listing
  plays its frames over the thumbnail, picked along the whole video, with a bar showing where. On a touch screen,
  press and hold the video: it plays until you lift your finger (a tap still opens it, scrolling cancels the hold).
  Only videos that already have trickplay: hovering doesn't queue one.

Trickplay is stored under `{Thumbnails:CacheDirectory}/trickplay/`. Trickplay of deleted videos is not cleaned up:
delete that folder to start over.

### Duplicates and moved files

Administration › **Duplicates & moves** has three pages next to its settings. All work from the index, so scan folders to keep it
current. Click a name to open the file, or its folder to browse there.

- **Duplicates:** groups of indexed files with the same size and fingerprint, biggest first, with the space the
  extra copies take. **Only in “folder”** compares only the folder being browsed. The fingerprint (computed by scans,
  `Sfe:Scan:ComputeHashes`) covers the first megabyte, so files differing only further in can show up; files indexed
  without one are not compared. **Remove** deletes a copy from the disk and adds its tags to the copy you keep. SFE
  first compares the two files byte for byte and refuses two paths to the same file (folder link, junction, hard
  link), so an identical copy always remains. The deletion is permanent (no recycle bin). Turn it off with
  `Duplicates:AllowRemove`; anyone who can open SFE can turn it back on, since there is no login.
- **Similar videos:** videos showing the same pictures in other files (another resolution, bitrate or format, or
  trimmed), which **Duplicates** can't see. They are compared frame by frame from their trickplay, so **turn
  trickplay on** for the folders to check; the summary says how many videos were compared. The first video of a
  group is the longest; the others show their **match**: the frames they share with it, in percent of the longer of
  the two (another resolution ≈ 100 %, a copy cut by a tenth ≈ 90 %, a short excerpt stays low). **Compare now**
  runs the comparison in the background; its result is kept until the next one or a restart. Not found: clips
  shorter than three trickplay intervals, cropped, mirrored or sped-up copies. Videos that look alike all along (a
  still screen) can match each other. Nothing is deleted from this tab.
- **Moved files:** tagged files a scan no longer found (moved, renamed, or on a drive that was unplugged). Instead of
  losing their tags, they are kept as *missing*. After each scan, a missing file with exactly one likely new place (same
  size and fingerprint, indexed after it; same name wins ties) gets its tags back there, with its thumbnail and
  trickplay. An identical copy indexed at the same time can be taken for it: the tags then go to a file with the same
  content. The others wait in the list: relink them to a candidate or any file (**Choose file…**), or forget them.

Settings (`Sfe:FileTracking`, also in **Administration › Duplicates & moves › Settings**):

| Setting | Default | |
|---|---|---|
| `Duplicates:MinimumKilobytes` | 1 | Smaller files are left out; 0 compares every file |
| `Duplicates:GroupsPerPage` | 50 | Groups loaded at a time (up to 500) |
| `Duplicates:AllowRemove` | `true` | Show **Remove** on each copy |
| `SimilarVideos:MinimumPercent` | 80 | Smallest match (50 to 100) to list two videos as similar; applies from the next comparison |
| `Relink:Enabled` | `true` | Keep tagged files no longer found as missing. When off, they lose their tags |
| `Relink:MatchByName` | `true` | Match files without a fingerprint on name, size and date (catches a move, not a rename) |
| `Relink:RetentionDays` | 30 | Missing files not found again in that time are forgotten; 0 = keep them |

### Background tasks

Administration › **Background tasks** gathers the jobs the server runs on its own. It refreshes while shown; its first
line says what runs now and what runs next.

- **Tasks:** index scan, trickplay, similar videos and thumbnails, each with its state, progress, last run and
  **next run**: the exact time, how long until then, and why (which schedule, or trickplay's own periodic check
  every `Trickplay:RecheckHours`). **Run now** starts a task (a scan covers the folder browsed last and asks
  first), **Cancel** stops it: a scan keeps what it already indexed; trickplay stops its pass and forgets the videos
  waiting until its next check; a comparison keeps the previous result; thumbnails being rendered are dropped and
  asked for again by the listings. Thumbnails can't be started or scheduled: listings ask for them.
- **Schedules:** start a scan (of a folder or the whole library), a trickplay check or a similar-video comparison
  **once**, **repeatedly** (every N minutes, hours or days from a start), **every day** or **on some days of the
  week** at a time. While you fill in a schedule, the next five runs are shown as the server works them out. Each
  schedule shows its next and last run, can be turned off without deleting it, edited or deleted. Times are shown in
  your browser's time zone; daily and weekly times follow the server's clock (the page says so when the two differ).
  A run that comes due while the task already runs is recorded as *skipped* (a trickplay check is queued after the
  current one instead); runs missed while the server was off are not caught up.
- **History:** every run of a scan, trickplay check or comparison: when, how long, what started it (by hand, a
  schedule, automatic), how it ended (completed, cancelled, failed, interrupted by a server stop, skipped) and a
  summary. Click a run for its log, live while it runs (the first and last 1,000 lines of a long one are kept).
  **Clear history** deletes the finished runs.

Runs are kept `Sfe:Tasks:LogRetentionDays` days (default 30, 0 = until cleared; Administration › Background tasks › Settings). Schedules and
runs are stored in the database (tables `TaskSchedules` and `TaskRuns`).

### Administration page

**Toggle fullscreen** in the ⋮ menu shows the page full screen (or leaves it). **Administration** there opens the
administration page (`admin`; the former `config` address still leads there). Its sidebar groups the sections; the
section open lists its pages under it. On phones the page opens on the list of sections, each one then a page of its
own with a back arrow to the list and its pages as chips at the top.

| Group | Section | Pages |
|---|---|---|
| Preferences | **Interface** | **Appearance** (default theme, this browser's theme), **Browsing** (listings, folder pictures, copy paths, resolutions and durations), **Search** (results, explain, search history) |
| | **Playback** | video player, external player, data server |
| Library | **Scanning** | **What to scan** (skipped folders and files, hidden items), **File types**, **Indexing** (fingerprints, resolutions and durations, speed) |
| | **Tags** | **All tags** (create, edit, delete), **Settings** (sidebar, tag history) |
| | **Duplicates & moves** | **Duplicates**, **Similar videos**, **Moved files**, **Settings** ([details](#duplicates-and-moved-files)) |
| Media | **Thumbnails** | size, video position, large photos, performance |
| | **Trickplay** | **Activity**, **Settings** ([details](#trickplay)) |
| Access | **People** | **Accounts**, **Groups & rights**, **Guest access** ([details](#accounts-libraries-and-rights)) |
| | **Libraries** | the server's libraries (server administrators) |
| Server | **Background tasks** | **Tasks**, **Schedules**, **History**, **Settings** ([details](#background-tasks)) |
| | **System** | version, API reference, shutdown/dismount |
| Account | **My account** | **Preferences** (your own theme, page sizes, player...), **Password** |

Each page shows only to the accounts with its right: an account without any right to change something sees only
**My account**, a visitor without an account no administration page at all.

The arrow at the top left goes back to the folder you came from, scrolled where you left it.

The settings change most values while the app runs, with a description for each. Changes on several pages are
kept together until **Save** in the bar at the bottom (a dot marks the sections and pages with unsaved changes, **Discard**
drops them, and leaving the page asks first). Saved values apply right away, for everyone using the library, and take
precedence over `appsettings.json` and environment variables; **Reset to defaults** goes back to those. Server
administrators choose at the top of a settings page whether they edit **this library** or **the whole server** (the
defaults of every library): see [Settings by level](#settings-by-level). Errors are shown next to their fields, and
their sections and pages are marked in red. The page shows the most used settings; tick **Show all settings** in the
bottom bar for the others (each browser remembers the choice).

Not editable there, because they decide which programs run or where data lives: the library root, the thumbnail cache
folder, the database, the ffmpeg path, shutdown/dismount, and everything about the setup and sign-in. Set them in
`appsettings.json` or environment variables.

The page saves to `Sfe:SettingsFile` (`data/settings.json` in the application folder). When that folder isn't
writable (IIS under Program Files, `/opt/sfe`), point it elsewhere, e.g. `Sfe__SettingsFile=/var/lib/sfe/settings.json`.

System commands run through `cmd.exe /c` on Windows, `/bin/sh -c` elsewhere. An empty `ShutdownCommand` is
`shutdown /s /f /t 5` on Windows and `shutdown -h +1` elsewhere. On Linux the service account usually may not shut
down: use e.g. `sudo -n /usr/sbin/shutdown -h +1` and allow that command in sudoers (or polkit).

### Database

- **SQL Server:** the database is created or upgraded on startup (`Sfe:Database:MigrateOnStartup`).
- **SQLite** (`"Provider": "Sqlite"`, `"ConnectionStrings:Sfe": "Data Source=data/sfe.db"`): the simplest choice,
  especially on Linux.
- **Upgrades:** an existing index gets new tables and indexes on the next start, which can take a moment on a large
  library.
- **Case sensitivity:** names are searched case-insensitively. Full paths follow the host file system: on Linux
  `photo.jpg` and `Photo.jpg` are two files. SQL Server's default collation can't index both (the scan reports an
  error for that folder), and it limits paths to 260 characters (longer ones are skipped). SQLite has neither limit.
- **Moving an index between operating systems** is not supported (it stores physical paths): rescan instead.

## Accounts, libraries and rights

Everyone signs in (or browses as a guest where a library allows it), and each account only gets the rights its
groups give it.

### First start: the setup code

Until the first administrator exists, every page shows the setup page and the API answers nothing else. So that
only whoever runs the server can claim it, the setup page asks for a **setup code** that you give the server
in its environment first, at least 12 characters long:

| How SFE runs | Where the code goes |
|---|---|
| Windows service | `"Setup": { "Code": "…" }` in the `Sfe` section of `appsettings.json` next to the program, or the system environment variable `Sfe__Setup__Code`; restart the service |
| systemd | `Environment=Sfe__Setup__Code=…` in the `[Service]` section (`systemctl edit sfe`), then `systemctl restart sfe` |
| IIS | `<environmentVariable name="Sfe__Setup__Code" value="…" />` in the site's `web.config`, then recycle the app pool |
| Container | the environment variable `Sfe__Setup__Code` (`docker run -e`, a compose file, your NAS's container settings) |
| Portable package / AppImage | `"Setup": { "Code": "…" }` in the `Sfe` section of its `appsettings.json` (`~/.config/sfe` for the AppImage) |

Then open the app: it asks for the code, the administrator's name and password, and the name of your library. The
administrator is signed in at once. The code is not used again: remove it from the environment (the log reminds you).
Without a code, or with one that is too short, the page shows these instructions for the way your server runs.

### Signing in

The sign-in page asks for a name and password (names ignore case). **Keep me signed in** keeps the session for
`Sfe:Security:SessionDays` (30) without use; otherwise it ends with the browser. Five wrong passwords in a row lock
the account for 15 minutes, and each address may try 10 times a minute. A new password, a disabled account or a
deleted one ends its sessions at once, on every device. The ⋮ menu shows who is signed in, switches between your
libraries and signs out; **Administration › My account** changes your password and your own preferences.

### Accounts and groups

**Administration › People** (right `tenant.members`):

- **Accounts:** create local accounts (name, display name, password), put them in groups, disable them, unlock them,
  delete them. A **guest account** never gets a right that changes something, whatever its groups give: handy as a
  shared account for visitors or children. Server administrators see every account of the server and make other
  server administrators; a library's administrators see the accounts of their library and only change the groups
  of those other libraries share. The last server administrator cannot be removed.
- **Groups & rights:** each library has three built-in groups, **Administrators** (every right of the library),
  **Users** (browse, view, download, external player, tag files, own preferences and password) and **Guests** (browse,
  view, see tags), and any groups you add. A group's page lists every right by area with what it allows.
- **Guest access:** lets visitors without an account browse the library, with the Guests group's rights.

**Server administrators** (made by the setup or by another server administrator) have every right in every
library, plus the server's own: libraries, every account, the server's settings, shutdown and dismount.

### Rights

Every function and every setting needs a right; what you don't have is hidden, and the server refuses it anyway.
Rights marked "no" are never given to guests.

| Right | Allows | Guests | Default groups |
|---|---|---|---|
| `library.browse` | browse, search, see the scan's progress | yes | all |
| `files.view` | open pictures, play videos and audio, thumbnails, trickplay | yes | all |
| `files.download` | download files | yes | Administrators, Users |
| `files.external` | open files in an external player (a link of its own) | yes | Administrators, Users |
| `files.copyPaths` | copy full paths (shows the server's folders) | no | Administrators |
| `files.edit` | edit pictures (crop) and save the result as a new file | no | Administrators, Users |
| `tags.view` | see tags, the tag sidebar | yes | all |
| `tags.assign` | tag files, folders and searches | no | Administrators, Users |
| `tags.manage` | create, edit, delete tags; clear the tag history | no | Administrators |
| `index.scan`, `index.remove` | scan; remove folders from the index | no | Administrators |
| `tracking.duplicates`, `tracking.duplicatesRemove`, `tracking.similar`, `tracking.missing` | duplicates, deleting copies, similar videos, moved files | no | Administrators |
| `trickplay.manage` | trickplay activity | no | Administrators |
| `tasks.view`, `tasks.run`, `tasks.schedule` | background tasks, running them, schedules | no | Administrators |
| `settings.interface`, `.playback`, `.scanning`, `.tags`, `.tracking`, `.thumbnails`, `.trickplay`, `.tasks`, `.editor` | the library's settings, by section | no | Administrators |
| `tenant.members` | the library's accounts, groups and guest access | no | Administrators |
| `account.preferences`, `account.password` | own preferences and password | no | Administrators, Users |
| `system.apiDocs` | the API documentation (`/api/docs`) | no | Administrators |
| `server.settings`, `server.tenants`, `server.users`, `server.shutdown`, `server.dismount` | the server's settings, libraries, every account, system commands | no | server administrators only |

Three levels decide who gets what: the **environment** can switch a right off for everyone, even administrators
(`Sfe:Rights:Disabled`, e.g. `files.download` or `tracking.*`); a server administrator can switch rights off in one
**library** (Administration › Libraries); and **groups** give the rest.

### Libraries

A server can hold several libraries, each with its own root folder, index, tags, schedules, thumbnails, settings
and people (**Administration › Libraries**, server administrators). A new library's root is picked among the folders
of `Sfe:Tenants:AllowedRoots` (by default the configured `Sfe:Root`, so a library can be a sub-folder of it). Its
index goes next to the first library's (SQLite: `tenants/<short name>.db` beside the index file; SQL Server: the
database `<database>_<short name>` on the same server, which the server's account must be allowed to create), or
where `Sfe:Tenants:ConnectionString` says; its caches under `<CacheDirectory>/tenants/<short name>`. Deleting a
library forgets its groups and settings but leaves its index and caches on the disk. Thumbnail and trickplay limits
(`MaxConcurrency`, `Parallelism`) are for the whole server, not per library.

An account reaches the libraries it has a group in (server administrators: all of them) and switches between them
in the ⋮ menu. The first library is the configuration's (`Sfe:Root`, `ConnectionStrings:Sfe`): it cannot be deleted.

### Settings by level

A setting's value comes from up to four levels, each over the one before:

1. **Environment**: `appsettings.json`, environment variables, the command line;
2. **Server**: Administration's settings pages, "the whole server" (server administrators): every library's defaults;
3. **Library**: the same pages, "this library" (its administrators, section by section with their rights);
4. **User**: Administration › My account › Preferences, each user's own.

Each setting has a lowest level: what the server shares (scan and ffmpeg parallelism, time limits, page size caps,
the data server, task log retention) stops at the server; what a library decides (file types, skipped and excluded
folders, thumbnails, trickplay, duplicates, the external player...) at the library; personal choices (theme, page
sizes, folder pictures, resolution and duration display, seek step, loop, hover previews, search and sidebar sizes) go
down to each user. The folders of a library (excluded folders, trickplay folders) are only set in the library itself.

A level can **lock** a setting: the levels below keep its value. The environment locks with `Sfe:Locked` (e.g.
`Sfe__Locked__0=player.*`); a settings page offers **Lock for libraries and users** (server) or **Lock for users**
(library) next to each setting the levels below could change. A setting you can't change says why (set by the
configuration, locked by the server or the library, a server setting, or not your section). Only values that differ
from the level above are saved, so a level keeps following the one above for the rest; **Reset to defaults** forgets
a level's own values.

### Directories (LDAP)

Accounts are checked by **directories** in the order of `Sfe:Security:Directories` (empty = the local accounts
only). Only the local one exists today; the model is ready for others such as LDAP: an account coming from a
directory gets a local entry at its first sign-in (its rights and preferences hang on it), and a group with a
**Directory group** name takes that directory group's members at each sign-in. The setup's administrator always
stays local, so a directory outage never locks you out.

### Lost administrator password

Set `Sfe__Setup__Recover=true` (with the setup code) and restart: the sign-in page links to the setup page, which now
gives a local administrator (by name) a new password, unlocked and enabled, or creates one. The app keeps working
meanwhile. Turn it off afterwards.

### Upgrading to 1.34

- After the upgrade, **set a setup code** (see above) and create the administrator: until then the app only shows the
  setup page. Your index, tags, thumbnails, trickplay and settings are kept: they become the first library's.
- The accounts' tables are added to the existing database on the first start (SQL Server: a migration; SQLite: new
  tables in the same file).
- Excluded folders and trickplay folders move from the server's settings file to the first library's settings (the
  log says so); everything else in `settings.json` keeps applying as the server level.
- Media players opened with ▶ now get links that expire (see [External player](#external-player)).
- Browsers keep their own theme and search history as before.

### Upgrading to 1.35

- The rights added by 1.35 (`files.edit`, `settings.editor`) are given once, at the first start, to the built-in groups
  of every existing library as a new library would have them: Administrators and Users may edit pictures,
  Administrators change the editor's settings. Take them away afterwards if you wish: later starts don't give them back.
- The accounts' database gets one new column (SQL Server: a migration; SQLite: added on start-up).

## Deploy

- **IIS:** `dotnet publish src/Sfe.Api -c Release` (Node.js needed to build), install the
  [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) and point a site at the output.
  The app pool identity needs read access to the library and write access to the thumbnail cache.
- **Windows service:** `sc.exe create SFE binPath= "C:\sfe\Sfe.Api.exe" start= auto`, with a service account that
  can read the library drives.
- **Linux (systemd):** `dotnet publish src/Sfe.Api -c Release -o /opt/sfe` (add `-r linux-x64 --self-contained`
  without the .NET runtime), then `/etc/systemd/system/sfe.service`:

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
- The default listening address is `http://*:5080` (`Urls` in `appsettings.json`).

### Portable packages

Self-contained packages that need no .NET install: `SFE-<version>-win-x64.zip` and `SFE-<version>-x86_64.AppImage`.
They start with neutral settings: library root `~`, SQLite index, `ffmpeg` from the `PATH`, system commands off,
listening on **`http://localhost:5080` only** (set `"Urls": "http://*:5080"` to open it to the network). Before the
first start, add a setup code to their `appsettings.json` (`"Setup": { "Code": "…" }` in the `Sfe` section): see
[First start](#first-start-the-setup-code).

- **Windows:** unzip, run `SFE\Sfe.Api.exe`. Settings (`appsettings.json`) and data (`data\`: index, thumbnails,
  saved settings) stay in that folder, which must be writable.
- **Linux:** `chmod +x SFE-*.AppImage` and run it. Settings and data live in `~/.config/sfe` (or `$SFE_HOME`); the
  first start copies the default `appsettings.json` there: edit it to set the library root. To keep everything next to
  the AppImage, create a `SFE-….AppImage.config` folder beside it. Without FUSE, run it with
  `--appimage-extract-and-run`. Without ICU (`libicu`), the app runs culture-invariant.
- Any setting can be given on the command line, e.g. `Sfe.Api.exe --Sfe:Root=D:\Media --urls http://*:8080`.

## Themes

The pages come in dark and light colors. The default theme is `Sfe:Appearance:Theme` (Administration › Interface ›
Appearance › **Default theme**): `System` (default) follows the light or dark mode of each device; `Dark` and `Light` force one.

Each browser can override it with **This browser's theme**, just below (applied at once, without saving): **Light** or **Dark**, kept even if the default
changes later, or back to the **Server default**. The choice is remembered per browser. The separate video player window always stays dark.

## Browsing

Inside a folder, the buttons before the path go to the **parent folder** (also Backspace or Alt+↑) and to the
**previous / next folder** of the parent (also `[` and `]`), in the order the parent is sorted by the current sort.
Every button shows a hint (and its keyboard shortcut, if any) when you rest the mouse on it.
Once you scroll down past the path, a slim bar under the top bar keeps these buttons and the folder's name (with its
parent, or the search) in view; click the name to go back to the top.

In the full-screen **viewer**, go to the previous / next file with the arrow buttons, ← and →, a swipe, or the
**mouse wheel** (down = next, up = previous; not over the video controls).

Long folders and search results load more items as you scroll. Once you scroll down, a small box at the bottom right
says where you are: the page (in pages of `Sfe:Browse:DefaultPageSize` or `Sfe:Search:DefaultPageSize` items), how
many items you have scrolled to out of the total, and a bar for the same. A search counts up to 10,000 matches
("10,000+" beyond). Pressing Enter in the search box on the search already shown keeps the results and your place, and reloading the
page (or coming back to it) brings you back to the item you were looking at.

In the **grid** and **one per row** layouts, folders are cards showing their first pictures (images, and videos when
thumbnails are available), like a Windows media folder. `Sfe:Browse:FolderPictures` (Administration › Interface › Browsing › **Pictures
per folder**) sets how many: 4 (default) makes a 2×2 grid, 9 a 3×3 grid, up to 16; 0 turns the pictures off and
lists folders compactly. Only the folder's own files count, not its sub-folders; a folder without pictures gets a
card of the same size with a folder icon. In a very large folder the pictures come from its first 5,000 files.

Large images (over `Sfe:Thumbnails:LargeImageKilobytes`) are shown through their thumbnail in listings.
**Full images in one-per-row view** (`Sfe:Thumbnails:SingleViewOriginals`, Administration › Thumbnails, off by default) shows
the images themselves in the **one per row** layout instead, so big previews stay sharp; each photo is then
downloaded whole as you scroll past it.

**Resolutions** of photos and videos (e.g. `1920×1080`) are shown before their size in the grid and one-per-row
layouts, under it in the list, and in the viewer, followed by the **duration** of videos and audio files (e.g.
`1:23:45`); in the grid and one-per-row layouts, the duration of a video or audio file is shown in the
bottom-left corner of its preview instead. Scans read them (`Sfe:Scan:ReadResolution`, Administration › Scanning › Indexing › **Read resolutions and
durations**, default on): photos from their first bytes (JPEG, PNG, GIF, BMP, WebP; other formats through ffmpeg),
videos and audio through ffmpeg, which makes the first scan of many videos or songs slower. Files indexed before it
was on are read by the next scan; without ffmpeg, videos and audio are read once it is set up. **Show resolutions**
(`Sfe:Browse:ShowResolution`) and **Show durations** (`Sfe:Browse:ShowDuration`), both in Administration › Interface
› Browsing and on by default, hide them from the listings; they can still be searched.

**Upgrading from 1.32 or earlier:** durations are new in 1.33. The database gets its new column on start-up (SQL
Server and SQLite alike), and the next scan of each folder reads the duration of the videos and audio files already
indexed, once: expect that scan to take about as long as the first scan of those files did. Until then, their duration is unknown (not shown, not found by `d:`).

**Mark high and low resolution** (`Sfe:Browse:ResolutionMark`, Administration › Interface › Browsing, off by default) labels the
preview of each photo and video whose resolution is known **HR** (high resolution, highlighted) or **LR** (low
resolution) in its bottom-right corner, and the thumbnail in the list layout; hover it for the resolution.
Photos and videos have their own threshold on the **shorter side**, so the orientation doesn't matter: **Photos are
high resolution from** (`Sfe:Browse:ImageHighResolutionPixels`, default 1500) and **Videos are high resolution from**
(`Sfe:Browse:VideoHighResolutionPixels`, default 720). With these, a 1000×1000 photo is LR but a 1000×1000 video HR;
a 2000×1500 photo or a portrait 1500×2000 one is HR, a 854×480 video LR. The searches `r:hr` and `r:lr` use the same
thresholds, whether the marks are shown or not.

**Copy full paths** (`Sfe:Browse:CopyPaths`, Administration › Interface › Browsing, off by default) adds buttons that copy the full path
of an item on the server, e.g. `D:\Media\Photos\beach.jpg`, to paste it into another program: after the location for
the current folder (the folder searched in while searching), on each folder and file (on hover with a mouse), and in
the viewer for the file shown. It is off by default because it tells every visitor where the library lives on the
server's disk. Copying also works over plain `http://` from another computer on the network. Folders and files
whose path you copied get a dashed outline in the listing, until the page is reloaded.

### Editing pictures

The viewer's **Edit** button (crop icon, or the E key) opens a picture in the editor, for those with the right
`files.edit`. **Crop** is its first tool: drag the frame's sides and corners, drag inside it to move it, drag outside
it to draw a new one, or type its position and size in pixels. The **Aspect ratio** buttons keep the frame to a
shape: Free, Original (the picture's own), Square (1:1), 4:3, 3:2, 5:4, 7:5, 16:9, 16:10 and 21:9, each also in
portrait (3:4, 2:3, 4:5, 5:7, 9:16, 10:16, 9:21). Picking one gives the largest frame of that shape around the current
frame's centre; the **Portrait** / **Landscape** button (or X) turns the chosen ratio the other way round. **Apply** (Enter) crops and lets you go on, **Undo**
(Ctrl+Z) takes the last edit back, and **Save a copy** (Ctrl+S) saves the result.

The original is never changed: the result is saved as a **new file**, in the folder for edited pictures under the
original's own folders (`Photos/2024/beach.jpg` becomes `Edited/Photos/2024/beach.jpg`), with ` (2)`, ` (3)`... added
when that name is taken. Set the folder in Administration › Media › Picture editor (`Sfe:Editor:OutputFolder`, default
`Edited`, a setting of each library); left empty, edited pictures go next to their originals as `beach (edited).jpg`.
Pictures already in that folder are saved next to themselves. The new file is added to the index at once, so search
finds it straight away.

The browser does the editing: JPEG, PNG and WebP pictures keep their format (JPEG and WebP at **Quality**,
`Sfe:Editor:Quality`, default 92), other formats are saved as PNG. The saved picture keeps no metadata of the
original (EXIF date, camera, location); a photo's orientation is applied to its pixels. A browser can only edit
pictures it can show, and phones may refuse very large ones. Saves are limited to 200 MB; behind IIS, raise its own
request limit (`maxAllowedContentLength`, 30 MB by default) to save larger pictures.

## Search syntax

| Query | Meaning |
|-------|---------|
| `cat dog` | name contains both |
| `cat, dog` | either one |
| `cat -kitten` | exclude a word (per alternative) |
| `s:>1gb`, `s:<=10mb`, `s:=0b`, `s:700mb` | size larger / smaller / exact / about (units b, kb, mb, gb, tb; mb by default) |
| `e:mp4` | extension |
| `w:>=1920`, `h:<720`, `r:>720x1024`, `r:1920x1080` | width / height / both sides of a photo or video, in pixels (`<`, `<=`, `>`, `>=`, `=`; none = exactly) |
| `r:hr`, `r:lr` | photos and videos of high / low resolution, as the HR/LR marks say |
| `d:>10m`, `d:<=30s`, `d:1h`, `d:>1:30:00` | videos and audio files longer / shorter / about that long (units s, m, h; m by default; or a clock `m:ss`, `h:mm:ss`) |
| `*holiday 2020` | match the whole path instead of the file name |
| `t:beach`, `tag:"science fiction"`, `t:science_fiction` | tagged with it, or with a tag inheriting from it (the search box suggests tags) |
| `special:untagged` | files without tags |
| `-t:work`, `-e:jpg`, `-s:>1gb`, `-r:1920x1080`, `-d:>1h`, `-special:untagged` | a `-` before any term finds the files that term doesn't: not tagged with it, any other extension, not larger than 1 GB, not exactly 1920×1080, not longer than an hour, with at least one tag |
| `"a, b"` | double quotes keep spaces and commas in a term |

Search is case-insensitive and covers the current folder and its sub-folders. It only finds indexed files: **Scan this folder**
(⋮ menu) refreshes the index of the current folder and its sub-folders in the background, with a progress bar.
Scans can also run at set times ([Background tasks](#background-tasks)).

**Remove from index…** (⋮ menu) takes the folder being browsed and its sub-folders out of the index: it says how many
folders, files and tagged files that is first. The files stay on the disk and browsing still shows them; search no
longer finds them. Tagged files are kept with the [moved files](#duplicates-and-moved-files) (unless relinking is
off), so scanning the folder again gives their tags back. **Skip this folder in future scans** (ticked by default)
adds it to `Sfe:Scan:ExcludedFolders` (Administration › Scanning › What to scan › **Excluded folders**, paths from the library root), so a
scan of a folder above it leaves it out; without it, the next such scan indexes it again. Folders added to that list
by hand are removed from the index by the next scan of their parent. Not possible while a scan runs.

**Resolutions**: `w:` compares the width, `h:` the height, `r:` both at once, so `r:>720x1024` finds pictures wider
than 720 **and** taller than 1024 pixels. They are the sides of the picture as shown (a portrait phone photo or video
counts as portrait). Only photos and videos whose resolution a scan has read are found (see [Browsing](#browsing)).
An excluded resolution finds everything else, files whose resolution is not known included: `-w:1920` lists
documents too; `-w:1920 w:>0` keeps only the photos and videos of another width. `-r:1920x1080` leaves out the files
that are 1920 wide **and** 1080 high, not every file 1920 wide. `r:hr` finds the photos and videos whose shorter side
reaches the high-resolution threshold of its kind (see [Browsing](#browsing): 1500 pixels for photos, 720 for videos
by default), `r:lr` the ones whose resolution is known and below it.

**Durations**: `d:` compares how long a video or audio file lasts. A number is in minutes unless it ends with `s` (seconds) or `h`
(hours): `d:>90` and `d:>1.5h` are the same. A clock reads like a player's: `d:1:30` is a minute and a half,
`d:<1:30:00` under an hour and a half. Without an operator, the duration is "about": `d:10` finds 9½ to 10½ minutes,
`d:1h` half an hour to an hour and a half, `d:45s` 44½ to 45½ seconds; `d:=` is the exact second. Only videos and audio
files whose duration a scan has read are found; `-d:>1h` also lists everything else (photos, documents), `-d:>1h d:>0`
only the shorter videos and songs; add `e:mp3` (or another extension) to keep one kind. As for `e:`, `s:` and the others, a full-path search for a drive (`*D:\Movies`) takes `D:` as a
duration: search `*\Movies` instead.

Not sure what a query does? The speech-bubble button in the search box (shown while it has text) puts the search into
words under the location, updated as you type: words, tags, extensions and sizes each get their own colour, AND / OR
/ NOT are spelled out, and parts that are ignored or don't work as they may seem (an invalid size, `-e:mp4`, a `*`
not at the start, `clip.mp4` as a name) are pointed out. It stays open until you close it (remembered per browser).
`Sfe:Search:Explain` (default on, Administration › Interface › Search › **Explain searches**) removes the button for everyone.

While results are shown, the search box shows how many files were found in small grey numbers at its end ("10,000+"
beyond 10,000); hover it for the words. It disappears while you change the query, until the new results are in.

The search box remembers your last searches and suggests them again, like a web search engine: all of them, newest
first, when you click into the empty box, then those starting with what you type, followed by those containing it. The
characters you typed show in bold in each suggestion.
Pick one with the mouse or the arrow keys and Enter (Enter alone still searches what you typed); the × next to a
suggestion forgets it. A search is remembered when you press Enter, pick a suggestion, or leave the box on its
results. While a `t:` term is typed the box suggests tags instead. Searches are kept by each browser for itself
(there are no accounts), so other devices and visitors don't see them. `Sfe:Search:HistorySize` (default 10,
Administration › Interface › Search › **Searches remembered**) sets how many; 0 turns the history off. **Clear search history** (same
place) forgets this browser's searches.

Results are grouped by folder (sort **Path**) unless you pick another sort: name, date modified, size or type, in
either direction. Name and type sorts use the database order, not the natural number order used when browsing.

## Tags

Tagging follows [TagStudio](https://github.com/TagStudioDev/TagStudio)'s model:

- A tag has a **name** (not necessarily unique), an optional **shorthand** and **aliases**, all searchable: `t:lol`
  finds files tagged "League of Legends" if "LoL" is its shorthand. Double quotes in a tag name are ignored when
  searching: `t:"12 Vinyl"` finds the tag `12" Vinyl`.
- **Parent tags:** a file tagged `Shrek` (parent `Character`) is found by `t:character` too, through any number of
  levels. Loops are refused.
- **Disambiguation:** a parent can be shown after the name, e.g. "Freddy (FNAF)".
- **Category** tags group the tags inheriting from them in a file's tag list. **Hidden** tags keep their files out of
  search results unless the search names the tag (or a parent).
- New libraries start with **Favorite** and **Archived** (hidden), under **Meta Tags**. They can be edited, not deleted.
- Colors come from TagStudio's standard palette. Not ported: fields, tag icons, custom palettes, per-tag category
  exclusions.

The tag button on a file (or <kbd>T</kbd> in the viewer) opens its tags: type to add an existing tag or create one.
Click a tag on a file to edit it, a tag chip in the listing to search for it. Administration › **Tags** lists them all (also **Manage tags** under the tag sidebar).

**Several files at once:** tick their checkboxes (top-left of a tile, first column of the list). Shift-click selects
a range, <kbd>Ctrl</kbd>+<kbd>A</kbd> everything loaded, <kbd>Esc</kbd> clears. While files are selected, clicking a
file selects it. **Tags** in the bottom bar lists the tags on all of them and on some (with how many): adding a tag
puts it on every file, clicking a partial tag completes it, × removes it from all. The selection is cleared when you
open another folder or search.

**A whole folder, or every search result:** the tag button next to the location (right of the breadcrumbs) opens the
tags of every file of the folder you are in, or of every result of the current search (all of them, not only the ones
loaded). The tag button on a folder (shown on hover; always on touch screens) does the same for that folder. For a
folder, **Include sub-folders** extends it to every file below it. The dialog counts the files and shows the tags on
all of them and on some; each change (adding a tag, completing a partial one, removing one) says how many files it
touches and waits for you to confirm it. A folder is indexed first, so its files not indexed yet are tagged too
(this can take a while for a large tree, and is refused while a scan runs); search results are the indexed files the
search finds.

The **tag sidebar** (left of the top bar; a drawer on phones) shows a few tags in each section: **Most popular**,
**Last searched** (by `t:`), **Last viewed** (files opened) and **I feel lucky** (random; refresh for others). Click
a tag to search for it. The history is shared by everyone using the library; turn it off or clear it in Administration › Tags › Settings.

A file's tags survive rescans while it keeps its path, and follow it when it is moved or renamed (see
[Duplicates and moved files](#duplicates-and-moved-files)). Tagging a file that isn't indexed yet indexes it first.

## API

Every route is documented in the API reference at `/api/docs` (OpenAPI: `/openapi/v1.json`). Paths are relative to
the library root and use `/`; a path leaving the root (`..`, drive letters) is rejected. On Windows `\` is accepted
as a separator too; on Linux `\` and `:` are ordinary file name characters.

## What changed from the .NET Framework version

- **New UI:** grid and list layouts, sorting, infinite scroll that restores your position, a full-screen viewer
  (arrow keys and swipe), search highlighting with links to the folder, live scan progress, light and dark themes,
  usable on phones. Plus tags, trickplay, duplicates and moved files.
- **Cross-platform:** Linux and macOS as well as Windows (case-sensitive paths, `/` roots, `~`, systemd).
- **Fixes:** paths can't escape the root; rescans remove deleted folders (including top-level ones under a root
  scanned from the UI); bounded scan parallelism; an unreadable file no longer aborts a scan; `%`, `_` and `[` match
  literally; size filters keep all digits without a unit; an alternative made only of exclusions works; the unbounded
  thumbnail memory cache is gone; folder links looping back are not followed forever; an ffmpeg timeout no longer
  caches "no thumbnail"; the dismount timeout works.
- **Behaviour changes:**
  - Hidden and system files (`Thumbs.db`, `desktop.ini`) are skipped unless `ShowHiddenItems` is set.
  - New thumbnails are scaled to `Thumbnails:Width`; old full-size ones are still served. Browsers don't cache
    images, so a replaced photo shows up right away.
  - Scans only accept paths inside the root; shutdown and dismount are opt-in and use POST.
  - Search results are ordered by full path (folder by folder, then by name) by default and can be sorted like a
    folder; either way they page quickly in a library of millions of files.
  - Tagging a file that isn't indexed yet indexes only that file, not the whole folder.
