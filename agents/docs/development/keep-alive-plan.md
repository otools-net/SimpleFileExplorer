# Keep alive (network folders): plan and progress (epic)

Code: `src/Sfe.Core/KeepAlive/` (`KeepAliveOptions` in `SfeOptions`, `NetworkFolders`, `KeepAliveService`),
`src/Sfe.Api/KeepAliveWorker.cs` (registered per library in
`Tenancy/TenantHost.cs`, like `TrickplayWorker`); planned: `Endpoints/KeepAliveEndpoints.cs`; client
`src/Sfe.Web/src/app/settings/settings-schema.ts`, `app/admin/`. Related: [tenants](../architecture/tenants.md),
[settings-new-key](../features/settings-new-key.md), [work-folder](../features/work-folder.md), [scale](scale.md),
[logs](../features/logs.md).

Goal: some libraries live on network folders (SMB shares, mapped drives, CIFS/NFS mounts). When nothing touches a
share for a while, the file server drops the session (Windows `autodisconnect`, 15 min by default; many NAS a few
minutes), and the next access is slow, fails, or needs a reconnection SFE can't make. **Keep alive** touches each of
those folders every N seconds with a cheap file operation, so the session never goes idle.

## How to use this file

- Built over several sessions: **update it in the commit that does the work** (tick `[x]` with the version, move
  findings into the phase), so the next session starts here: take the first phase that isn't done. One phase = one
  session = one commit = one minor version.
