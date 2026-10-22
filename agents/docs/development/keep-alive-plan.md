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
  works again, never one line per tick (the log would fill up). A ping much slower than usual is logged at debug: it
  means the session had to be made again.
- **UI (phase 2):** a "Keep alive" group in the library's settings (right and tab: see Open questions) with the fields
  above and the status of each target ("Last ping 2 min ago, 14 ms" / "Failing since 10:42: <message>"), and
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

## Open questions

1. (Decided: `Off`.)
2. (Decided: all of them.) **A test share** is still needed for the checks by hand.
3. **Does a directory listing really reset the server's idle timer?** SMB clients cache: Windows keeps directory
   information ~10 s (`DirectoryCacheLifetime`) and SMB 3 directory leases can serve listings without asking the
   server; Linux CIFS sends its own echo every 60 s (`echo_interval`), which keeps the TCP connection but may not count
   as activity for the server. To verify on a real share (on the file server: `Get-SmbSession` / `net session` idle
   time; or Wireshark). Decided: even if not, `Touch` stays opt-in.
4. (Decided: the mapping is lost; no reconnect.)
5. **Where the settings live:** library level under the Scanning tab (right `settings.scanning`), or a new tab and
   right (`settings.keepAlive`, `since: Revision + 1`)? Proposed: Scanning tab, no new right.
6. **Folders outside the libraries** (e.g. a cache folder on a share): configuration-only absolute paths
   (`Sfe:KeepAlive:ExtraPaths`, never editable from the UI: it would let an administrator probe the server's disks)?
   Proposed: not now.
7. **Skip a ping when the folder was just used** (browsing, a scan, streaming): it saves nothing that matters (one
   query per interval) and needs a hook in every file access: proposed not to.

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
- [ ] Verified by hand on a real share (open question 3), on Windows SMB, a NAS, Linux CIFS/NFS and Docker: the
      server's idle time stays low while SFE is idle. Needs a test share (note how to reach it in
      [dev-machine](../environment/dev-machine.md)).
- [x] (unreleased, edge) `CHANGELOG.md`; no `docs/upgrading.md` note (default `Off`).

### Phase 2 — settings and status in the UI

- [ ] `EditableSettings` keys (library level, right per open question 5), `SettingsService.Validate` ranges,
      `settings-schema.ts` group, `SIMPLE_SETTINGS`.
- [ ] `GET api/keep-alive` (each target: path shown root-relative, network or not, last ping, duration, error) and
      `POST api/keep-alive/ping`, with `RequireRight`; `AccessTests.EveryRouteNamesARight` passes.
- [ ] Status and **Ping now** in the settings group; client specs.
- [ ] User docs with a screenshot (`agents/tools/docs-screenshots`; the sample library is local, so the status shows
      "not on the network" unless the mode is `Always`).

### Phase 3 — more folders and the write ping

- [ ] `Folders` (root-relative list, `PerLibraryKeys`, followed by `PathMoves` on rename), with the folder picker.
- [ ] `Method: Touch`: create, write and delete `keep-alive.tmp` in `WorkFolder.Get("keep-alive")` (only when the
      work folder is in the library, else it falls back to `List` with a warning); never in the library's own folders.
- [ ] Tests and docs as above.

## Found while building

- Phase 1's facts are in [keep-alive](../features/keep-alive.md).
- The two `Directory`/`DriveInfo` calls of detection touch the share too (`RealPath` reads links on it): detection
  runs inside the pinged task, under the same timeout, never on the loop.
