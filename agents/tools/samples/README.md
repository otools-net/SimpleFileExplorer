# Sample files

Keeps `tests/samples/` (what it holds and how to add a sample: `tests/samples/README.md`) up to date. Node.js 22+,
ffmpeg, and Microsoft Edge for the library's photos, 7-Zip for the archives (each only when what it makes is out of
date); no npm packages.

```sh
# make again what is out of date (the library when make-media.mjs or its helpers changed; media whose arguments changed or are missing)
node agents/tools/samples/update-samples.mjs --ffmpeg <path to ffmpeg>

# remake a video too (videos are kept unless forced: they differ at each encoding)
node agents/tools/samples/update-samples.mjs --ffmpeg <path> --force "library/Videos/Holidays/Campfire.webm" --force media/life-20s.mp4

# the archives alone (7z found on the PATH or in C:/Program Files/7-Zip without it)
node agents/tools/samples/update-samples.mjs --sevenzip <path to 7z>

# what is out of date, changing nothing (exit 1 if anything is)
node agents/tools/samples/update-samples.mjs --check
```

| File | |
|---|---|
| `update-samples.mjs` | The update: makes the library in a temporary folder with `make-media.mjs` and copies over what changed (new files added, changed ones replaced except videos, files no longer made deleted); runs ffmpeg for each medium of `media.json` that is missing or whose arguments changed (and those made from it); writes `samples.lock.json` |
| `make-archives.mjs` | The tests' 7z archives (`archives/`): a small tree with fixed times, solid, encrypted data, encrypted names. The plain one comes out byte-identical at each run; the encrypted ones differ (a random salt) |
| `samples.mjs` | Paths, the fingerprints (generators of the library and of the archives, arguments of each medium; line endings ignored), `libraryIsCurrent()` for `docs-screenshots/run.mjs` |