- Gotchas found while building go in [Found while building](#found-while-building); questions are decided at the start
  of the phase that needs them and move to [Decisions](#decisions).
- When a phase is done, its lasting facts go into a feature note (`agents/docs/features/keep-alive.md`, indexed in
  `AGENTS.md`); this file keeps the checklist.

Status legend: `[ ]` to do, `[~]` started, `[x]` done (version), `[-]` dropped (why).

## Design

- **Per library**, in the library's own container: each library keeps its own root alive. A
  `KeepAliveWorker : BackgroundService` per `TenantHost` runs `KeepAliveService.RunAsync`, like `TaskScheduleWorker`.
  Two libraries on the same share both ping it: harmless, nothing to share.
- **Settings** (`Sfe:KeepAlive`, library level, `EditableSettings` in phase 2):
  - `Mode`: `Off` (default), `Auto` (only folders found to be on the network), `Always` (every target, e.g. a network
    drive SFE can't recognise).
  - `IntervalSeconds`: default 240 (under the 5 min idle limit common on NAS), range 10–3600.
  - `TimeoutSeconds`: default 30: a ping taking longer counts as failed.
  - `Folders` (phase 3): more folders inside the library, root-relative (folders reached through a link/junction to
    another share, which `Auto` can't see without walking the library).
  - `Method` (phase 3): `List` (default, read-only) or `Touch` (writes and deletes a small file in the work folder;
    only when an administrator chooses it).
- **Targets** (`KeepAliveTargets`): the root, plus `Folders` resolved with `LibraryPaths.TryResolve` (refused paths
  dropped with one warning). `Auto` keeps those `NetworkFolders.IsNetwork` says yes to:
  - Windows: a UNC path (`\\server\share`), or `DriveInfo(...).DriveType == DriveType.Network` (mapped drive). Junction
    targets resolved first (`FileSystemInfo.ResolveLinkTarget(true)`).
  - Linux / Docker: the longest mount point of `/proc/self/mountinfo` containing the path has a network file system
    type (`cifs`, `smb3`, `nfs`, `nfs4`, `fuse.sshfs`, `fuse.rclone`, `9p`…). Inside a container a host's CIFS mount
    bound to `/library` still shows `cifs`.
  - Detection is cached and done again when the settings change (a mapped drive may appear after start).
- **The ping** (`KeepAlivePinger`): `List` = `Directory.EnumerateFileSystemEntries(target).FirstOrDefault()`: one
  directory query on the server, whatever the folder's size (rule [scale](scale.md)). Run on the thread pool with
  `WaitAsync(timeout)`: a dead share can block a file call for a minute or more, so the worker never waits on it.
  A target whose last ping is still running is skipped (never more than one blocked thread per target).
- **State and logs:** per target last ping time, duration, result, failures in a row, kept in memory (nothing stored:
  no database change). The log gets a warning when a target **starts** failing and an information line when it
  works again, never one line per tick (the log would fill up).
- **UI (phase 2):** a **Keep alive** tab of its own in the library's settings (right `settings.keepAlive`) with the
  fields above and the status of each target ("Last ping 2 min ago, 14 ms" / "Failing since 10:42: <message>"), and
  **Ping now**. Routes `GET api/keep-alive` (status) and `POST api/keep-alive/ping`, each with its right.

## Decisions

Asked 2026-10-22:

- **Default `Off`**: upgraded installs change nothing until an administrator turns it on (open question 1). No
  `docs/upgrading.md` note needed; the changelog says it exists.
- **Every kind of setup counts** (open question 2): Windows SMB shares, NAS (Synology, QNAP…), Linux CIFS/NFS mounts and
  Docker. `NetworkFolders` covers UNC, mapped drives and mountinfo types; a real check is still needed on each.
- **What users see today: the mapping is lost** (open question 4). Keep alive prevents the drop; it can't bring a lost
  mapping back, and docs say so. A reconnect command stays out of scope.
- **The write ping only when turned on** (open question 3's fallback): `List` stays the default everywhere, even if a
  listing turns out not to reset the server's idle timer; `Touch` is a per-library option (phase 3).
- **A tab and a right of its own** (open question 5): Administration › **Keep alive**, library-level settings under a
  new right `settings.keepAlive` (`since: RightCatalog.Revision + 1`, given to the built-in Administrators group of
  existing spaces by `GrantNewRightsAsync`).
- **Library folders only** (open question 6): no paths outside the libraries for now.
- **Always ping** (open question 7): no skipping when the folder was just used.
- **The user checks real shares themselves** (open question 3): the steps are in
  [Checking on a real share](#checking-on-a-real-share); their findings go in [Found while building](#found-while-building).

## Open questions

1. (Decided: `Off`.)
2. (Decided: all of them.)
3. **Does a directory listing really reset the server's idle timer?** SMB clients cache: Windows keeps directory
   information ~10 s (`DirectoryCacheLifetime`) and SMB 3 directory leases can serve listings without asking the
   server; Linux CIFS sends its own echo every 60 s (`echo_interval`), which keeps the TCP connection but may not count
   as activity for the server. To verify on a real share (on the file server: `Get-SmbSession` / `net session` idle
   time; or Wireshark). Decided: even if not, `Touch` stays opt-in. The user checks it on their servers.
4. (Decided: the mapping is lost; no reconnect.)
5. (Decided: own tab, right `settings.keepAlive`.)
6. (Decided: not now.)
7. (Decided: always ping.)

## Phases

### Phase 1 — the worker, configuration only

- [x] (unreleased, edge) Open questions 1, 2, 4 and the write ping decided with the user.
- [x] (unreleased, edge) `KeepAliveOptions` (`Mode`, `IntervalSeconds`, `TimeoutSeconds`) in `SfeOptions`; defaults
      in both `appsettings.json`; `docs/configuration.md` (Libraries on a network share).
- [x] (unreleased, edge) `NetworkFolders.IsNetwork` (links through `LibraryPaths.RealPath`; Windows: UNC,
      `DriveType.Network`; Linux: `FileSystemTypeAt(mountinfo text, path)`).
- [x] (unreleased, edge) One class, `KeepAliveService` (targets, ping, state; `Ping` and `IsNetwork` replaceable by
      tests), instead of the three planned; `RunAsync` waits on a semaphore released by `OnChange` (no `TimeProvider`:
      tests call `PingAllAsync`); `KeepAliveWorker` in `TenantHost`.
- [x] (unreleased, edge) Logs: first success, first failure, recovery, "not on the network" once.
- [x] (unreleased, edge) `KeepAliveTests` (24): mountinfo samples (nested, prefix, `\040`, stacked), UNC forms, Off,
      Always, Auto local/network, failure and recovery logged once, a blocked ping (timeout, never stacked, recovery),
      turning it on wakes the loop, the real ping.
- [ ] Verified by the user on real shares (open question 3), on Windows SMB, a NAS, Linux CIFS/NFS and Docker:
      [Checking on a real share](#checking-on-a-real-share).
- [x] (unreleased, edge) `CHANGELOG.md`; no `docs/upgrading.md` note (default `Off`).

### Phase 2 — settings and status in the UI

- [x] (unreleased, edge) Right `settings.keepAlive` (`Tenant`, Administrators, Write, `since: 11`), in
      `core/rights.ts` and `SETTINGS_RIGHTS`; `RightsByPrefix` maps `keepAlive.` to it.
- [x] (unreleased, edge) `EditableSettings.KeepAlive` (library level), `Validate` ranges and mode; a `keepAlive` tab in
      `settings-schema.ts`; section **Keep alive** (group Library, icon `refresh`) with sub-pages `status` and
      `settings`; `keepAlive.mode` and `.intervalSeconds` in `SIMPLE_SETTINGS`.
- [x] (unreleased, edge) `GET api/keep-alive` (`KeepAliveView`: mode, interval, folders root-relative, the root's full
      path replaced in errors) and `POST api/keep-alive/ping` (`PingNowAsync`: also while Off, as `Always`); one round
      at a time (`_round` semaphore). `KeepAliveRouteTests` (4).
- [x] (unreleased, edge) `KeepAliveStatusComponent` (admin › Keep alive › Status, refreshed every 10 s): state line,
      folders, **Ping now**, **Settings**; 4 specs in `admin-page.spec.ts`; `docs/reference/rights.md`.
- [x] (unreleased, edge) `docs/administration/keep-alive.md` with `admin-keepAlive-status.png` (taken after **Ping
      now**: the sample library is local) and `admin-keepAlive-settings.png`; `docs/configuration.md`,
      `docs/upgrading.md` (the new right).

### Phase 3 — more folders and the write ping

- [x] (unreleased, edge) `Folders` (root-relative list, `PerLibraryKeys`, moved by `SettingsService.MoveFolderPaths`
      on renames; `.sfe` and paths leaving the library refused). Removed from the status when taken off.
- [-] Folder picker: settings lists have none (trickplay's folders are a text list too); a text list it is.
- [x] (unreleased, edge) `Method: Touch`: writes and deletes `ping.tmp` in `WorkFolder.Get("keep-alive")`, root only,
      only when the work folder is in the library; else reads with one warning. Never in the library's own folders.
- [x] (unreleased, edge) Tests (`KeepAliveTests` +4, `KeepAliveRouteTests` +1, the rename test of
      `FileOperationsTests`, a spec), `docs/administration/keep-alive.md`, `docs/configuration.md`, changelog.

All three phases are built. Left: the user's checks on real shares (below); their findings go in
[Found while building](#found-while-building), and may change the default `Method`.

## Checking on a real share

The user runs this on their servers (open question 3) and reports what they see. For each setup: SFE with
`Sfe:KeepAlive:Mode` = `Auto` and `IntervalSeconds` = `60`, the library on the share, nobody using SFE (no browsing,
scans, trickplay or schedules running) during the test.

1. **SFE's log** (Administration › Logs, or the console): `Keep alive: pinging <folder> every 60 s` must appear
   within a minute of the start. `is not on the network` means detection missed it: note the folder and, on Linux or
   Docker, its line of `cat /proc/self/mountinfo`; then retry with `Mode` = `Always`.
2. **Windows file server**: shorten the idle limit for the test (admin PowerShell on the file server:
   `Set-SmbServerConfiguration -AutoDisconnectTimeout 2 -Force`, note the old value of
   `(Get-SmbServerConfiguration).AutoDisconnectTimeout` first, 15 by default). Then for 10 minutes watch
   `Get-SmbSession | Where-Object ClientComputerName -match '<SFE host>' | Select-Object ClientUserName, SecondsIdle, NumOpens`:
   with keep alive, `SecondsIdle` keeps falling back under ~60 and the session stays; set `Mode` = `Off`, restart SFE,
   and the session should disappear after about 2 minutes (the control). Restore the old timeout.
3. **NAS (Samba: Synology, QNAP…)**: over SSH on the NAS, `sudo smbstatus -b` lists the sessions; check the SFE host's
   session is still there after more than the NAS's idle limit (Samba `deadtime`, in minutes; DSM has no direct
   setting) with keep alive on, and gone with it off.
4. **Linux CIFS / NFS client and Docker**: as 2 or 3 on the server side; on the SFE host, `cat /proc/fs/cifs/DebugData`
   (CIFS) shows the session. In Docker, step 1's log tells whether the mount was recognised.
5. **Report**: per setup, the log lines of keep alive, whether the session stayed, and any `does not answer` warning
   with its message. If the session is dropped despite the pings, the listing is answered from the client's cache:
   say so (then `Touch`, phase 3, is the way, opt-in).

## Found while building

- Phase 1's facts are in [keep-alive](../features/keep-alive.md).
- `LibraryPaths.Root` keeps its ending separator and `TryResolve` doesn't: folders are keyed without it (phase 3), or
  a folder of the settings naming the root is pinged twice.
- The two `Directory`/`DriveInfo` calls of detection touch the share too (`RealPath` reads links on it): detection
  runs inside the pinged task, under the same timeout, never on the loop.
