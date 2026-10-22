# Keep alive (network folders)

Code: `src/Sfe.Core/KeepAlive/` (`KeepAliveService`, `NetworkFolders`), `KeepAliveOptions` in `Options/SfeOptions.cs`,
`src/Sfe.Api/KeepAliveWorker.cs` (registered in `Tenancy/TenantHost.cs`), `Endpoints/KeepAliveEndpoints.cs`; client
`app/admin/keep-alive-status.component.ts`, tab `keepAlive` of `settings/settings-schema.ts`. Tests: `KeepAliveTests`,
`KeepAliveRouteTests`, `admin-page.spec.ts`. Plan and what is still to do:
[keep-alive-plan](../development/keep-alive-plan.md). User docs: `docs/administration/keep-alive.md`,
`docs/configuration.md` (Libraries on a network share).

- One `KeepAliveService` per library (its container), looped by `KeepAliveWorker`: each round pings the library's root
  and the `Folders` of the settings (resolved by `LibraryPaths.TryResolve`; refused ones warned once) with one
  directory query each (`EnumerateFileSystemEntries(...).FirstOrDefault()`), then waits
  `IntervalSeconds` or until the options change (`IOptionsMonitor.OnChange` releases a semaphore: turning it on pings
  at once).
- `Mode` `Off` is the default (the user chose it: upgraded installs change nothing). `Auto` asks
  `NetworkFolders.IsNetwork` (links followed through `LibraryPaths.RealPath`; Windows: UNC or
  `DriveType.Network`; Linux/Docker: the deepest mount of `/proc/self/mountinfo`, a network file system type). The
  answer is kept per folder and forgotten on any options change.
- Every file system call (detection and ping) runs in `Task.Run` awaited with `WaitAsync(timeout)`; a folder whose
  previous ping is still blocked is skipped (one thread per dead share at most). The abandoned task's exception is
  observed by a continuation.
- Logs: information at the first success, a warning at the first failure of a run, information when it answers
  again, information once when `Auto` finds a folder local; never one line per ping.
- Settings `keepAlive.*` are library level (`EditableSettings.KeepAlive`), right `settings.keepAlive` (also the right
  of both routes); a library's change reaches its service through `IOptionsMonitor.OnChange` (pings at once).
- `PingNowAsync` (route `POST api/keep-alive/ping`) pings also while `Off`, as `Always`, to try a share first. Rounds
  never overlap (`_round` semaphore; a round lasts the timeout at most).
- The routes give paths root-relative and replace the root's full path in error messages: space administrators
  without `server.settings` never see where the library is on the server.
- Folders are keyed by `Path.TrimEndingDirectorySeparator`: `LibraryPaths.Root` keeps its ending separator, `TryResolve`
  doesn't, so a folder of the settings naming the root would otherwise be pinged twice. Folders taken off the settings
  leave the status at the next round.
- `Method: Touch` writes and deletes `ping.tmp` in `WorkFolder.Get("keep-alive")` (`.sfe/keep-alive`), only for the
  root and only when the work folder is in the library (`WorkFolderLocation.Library`); otherwise it reads, with one
  warning. `keepAlive.folders` is a `PerLibraryKeys` setting moved by `SettingsService.MoveFolderPaths` on renames.
- `KeepAliveService.State` (`None` off or nothing pinged, `Ok`, `Failing`; only folders pinged count) and
  `StateChanged`, raised after a round or when the settings change (turned off: at once), outside its lock. The change
  feed (`ChangeEndpoints`) sends `event: keepAlive` `{ state }` first, then on each change, to everyone who browses (no
  folder or error named); client `ChangeFeed.keepAlive` signal, shown by `MainMenuComponent` (dot on ⋮, a line in the
  menu, a link to Keep alive › Status with `settings.keepAlive`). The feed is opened by the library page only.
- State in memory only (`Status()`, `KeepAliveFolder`): no database, no stored file, nothing to migrate.
- Tests replace `Ping` and `IsNetwork` (internal properties) and call `PingAllAsync` directly, with an
  `IOptionsMonitor` of their own whose `Change` fires `OnChange`.
- It prevents the drop; it does not reconnect a lost mapping (the user's case is "mapping is lost").
