# Long runs: progress reports every 10 minutes

How an agent keeps the user informed while a long command runs (full `dotnet test`, `references.mjs`, the e2e suite,
`build/build.ps1`, docs screenshots, a portable or Docker build).

- **Expected to take more than 10 minutes?** Start it in the background, then create a recurring cron task (every
  10 minutes) that reports how it is going: elapsed time, the last meaningful lines of its output (tests passed /
  failed so far, current step), and whether it is still alive.
- A report is short (a few lines) and says only what changed; a failure seen in the output is reported at once, not
  at the next tick.
- Delete the cron task as soon as the run ends (success, failure or cancelled), in the same turn that reads its
  result; never leave one running after the session's work is done.
- A background `dotnet test … | tail` exits 0 even when the build fails (the pipe's last command wins): use
  `set -o pipefail`, and look for `error` and `Passed!` in the output, not only the exit code.
- Not sure of the duration? Assume more than 10 minutes for the full server test suite, the e2e suite, the build
  scripts and anything that builds the client and the server together.
