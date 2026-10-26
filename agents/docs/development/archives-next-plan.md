# Archives: what is left (plan)

Code: `src/Sfe.Core/Files/Compression/`, `ExploreEndpoints`, `FileJobs` (extract, compress), `app/formats/archive/`,
`files/extract.dialog.ts`, `files/compress.dialog.ts`.

The compressed-archives epic (explore, passwords, extract, compress) shipped in 1.84 (`edge`, 2026-10-26); how it
works is in [archive-reading](../features/archive-reading.md), [archive-viewer](../features/archive-viewer.md),
[archive-extraction](../features/archive-extraction.md) and [compression](../features/compression.md). This file keeps
what it left for later, none of it promised. Tick `[x]` in the commit that does it; delete the file once empty.

## Explore

- [ ] Videos and audio of an archive in place (today: a new tab): they need Range on a decompressed stream (a seek
  re-reads from the entry's start, or a temp copy).
- [ ] Links and pictures between the entries of a Markdown document of an archive (today: text): an entry route by
  path (`…/entry/{archive}/at/{path}`), then `MarkdownLinks` for entries.
- [ ] Zip methods other than stored and deflate (deflate64, bzip2, lzma, zstd): SharpCompress' decompressors over
  `WindowStream`, if real archives need them.

## Extract

- [ ] Paths too long for Windows without long paths (.NET handles `\\?\` itself: only the index's limit applies now).
- [ ] Desktop: drop an archive with Alt to extract it?

## Measured (`agents/tools/archives/measure.mjs`, results in [archive-reading](../features/archive-reading.md))

- [ ] 7z lists past `MaxSevenZipListMegabytes` (refused today): our own header reader streaming entries, as
  `ZipDirectory` does for zips, if people need bigger 7z.
- [ ] RAR with millions of entries: not measured (nothing free writes one).
