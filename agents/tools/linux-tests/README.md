# Server tests on Linux (WSL)

`run.sh` runs `tests/Sfe.Tests` on Linux from WSL (Ubuntu 24.04, which has the .NET 10 SDK): it copies the working
tree (no `.git`, `node_modules`, build outputs) to `~/sfe` and runs `dotnet test` there, since building on `/mnt/c`
is slow and its outputs would mix with Windows'. The web client is not built (`-p:SfeSkipWebClient=true`).

```powershell
# from Windows (PowerShell): the filter goes through WSLENV (a `|` on wsl's command line is split by the shell)
$env:SFE_TEST_FILTER = 'FullyQualifiedName~DiskWritesTests|FullyQualifiedName~ListingReadOnlyRouteTests'
$env:WSLENV = 'SFE_TEST_FILTER'
wsl -d Ubuntu-24.04 -- bash /mnt/c/workspace/SimpleFileExplorer/agents/tools/linux-tests/run.sh --readonly-mount
```

- No `SFE_TEST_FILTER`: every test (long; a long run goes in the background, [long-runs](../../docs/environment/long-runs.md)).
- `--readonly-mount`: afterwards, the tests that need a folder on a read-only mount (`SFE_TEST_READONLY_FOLDER`,
  `DiskWritesTests.AReadOnlyMountIsTold`), run in a user namespace (`unshare --user --map-root-user --mount`) where a
  read-only bind mount needs no sudo. Inside it the process is root: tests needing a plain user go inconclusive, so only
  those of the mount run there.
- From Git Bash, `wsl … bash /mnt/…` is mangled (the path gets Git's prefix, `$PATH` is expanded before WSL): use
  PowerShell.
- Shut WSL down after (`wsl --shutdown`): it holds the RAM it took.
- The end-to-end suite on Linux has its own tool: [e2e-linux](../e2e-linux/README.md).
