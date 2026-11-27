---
name: run-tests
description: Run SFE's tests (dotnet test, npm test / ng test, Playwright e2e) so that the failing tests are named with their messages on the first run. Use whenever running tests in this repository, instead of calling dotnet test, npm test or npm run e2e directly.
---

# Running SFE's tests

Run tests through `agents/tools/tests/run.mjs`, never `dotnet test`, `npm test` or `npm run e2e` directly and never
piped through `tail` / `grep`: it prints only the failed tests (name, message, the test's own stack lines) and a
summary line per suite, so a failure is known after one run.

```sh
node agents/tools/tests/run.mjs                 # dotnet + web: the check before a change is done
node agents/tools/tests/run.mjs dotnet -- --filter FullyQualifiedName~SomeTests
node agents/tools/tests/run.mjs web -- --include=src/app/some/thing.spec.ts
node agents/tools/tests/run.mjs e2e -- --grep "@smoke|@reported"
```

- Arguments after `--` go to the runner (one suite named).
- The whole output is in the `output.log` whose path is printed: read it only when the report isn't enough.
- A hint about files locked by `testhost` means another session's tests are running: wait, don't kill it.
- The full suites are long: run them in the background (`agents/docs/environment/long-runs.md`).
- Details: `agents/tools/tests/README.md`.
