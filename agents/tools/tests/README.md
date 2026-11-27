# Test runs that name what failed

`run.mjs` runs a test suite and prints **only what failed**: one summary line per suite, then each failed test with
its message and its own stack frames. A passing run is one line. The runner's whole output goes to a log file in the
temp folder, whose path is printed, so the output never has to be cut with `tail` or `grep`, and a failure doesn't
need a second run to be named.

```sh
node agents/tools/tests/run.mjs                 # dotnet + web: the check before a change is done
node agents/tools/tests/run.mjs dotnet          # dotnet test (Sfe.Tests and Sfe.Client.Tests)
node agents/tools/tests/run.mjs web             # the client's unit tests (ng test, Vitest)
node agents/tools/tests/run.mjs e2e             # the end-to-end tests (Playwright)

# arguments after -- go to the one runner named
node agents/tools/tests/run.mjs dotnet -- -p:SfeSkipWebClient=true --filter FullyQualifiedName~DiskSpaceTests
node agents/tools/tests/run.mjs web -- --include=src/app/library/sidebar/storage-bar.component.spec.ts
node agents/tools/tests/run.mjs e2e -- --grep "@smoke|@reported"
```

What it prints:

```
dotnet: running (full output: C:\Users\…\Temp\sfe-test-runs\2026-11-27T13-48-06-780Z\dotnet\output.log)
dotnet: FAILED: 2412 passed, 1 failed, 5 skipped (311 s)

  FAILED DiskSpaceTests.ReportsTheFreeSpace
    Assert.AreEqual failed. Expected:<1>. Actual:<2>.
      at Otools.Net.Sfe.Tests.DiskSpaceTests.ReportsTheFreeSpace() in …\DiskSpaceTests.cs:line 19

  full output: …\dotnet\output.log
```

- The exit code is 0 only when every suite passed.
- Results come from the runners' structured reports, not their console text: TRX files (`dotnet test --logger trx`),
  Vitest's JSON reporter (`ng test --reporters=json --output-file=…`), Playwright's JSON reporter
  (`PLAYWRIGHT_JSON_OUTPUT_FILE`). The e2e config's HTML report is not written then: run `npm run e2e` for it.
- When the runner stops before any result (a build error, a crash), its error lines are printed instead: `error CS…`,
  `[ERROR] TS…` with the `file:line:col` after it, or the end of the log.
- Another session's `dotnet test` still running locks the test DLLs (`MSB3027 … locked by: "testhost"`): the run says
  so in a hint. Wait for it to end; don't kill another session's test host.
- Runs are kept in `%TEMP%/sfe-test-runs/<time>/<suite>/` (`output.log`, the TRX or JSON report), the last 10 only.
  Not `%TEMP%/sfe-tests`: the backend tests' libraries live there.
- A message is cut after 20 lines and a stack after 6 of the test's own frames (runner frames left out): the rest is in
  the log.
- A lower-case drive letter in the client's folder (`c:\workspace\…`, which VS Code and its terminals give) makes every
  spec file of `ng test` fail with "Vitest failed to find the runner", no test run: the test bed's setup
  (`angular:test-bed-init`) gets a second copy of `vitest`, loaded through the other spelling. `run.mjs` puts the
  drive letter in capitals; by hand, `cd C:\…` (Git Bash: `cd /c/…` is fine) before `npm test`. Found 2026-11-27.
- A `--reporters=json` without `--output-file` writes `src/Sfe.Web/.vitest/` (not ignored by git): delete it.
- Files: `run.mjs` (arguments, running, the run folder), `dotnet.mjs`, `web.mjs`, `e2e.mjs` (one per runner: command
  and reading its report), `report.mjs` (what is printed).
