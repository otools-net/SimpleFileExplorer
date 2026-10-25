# Sample files

Code: `tests/samples/` (`library/`, `media/`, `media.json`, `samples.lock.json`, `README.md`),
`agents/tools/samples/` (`update-samples.mjs`, `samples.mjs`), `tests/Sfe.Tests/Infrastructure/TestSamples.cs`,
`TestSamplesTests`, `agents/tools/docs-screenshots/run.mjs` (step `sample library`).

- **Files a test or a tool needs are committed, not made per run.** Pictures, audio, video, documents (EPUB, PDF,
  SWF…) go in `tests/samples/`: the backend tests' media in `media/` (listed in `media.json` with the ffmpeg arguments
  that make them), the docs screenshots' library in `library/` (made by `make-media.mjs`). A test copies them
  (`TestSamples.CopyTo(name, destination)`: dated now, as a file just written) or reads them in place
  (`TestSamples.PathOf`), never into the samples folder.
- **Keep them up to date in the same commit:** a new sample, or a change to how one is made (`media.json`,
  `make-media.mjs`, `make-epub`/`make-pdf`/`make-swf.mjs`), runs `node agents/tools/samples/update-samples.mjs --ffmpeg
  <path>` and commits the files with `samples.lock.json`. `update-samples.mjs --check` tells what is out of date.
  Guards: `TestSamplesTests` (list and folder agree), `run.mjs` warns and makes the library itself while the lock's
  fingerprint differs.
- Small synthetic files written by the test itself (a header of a few bytes, a text, `TestLibrary.AddFile`'s padded
  files) stay in the test: they show what is tested. A sample is for what needs a program (ffmpeg, a browser) or
  takes time to make.
- Videos are re-encoded differently at each run (libvpx, libx264 threads): the tool keeps them unless forced
  (`--force <path or prefix>`), so updating doesn't churn the repository. Everything else made by the generators
  comes out byte-identical (checked 2026-10-25).
- Tests needing ffmpeg for SFE's own work (probe, trickplay, streaming) still need `SFE_TEST_FFMPEG` and stay
  inconclusive without it; the streaming tests check it before copying (`AddVideoAsync`).
- Size (2026-10-25): `library/` 14 MB, `media/` 6.4 MB (identical copies are stored once by git). `tests/` isn't in
  the Docker build context (`.dockerignore`) nor in the packages.
- `.gitignore` ignores `*.log` and other build names: `!tests/samples/**` keeps every sample (the docs library's
  `Photo backup.log` would be left out without it). `.gitattributes` marks them `-text` (byte for byte).
