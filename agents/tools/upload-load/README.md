# Upload load test

Sends many small files, then one large file, through `Sfe.Client` (the resumable uploads of
[uploads](../../docs/features/uploads.md)) and checks what arrived: every small file listed back with its size, the
large one's SHA-256 read by the server (`Properties.ChecksumAsync`) equal to the bytes made, no unfinished upload left.
Prints timings per run and per request kind (check, start, chunk, finish), and the server's memory when it started
the server itself. A file-based .NET 10 app (`#:project` on `src/Sfe.Client`); nothing to install.

```sh
# the plan's run (release 2.0, step 4.3c): 5,000 files of 4 KB, two at a time (as the page sends them), then 1 GB
dotnet run -c Release agents/tools/upload-load/UploadLoad.cs

# the large file on a scratch disk: 8 GB at most (owner's rule, testing.md); the work folder holds the source file,
# the library and the upload's part, so 2 × the size on that disk
dotnet run -c Release agents/tools/upload-load/UploadLoad.cs -- --work D:/sfe-upload-load --small 0 --large-mb 8192

# against a server already running (a token with the upload right; its folder must not exist yet)
dotnet run -c Release agents/tools/upload-load/UploadLoad.cs -- --url http://nas:5080/ --token sfe_… --folder "Load 2"
```

- Options: `--small` (5000), `--small-bytes` (4096), `--parallel` (2), `--large-mb` (1024; above 8192 refused),
  `--folder` (`Upload load`), `--work` (`<tmp>/sfe-upload-load`), `--port` (5097), `--skip-build`, `--keep`.
- Without `--url`/`--token` it builds `Sfe.Api` in **Release** into `<work>/build` (Debug answers each request a few
  milliseconds slower), starts it on an empty library in `<work>/library` (External mode, SQLite, as the end-to-end
  tests), makes the administrator and a token, and stops it at the end; the app's output goes to `<work>/app.log`.
  The library, data and source files are removed at the end unless `--keep`; the build is kept for `--skip-build`.
- A run with `FAILED` in its report exits 1.
- Server-side time of each request: run with `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information`
  in the environment (`env 'Logging__…=Information' dotnet run …` in Git Bash: the dots are not valid in `export`) and
  read "Request finished … ms" in `app.log`.

## Measured (2026-11-26, the development machine: Windows; work folder on C:, the 8 GB run on D:)

| Run | Result |
|---|---|
| 5,000 files of 4 KB, 2 at a time | 120 s, 42 files/s; per file median 39 ms, 95th 64 ms; server memory 175 → 218 MB peak |
| per small file (medians) | list unfinished 1.3 ms, check 3.2, start 7.8, chunk 4.8, finish 13.2 ms |
| one file of 1 GB | 14 s, 72 MB/s (8 MB chunks one after the other, 70 ms each); SHA-256 equal; memory flat (~210 MB) |
| one file of 8 GB on D: | 115 s, 71 MB/s, steady (each tenth in ~11.5 s); SHA-256 equal (server read it in 49 s); memory 173 → 218 MB peak |

Found and fixed by the first runs: each request made with an access token resolved the server's settings twice
(`SettingLayers.ServerLevel`, ~1.5 ms each, now kept until the settings change): 155 ms per small file before, 13
files/s.
