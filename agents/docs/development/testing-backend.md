# Backend test gotchas

Rules and infrastructure: [testing.md](testing.md). Search expectations: [search-terms.md](../features/search-terms.md).

- Never call `SqliteConnection.ClearAllPools()` in tests: under method-level parallelism it disposes handles other
  tests are opening (`ObjectDisposedException: SQLitePCL.sqlite3`, "database is locked"). `TestLibrary` clears only its
  own pool. If SQLite contention still shows up occasionally, rerun before chasing it.
- Microsoft.Data.Sqlite's pool can hand an in-use connection to a second `Open` while it grows (leaked-connection
  reclaim races `Activate`): "database is locked" from `SqliteConnection.Open()`, or a deadlocked pool.
  `SqliteOpenGate` (added by `SfeDatabase.Configure`) serialises EF's opens; open raw pooled `SqliteConnection`s in
  parallel only for a test of it (`ParallelContextsNeverShareAPooledConnection`).
- HTTP test servers are **External** (`Sfe:Mode` in `TestLibrary.Settings`): their clients have no remote address, so
  a Local server would refuse them. Local tests remove the key ([server-modes.md](../features/server-modes.md)).
- A scan's `progress.Errors` is only logged: pass a `WarningLog` (`Infrastructure/`) to `CoreServices.CreateAsync` and
  use it as the assert message to see why.
- **Waits on the machine's speed** (ffmpeg making test media, workers rendering trickplay or fingerprints) go through
  `Infrastructure/SlowWork` (`UntilAsync`, `RunFfmpegAsync`, `Limit`): past the limit the test is inconclusive, not
  failed; `SFE_TEST_TIME_FACTOR` multiplies the limits (the GitHub workflow sets 3). A wait checking SFE's own
  behaviour (a stream that must end) keeps failing, with `SlowWork.Limit` for its deadline. A test relying on Windows
  file locking is inconclusive elsewhere (it would just wait: `OldSheetHeldOpenDelaysTheNewTrickplayButDoesNotFailIt`).
- ffmpeg tests (`TrickplayTests`, `SimilarVideoTests`) are inconclusive without `SFE_TEST_FFMPEG`/PATH. Tiny clips
  finish in milliseconds: assert "waiting" states before starting `RunAsync`. Moving test pictures: lavfi
  `mandelbrot`, `life`.
- A stream test that checks a stream *while it runs* (`StreamingTests`) needs output bigger than the test server's
  response pipe (~64 KB): a copied 4 s clip (~50 KB) ends before the next request. Add `-vf noise=alls=60:allf=t`.
- With ffmpeg on PATH, the default `FfmpegPath` (`ffmpeg`) finds it, so scans in tests run it on the fixture's fake
  media too (assert either way).
- File-tracking tests make junctions/hard links with `cmd /c mklink /J|/H` (no admin); `TestLibrary` can't delete
  through a junction, remove it first. "Registered after" ordering needs separate scans (one scan is parallel).
- "The configured user limit (128) on the number of inotify instances has been reached" (Linux, CI) = file watchers
  leaked by test servers: a `JsonConfigurationSource` given its own provider (`ResolveFileProvider`) is not disposed
  with the configuration, `Program.cs` disposes them at `ApplicationStopped`. `ConfigurationWatcherTests` counts them
  (Linux only; WSL Ubuntu has .NET 10: `dotnet test -p:SfeSkipWebClient=true` on a copy outside `/mnt/c`).
- User's app running → locked DLLs: build/test with `--artifacts-path <scratch dir>`; there,
  `TheShippedChangelogHasTheRunningVersion` fails ([build.md](../environment/build.md)).
