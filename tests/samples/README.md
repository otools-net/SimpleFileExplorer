# Sample files

Files the tests and the agents' tools use, kept here so that no run has to make them again (drawing photos in a
browser, encoding clips with ffmpeg took ~40 s a run). Made by `agents/tools/samples/update-samples.mjs`; never edited
by hand.

| Folder | What | Made by | Used by |
|---|---|---|---|
| `library/` | The docs screenshots' sample library: photos, WebM clips, music, documents (text, Markdown, JSON, CSV, PDF, EPUB, Flash) | `agents/tools/docs-screenshots/make-media.mjs` (with `make-epub`, `make-pdf`, `make-swf`) | `docs-screenshots/run.mjs` (copied into its work folder, never used in place: seeding changes it) |
| `media/` | Small media for the backend tests (test patterns, a tone, clips in several codecs) | ffmpeg, from `media.json` (each file's arguments) | `tests/Sfe.Tests` through `TestSamples.CopyTo` / `PathOf` |
| `archives/` | The backend tests' 7z archives, encrypted zips, and zips of the methods .NET doesn't write (Deflate64, BZip2, LZMA, PPMd, XZ); plain zips and tars are written by the tests | 7-Zip, by `agents/tools/samples/make-archives.mjs` | `ArchiveReaderTests`, `ArchiveCatalogTests` through `TestSamples.ArchiveOf` (read in place) |

- `samples.lock.json` (written by the tool) records what each part was made from: the fingerprint of the library's
  generators and of the archives' generator, and of each medium's arguments. `run.mjs` copies `library/` only while that fingerprint matches;
  otherwise it makes the library itself and warns. `TestSamplesTests` fails when `media.json` and `media/` disagree.
- `.gitattributes` keeps these files byte for byte (`-text`): some text samples are CRLF on purpose.
- Videos come out different at each encoding: the tool keeps an existing one unless it is forced; everything else
  that comes out different replaces the old file.

## Adding or changing a sample

1. A medium for a backend test: add `{ "name", "for", "args" }` to `media.json` (the ffmpeg arguments without the
   output path; `{sample:<name>}` for another sample as input). A file of the docs library: change `make-media.mjs`.
2. `node agents/tools/samples/update-samples.mjs --ffmpeg <path>` (only what is out of date is made again; `--force
   <path or prefix>` remakes a video whose making changed, e.g. `--force "library/Videos/Holidays/Campfire.webm"`).
3. In the test: `TestSamples.CopyTo("<name>", _library.FullPath("Some/where.mp4"))`; the docs library is copied as
   a whole.
4. Commit the new files with `media.json` / `make-media.mjs` and `samples.lock.json`.

`node agents/tools/samples/update-samples.mjs --check` tells what is out of date without changing anything (no ffmpeg
needed; exit 1 when something is).
