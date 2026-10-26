# Archives: what is left (plan)

Code: `src/Sfe.Core/Files/Compression/`, `ExploreEndpoints`, `FileJobs` (extract, compress), `app/formats/archive/`,
`files/extract.dialog.ts`, `files/compress.dialog.ts`.

The compressed-archives epic (explore, passwords, extract, compress) shipped in 1.84 (`edge`, 2026-10-26); how it
works is in [archive-reading](../features/archive-reading.md), [archive-viewer](../features/archive-viewer.md),
[archive-extraction](../features/archive-extraction.md) and [compression](../features/compression.md). This file keeps
what it left for later, none of it promised. Tick `[x]` in the commit that does it; delete the file once empty.

## Explore

- [ ] Search across the archive (`GET …/explore/search?path=&q=&after=`: names containing, on the catalog, paged, a
  time cap per page) and a name filter inside a folder (a prefix range on the folder's index).
- [ ] Open a picture / text / PDF entry in the viewer itself (today: a new tab): a `LibraryItem`-like entry whose file
  URL is the entry route; videos need Range on a decompressed stream.
- [ ] Item menu: "Open as archive" for `.cbz`/`.epub`/`.docx`… ([context-menu](../features/context-menu.md)).
- [ ] Per-caller limits on folder pages and builds started (entry opens have `RequestLimits.Media`).
- [ ] Zip methods other than stored and deflate (deflate64, bzip2, lzma, zstd): SharpCompress' decompressors over
  `WindowStream`, if real archives need them.

## Extract

- [ ] Selection bar with one archive selected; several archives in one job.
- [ ] Asking the password again from the panel when an extraction fails on it (today: its message, then Extract…).
- [ ] Paths too long for Windows without long paths (.NET handles `\\?\` itself: only the index's limit applies now).
- [ ] Desktop: drop an archive with Alt to extract it?

## Measured, not assumed

- [ ] A tool in `agents/tools/archives/` that writes big archives into a temp folder (5 M entries, 10 000 nested
  folders, 100 000 files in one folder; zip, tar, and 7z/rar to check SharpCompress' memory with millions: its 7z
  archive holds every entry) and reports build time, peak memory, catalog size and page latency into
  [archive-reading](../features/archive-reading.md). 1 M zip entries were measured by hand (11 s, 49 MB peak, ~3 ms a
  page).
