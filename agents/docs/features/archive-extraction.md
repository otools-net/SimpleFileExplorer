# Archive extraction

Code: `Core/Files/Compression/ArchiveExtractor.cs` (writing entries), `ArchiveReader.ReadThrough` (one pass),
`FileJobs.ExtractAsync` (the job: partial folder, placing, rights, indexing), `ExploreEndpoints.Extract`
(`POST api/files/jobs/extract`: ids → paths, unlock → password); client `files/extract.dialog.ts`,
`FileTransfers.extract`/`canExtract`, `ItemMenu` (`extractBeside`, `extract`), the archive viewer's **Extract…**,
`isExtractable` in `formats/formats.ts`. Tests: `ArchiveExtractorTests`, `ExtractJobTests`, `ExploreRouteTests`
(extract rows), `extract.spec.ts`, the archive view spec. User docs: `docs/user-guide/archives.md#extracting`. The
plan: [archives-next-plan](../development/archives-next-plan.md); reading: [archive-reading](archive-reading.md).

## Reading

- `ArchiveReader.ReadThrough(path, format, password, visit, read, cancel)`: every entry in archive order, each file
  with a stream valid during the visit only. Zip: its directory from one handle, the data from a second one
  (`KeptOpen` stops the entry's stream closing it). 7z: `SevenZipArchive.ExtractAllEntries()` (a solid block
  unpacked once). Rar, tar: the forward reader. Compressed tars: decompressed here, the end mark checked
  (`TailStream`). `.gz` alone (`Single`): the file named after the archive without its extension.
- An encrypted entry without a password throws only when it is opened (`Locked`): entries not chosen pass.
- SharpCompress reads the rest of an entry when its stream is **disposed**: a wrong 7z password throws then
  (`CryptographicException`), so `WrappedErrors` wraps `Dispose` too (checked 2026-10-26).
- Every zip entry's CRC is checked at its end (`CrcCheckStream`, `System.IO.Hashing.Crc32`), but AE-2's (none: its
  HMAC checks it). The viewer's entry route gets the same check.

## Writing (`ArchiveExtractor.Extract`)

- Names: `ArchiveEntryNames.Split` then `FileNames.Check` per segment (`PortableNames`: Windows' refusals on Linux
  too); the full path must start with the folder + separator; `MaxFullPath` (the index's `PathLengthLimit`).
- Left out and listed (first 1000, `LeftOutCount` all): refused names, links (never made), "other" entries, a ratio
  past `MaxCompressionRatio` (only when the format gives packed sizes), a second entry of the same name, a file where a
  folder is needed (and the reverse), a damaged entry (`ArchiveException` from its data: declared size, CRC).
- A damaged entry is deleted; an **encrypted** one failing is a wrong password: the whole extraction throws
  (`NeedsPassword`). An archive breaking between entries (`ArchiveException` outside an entry) stops it: what was
  written stays, `Stopped` says where. `MaxEntries` stops it too.
- Chosen entries: paths as the catalog gives them (`PathOf`, `/`, any case); each written from its own name (a chosen
  `a/b` → `b`), so a selection from one folder of the viewer comes out as it is seen.
- Files: `FileMode.CreateNew`, time from the entry (zip times are local, `ToUniversalTime`); folders' times not kept.
  `reserve(size)` before each file (the job's free-space check), `written` per buffer (the cancel checked between).

## The job (`FileJobKind.Extract`)

- `Start(request, actor, access, password)`: the password lives on the `Job` only while it runs (cleared in the
  `finally`), never in the request, the view or the log. `StartJob` (`api/files/jobs`) refuses the kind: only the
  extract route starts one.
- Rights per run: `library.browse`, `files.view`, `files.download` at the archive; `files.extract` at the target;
  replacing at a clash: the delete right there (`ClashAsync`, shared with moves and copies).
- Written into `{stem}.sfe-partial` in the target (`PartialFolder`, a free name); `RemoveLeftovers` deletes such
  folders of the target that no running job writes into (a stop of the server mid-way). The scan gate holds it.
- Then placed: `NewFolder` (default) → the one top folder keeps its name, else the stem (`StemOf`: `.tar.gz` too); a
  taken name gets `KeepBoth`. Not `NewFolder` → each top item through `ClashAsync` (asked, `forAll`). A `.gz` alone
  never gets a folder of its own.
- Names no route reaches (`.sfe` at the root, its 8.3 name `SFE~1`): `FileJobs.Reserved` (`TryResolve` of the
  target-relative path) — `ClashAsync` refuses them for every job kind (never placed, never replacing the work folder),
  the new folder's name takes another (`.sfe (2)`). Test: `ExtractJobTests.NothingIsPlacedIntoTheWorkFolderNorReplacesIt`.
- Any failure while extracting (wrong password, disk full `NoRoomException`, cancel) deletes the partial folder.
- Free space: `reserve` reads the drive before the first file, before any file of 64 MB or more, and every 64 MB.
- Progress: `BytesTotal` the archive's size, `SetBytes` the position in its file (zip: the data handle's).
- After: top files indexed at once, folders by a scan (one folder: it; several: the target; one scan at a time),
  default tags by the indexer, the listing invalidated, a log line (counts, never the password). The result message:
  files and size, left out (5 named), not placed, where it stopped.

## Route

- `POST api/files/jobs/extract {archive, target, newFolder, folders?, files?, conflicts, unlock?, password?}`
  (`files.extract`; `RequestLimits.Unlock`: each job with a password is a guess). Ids need a ready catalog (409
  `archive-reading` while it is read); an encrypted list needs the unlock (403 `wrong-password`); the root folder id
  means all; at most 10 000 ids. List, get, answer and cancel of `api/files/jobs` also take `files.extract`.

## Client

- `isExtractable`: explorable suffixes, compressed tars of any size, `.gz` `.bz2` `.xz`; needs `config.archives`.
- Item menu (one item): **Extract to “stem”** (`canExtractBeside`: browsing, `files.extract` in the folder listed)
  and **Extract…** (`ExtractDialog`: folder picker from the archive's folder, **Into a new folder**, password unless
  the viewer's unlock comes with it). The viewer: **Extract…** in its bar (the folder shown, all at the top) and in
  each row's menu; hidden where the dialog isn't (the `view?path=` page).
- The panel: icon `extract`, "Extracting … to …", bytes read; the result's message shown even when done (what was
  left out). The toast: what came out, **Open** to the new folder.
- Several archives: `canExtractEach` (all of them `canExtractBeside`), `extractEach` starts one job each, beside
  them, new folders; the selection bar (**Extract…** for one, **Extract** for several) and the item menu
  (**Extract each here**).
- A wrong or missing password ends the job with `FileOperationStatus.NeedsPassword` (`needsPassword`), nothing
  written. `FileTransfers` remembers each extraction it started (`extractions`, the request without the password,
  this page only): `canRetryExtraction`/`retryExtraction` reopen the dialog as asked (archive, entries chosen,
  `target`, `newFolder`) from **Enter the password…** in the panel and the toast's action; a job resumed after a
  reload keeps only its message.
