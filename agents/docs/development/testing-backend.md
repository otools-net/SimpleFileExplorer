# Backend test gotchas

Rules and infrastructure: [testing.md](testing.md). Search expectations: [search-terms.md](../features/search-terms.md).

- Never call `SqliteConnection.ClearAllPools()` in tests: under method-level parallelism it disposes handles other
  tests are opening (`ObjectDisposedException: SQLitePCL.sqlite3`, "database is locked"). `TestLibrary` clears only its
  own pool. If SQLite contention still shows up occasionally, rerun before chasing it.
- Microsoft.Data.Sqlite's pool can hand an in-use connection to a second `Open` while it grows (leaked-connection
  reclaim races `Activate`): "database is locked" from `SqliteConnection.Open()`, or a deadlocked pool.
  `SqliteOpenGate` (added by `SfeDatabase.Configure`) serialises EF's opens; open raw pooled `SqliteConnection`s in
  parallel only for a test of it (`ParallelContextsNeverShareAPooledConnection`).
- A scan's `progress.Errors` is only logged: pass a `WarningLog` (`Infrastructure/`) to `CoreServices.CreateAsync` and
  use it as the assert message to see why.
- ffmpeg tests (`TrickplayTests`, `SimilarVideoTests`) are inconclusive without `SFE_TEST_FFMPEG`/PATH. Tiny clips
  finish in milliseconds: assert "waiting" states before starting `RunAsync`. Moving test pictures: lavfi
  `mandelbrot`, `life`.
- With ffmpeg on PATH, the default `FfmpegPath` (`ffmpeg`) finds it, so scans in tests run it on the fixture's fake
  media too (assert either way).
- File-tracking tests make junctions/hard links with `cmd /c mklink /J|/H` (no admin); `TestLibrary` can't delete
  through a junction, remove it first. "Registered after" ordering needs separate scans (one scan is parallel).
- User's app running → locked DLLs: build/test with `--artifacts-path <scratch dir>`; there,
  `TheShippedChangelogHasTheRunningVersion` fails ([build.md](../environment/build.md)).
