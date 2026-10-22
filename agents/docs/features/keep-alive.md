# Keep alive (network folders)

Code: `src/Sfe.Core/KeepAlive/` (`KeepAliveService`, `NetworkFolders`), `KeepAliveOptions` in `Options/SfeOptions.cs`,
`src/Sfe.Api/KeepAliveWorker.cs` (registered in `Tenancy/TenantHost.cs`). Tests: `KeepAliveTests`. Plan and what is
still to do: [keep-alive-plan](../development/keep-alive-plan.md). User docs: `docs/configuration.md` (Libraries on a
network share).

- One `KeepAliveService` per library (its container), looped by `KeepAliveWorker`: each round pings the library's root
  (`LibraryPaths.Root`) with one directory query (`EnumerateFileSystemEntries(...).FirstOrDefault()`), then waits
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
- State in memory only (`Status()`, `KeepAliveFolder`): no database, no stored file, nothing to migrate.
- Tests replace `Ping` and `IsNetwork` (internal properties) and call `PingAllAsync` directly, with an
  `IOptionsMonitor` of their own whose `Change` fires `OnChange`.
- It prevents the drop; it does not reconnect a lost mapping (the user's case is "mapping is lost").
