# Compressed archives: plan and progress (epic)

Goal: work with compressed archives inside the library. **Compress** files and folders into a `.zip` saved
in the library, at the compression level the person chooses and with a password if they want one, **explore** an
archive (its folders, sizes, a preview of an entry) without unpacking it, and **extract** all of it or the chosen
entries into a folder; archives protected by a password open with it. Everything runs on the server, as background jobs like
move and copy. The existing zip *download* ([archives](../features/archives.md)) stays as it is: it streams to the
browser and writes nothing to the library.

## How to use this file

- Tick `[x]` in the commit that does the work; what stays true goes into a feature note
  ([archive-reading](../features/archive-reading.md) for the core, [archive-viewer](../features/archive-viewer.md) for
  exploring, [archive-extraction](../features/archive-extraction.md) for extracting, `features/compression.md` for
  compressing).
- When nothing is left: delete this file and its links (see `AGENTS.md`, "Keeping these notes current").

Status legend: `[ ]` to do, `[~]` started, `[x]` done, `[-]` dropped (why).

## Decisions to take first

- [x] **Library for reading** (2026-10-26): `SharpCompress` 0.50.7 (MIT, managed, ~2.5 MB) for 7z, rar and tars;
  zip by our own directory reader ([archive-reading](../features/archive-reading.md)). `System.IO.Compression`
  stays the zip *writer* (already used for downloads: streaming, Zip64, async).
- [x] **Creating 7z: dropped** (2026-10-26): the program would mean a setting, an install step and a process per
  job. Compress writes zip only; 7z is still explored and extracted. (SharpCompress upstream has had a 7z writer
  since 0.50.x: if 7z creation is ever wanted, it needs no program.)
- [x] **Exploring: a viewer** (decided 2026-10-26): a **viewer format** (`ARCHIVE_FORMAT` in the registry, like
  CSV/EPUB) with its own folder navigation inside the view; archives as folders of the listing stay a later epic.
- [x] **Rights** (decided 2026-10-26): two new rights, `files.compress` and `files.extract` (`Kind = Write`,
  `AtFolder = Write`, `GuestAllowed = false`, `DefaultGroups = []`, `since`: nobody gets them by upgrade), as every
  file operation ([file-operations](file-operations.md)). Exploring uses `files.view` (listing entries) and
  `files.download` (opening or saving one entry): an archive's content is the file's content.
- [x] **Passwords** (decided 2026-10-26): opening, exploring and extracting archives protected by a password, and
  creating zips with one ("Passwords" below). Neither .NET 10 (`ZipArchiveEntry.IsEncrypted` only) nor SharpCompress
  0.50.7 (no password in any writer's options) writes encrypted archives: **SFE writes its own zips** (also what
  gives the person the compression level). **AES-256 only**: secure; opened by 7-Zip, WinRAR, macOS `ditto`,
  Windows 11's newer zip support, not by older Windows Explorer nor macOS Archive Utility (the dialog says so).
  ZipCrypto is read (old archives), never written.
- [x] **Compression level** (decided 2026-10-26): the person picks it for each archive ("Compression level" below).

## Formats

| Format | Explore / extract | Create | What listing reads (never the data) |
|---|---|---|---|
| `.zip` (Zip64, UTF-8 and code-page names) | yes | yes (`System.IO.Compression`) | the central directory at the end (one sequential read) |
| `.7z` (solid or not, LZMA/LZMA2) | yes | no (dropped) | the header at the end (may itself be LZMA-packed: only it is unpacked) |
| `.rar` (4, 5) | yes, read only (RAR can't be written freely) | no | one header per entry, spread over the file: seeks past each entry's data |
| `.tar` | yes | later, if asked | one header per entry: seeks past each entry's data |
| `.tar.gz`/`.tgz`, `.tar.bz2`, `.tar.xz` | extract; explore only up to a size (above it: not offered) | later, if asked | **the whole stream decompressed**: no index exists |
| `.gz`, `.bz2`, `.xz` alone (one file) | extract only (exploring not offered) | no | nothing (one file) |

Detected by the file's first bytes, not only the extension (a renamed `.zip`); `.cbz`/`.cbr` (comics), `.epub`, `.docx`
are zips too, but keep their own viewer: the archive viewer only takes them through "Open as archive" in the menu.

**Exploring is offered only where it works.** One rule, `isExplorable(item)` in the client (extension and size, from
the listing: no file read per item), decides every way in: the click that opens the viewer (`ARCHIVE_FORMAT.matches`),
"Open as archive", the viewer's ‹ / › steps. Not explorable: `.gz`/`.bz2`/`.xz` alone, compressed tars over
`Files:Archives:MaxCompressedTarMegabytes` (sent in `/api/config`, as the text extensions). Those get no viewer (a click acts as
for any file without one) and keep **Extract** in the menus. The server applies the same rule (409 `not-explorable`,
also for a file whose first bytes aren't what its name says), so a hand-made request gets no catalog build.

## Scale: millions of entries, thousands of nested folders

Exploring must never unpack the data, must answer a folder of the archive in milliseconds after the first read, and
must keep the server's memory, CPU and disk use bounded whatever the archive holds ([scale](scale.md)).

- **Read the list once, into a catalog on disk** (built: [archive-reading](../features/archive-reading.md),
  "Catalogs"; 1 M zip entries: 11 s, 49 MB peak, ~3 ms a page). Every later question is an indexed query on it.
- [x] **Where catalogs live**: the library's cache folder, beside the thumbnails
  (`{cache}/archives/v1/{hash of path + FileVersion.Token}.db`; `archives` is not hex, so the cache layout step
  `V1_40_0/ShardCacheLayout` never touches it; never in the library's `.sfe`, which may be a slow share). The archive
  changed (size, time) = another key, the old catalog dropped. One code path for all sizes (a 10-entry catalog
  builds in milliseconds: no in-memory special case).
- [x] **Deep trees in the view**: the breadcrumb from `Trail` (collapsed: `root › … › parent › folder`, the middle
  in a menu), "Go to" through `FindFolder`.
- **Bounded cost on the server**:
  - [x] catalog builds are **background work, one at a time per server** (`Files:Archives:CatalogBuilds`, 1), queued
    beyond, with progress (entries read, bytes) that the view shows ("Reading the list of entries… 340 000");
    two viewers of the same archive share one build;
  - reads are sequential and buffered (1 MB); rar/tar seeks are cheap on a disk but slow on a network share: the
    progress says so rather than holding a request open; a build is cancelled when the archive changes, and a
    build nobody asked about for a minute is cancelled;
  - [x] catalogs are capped in total (`Files:Archives:CatalogMegabytes`, 1 024), evicted least recently used (a deleted
    archive's catalog ages out), never part of backups; a moved archive simply builds again (no
    [path-moves](../features/path-moves.md) entry needed);
  - compressed tars (`.tar.gz`…) have no directory: listing means decompressing everything. Explored only under
    `Files:Archives:MaxCompressedTarMegabytes` (2 048) as a background build like the others; above, extract only
    (exploring not offered).
  - per-caller limits (`RequestLimits`) on folder pages, entry opens and builds started.
- **Client**: the view never holds more than the pages near the screen: virtual rows (`VirtualRows`, from the CSV
  viewer) over keyset pages of 200, loaded while scrolling; the folder's counts come from `Folders` so the scrollbar
  and "n items" are right before the pages arrive. Search across the whole archive (name contains) runs on the
  catalog, paged, cancellable, with a time cap per page.

## Passwords

Opening (phase 2, [archive-viewer](../features/archive-viewer.md)) and extracting (phase 3,
[archive-extraction](../features/archive-extraction.md)) are built; creating (phase 4) is left. What a password protects differs by format, and the view says which: a zip encrypts only the **content** (names,
sizes and dates stay readable by anyone with the file); a 7z or RAR made with "encrypt names" hides the list too.

- **Opening.** The catalog read, or an entry opened, answers `password-needed` (the archive's names are encrypted, or
  the entry is); the view asks for it once per archive, with **Show** and no "remember". `POST
  api/files/archive/unlock {path, password}` checks it on the archive (zip AES: the 2-byte verifier then the first
  entry's HMAC; ZipCrypto: its check byte then the CRC; 7z/RAR: SharpCompress opening the header or the first
  entry) and answers an **unlock id** (32 random bytes, hex) that later requests carry: the entry route has it in its
  path (an `<img>` sends no header), the pages route in a header.
- **Where the password lives**: only in the server's memory, in the unlock (tenant, account, session, archive path
  + `FileVersion.Token`), dropped after 15 idle minutes, at sign-out and when the archive changes. Never in a URL,
  the log store, an error, the catalog, a job's view or the change feed; never sent back to the client. Logged:
  "archive unlocked" / "wrong password" with the account and the archive, never the password.
- **Wrong passwords**: "That password doesn't open this archive." A per-caller limit (`RequestLimits`, e.g. 10 a
  minute) on `unlock`: 7z derives its key with 2^19 SHA-256 rounds (~0.3 s of CPU each), so guessing is also a way
  to load the server. Logged with `LogSecurityWarning` past the limit.
- **Encrypted names never reach the disk.** A catalog of an archive whose names are encrypted would write them in
  clear into the cache: such catalogs are built **in memory** (SQLite `:memory:`, same schema and queries), live as
  long as the unlock, and are capped (`Files:Archives:MaxEncryptedListEntries`, 500 000; past it: "Too many entries
  to explore with a password: extract it instead"). Archives with only their content encrypted keep a catalog on
  disk (the names are in clear in the file anyway). SharpCompress needs the password even to list a RAR 5 whose
  names are in clear (0.50.7, `ArchiveReaderTests`): treated as encrypted names.
- **Zip decryption is ours** (the zip directory reader keeps memory flat; SharpCompress' zip would hold every
  entry): ZipCrypto (read only, for old archives) and WinZip AES (method 99, extra `0x9901`, AE-1 and AE-2,
  128/192/256 bits): PBKDF2-HMAC-SHA1 1000 rounds (`Rfc2898DeriveBytes.Pbkdf2`), AES-CTR with a little-endian
  counter, HMAC-SHA1 of the encrypted data (10 bytes) checked at the entry's end (a mismatch is "broken or wrong
  password", and an entry opened for the viewer is cut off). 7z and RAR through SharpCompress' `Password` (both
  checked 2026-10-26 on the samples: the right password opens, a wrong one fails cleanly).
- **Extracting**: the job takes the unlock id (or a password in the request body when started from the menu without
  exploring) and holds the password only while it runs; a wrong one fails the job before writing anything.
- **Creating** (zip): the dialog's **Password** and **Confirm** (Show toggles both), a strength hint, the line
  "File names stay visible: only the content is encrypted", and, on a page served over plain HTTP to another
  computer, "This connection isn't encrypted: the password crosses the network as is". AES-256, AE-2 (no CRC: a CRC
  leaks small files' content), a new 16-byte salt per entry (`RandomNumberGenerator`).
- Tests: our encrypted zips opened by SharpCompress always and by 7-Zip (`7z t -p…`) when present
  (`SFE_TEST_SEVENZIP`, else `C:/Program Files/7-Zip`); AES zips made by 7-Zip opened by us (committed samples:
  `make-archives.mjs`); wrong password, cut-off entry (HMAC), unlock of another account or space (404), expiry,
  sign-out, a changed archive, the limit, logs holding no password (`WarningLog`), no encrypted name in any file
  under the cache.

## Compression level

The person chooses how hard to compress, for each archive, in the Compress dialog:

| Choice | zlib level | For |
|---|---|---|
| Store | 0 | already compressed files; fastest, biggest |
| Fastest | 1 | big folders, quick |
| Normal (default) | 6 | the usual balance |
| Maximum | 9 | smallest, slowest (several times Normal's CPU) |

- SFE's own zip writer deflates with `DeflateStream(…, new ZLibCompressionOptions { CompressionLevel = n })`
  (.NET 9+): any level 0–9, not only `CompressionLevel`'s four. The dialog shows the four choices; the API takes
  0–9.
- **Skip what is already compressed** (checkbox, on by default): JPEG, PNG, MP4, MKV, MP3, zip, 7z… are stored
  whatever the level (as downloads do): compressing them costs CPU for ~0 %. Off, everything is deflated at the
  level.
- The job's view shows bytes read and written as it goes, and its result the **ratio achieved** ("2.4 GB → 1.1 GB,
  46 %"); the dialog, before starting, the total size from the index.
- CPU: compress jobs run one at a time per server (`Files:Archives:CompressJobs`, 1), queued beyond; a job at
  Maximum on a big folder shows its estimated time after the first minute.
- Tests: each level writes a valid zip read back by SharpCompress, `ZipFile` and 7-Zip; levels order the sizes
  (Store ≥ Fastest ≥ Normal ≥ Maximum on a text tree); skipped types are stored; the ratio in the result.

## Phase 1 — Core: reading archives safely (done 2026-10-26)

- [x] `Core/Files/Compression/`: formats by bytes, entry names, zip directory reader, SharpCompress for the others,
  bounded entry streams, the catalog; `Files:Archives:*` options (`MaxEntries`, `MaxDepth`, `MaxCompressionRatio`,
  `MaxCompressedTarMegabytes`): [archive-reading](../features/archive-reading.md).
- [x] Tests `ArchiveReaderTests`, `ArchiveCatalogTests` (39): every format but rar, hostile names and directories,
  a lying size, Zip64 (70 000 entries), 200 000 entries paged, 5 000 nested folders, cut tars and tar.gz, passwords;
  7z samples in `tests/samples/archives/` (`make-archives.mjs`, `update-samples.mjs --sevenzip`).
- [x] RAR samples: six files of SharpCompress' tests (MIT, pinned commit) in
  `tests/samples/third-party/sharpcompress/` (RAR 4, RAR 5, solid, encrypted; password `test`),
  `TestSamples.ThirdPartyOf`; RAR tests added (44 in all).
- [x] `MaxCompressionRatio`: the entry route (phase 2) refuses, extraction (phase 3) leaves out an entry whose
  `Size / Packed` passes it.

## Phase 2 — Explore: the archive viewer

Done 2026-10-26 ([archive-viewer](../features/archive-viewer.md); user docs `docs/user-guide/archives.md`):

- [x] `GET api/files/explore` (a folder of the archive in keyset pages, 202 while the list is read, 409
  `not-explorable`) and `GET api/files/explore/entry/{archive}/{id}/{name}` (sandboxed, inline only when safe, the
  preview limit, the ratio check, ETag/304). Not `api/files/archive/…`: the download feature's `archive/{link}`
  takes that path.
- [x] `ArchiveCatalogs`: one build per archive however many ask, the server-wide gate (`CatalogBuilds`), builds
  given up when nobody asks, failures kept a minute, eviction past `CatalogMegabytes`, old versions deleted.
- [x] Client: `ARCHIVE_FORMAT` / `isExplorable` from `config.archives`, the view (trail collapsed, Up, Go to, virtual
  rows over pages, reading progress, notes for stopped / left out / password, files opened in a tab or downloaded,
  a context menu per row), phones (name and size).
- [x] Tests: `ExploreRouteTests` (6), `ArchiveCatalogsTests` (3), `archive-view.component.spec.ts` (9), format rows;
  real app `agents/tools/visual-check/archive-viewer.mjs`; docs scene `archive` (sample library:
  `Documents/Mountain trip.zip`).

Also done:

- [x] Passwords (2026-10-26; [archive-viewer](../features/archive-viewer.md), "Passwords"): `unlock` and its limit,
  `ArchiveUnlocks` (memory only, owner + stamp, 15 idle minutes, sign-out), catalogs in memory for encrypted lists,
  our zip decryption (ZipCrypto, AES; checked against 7-Zip's zips), 7z/RAR through SharpCompress, the view's form.
  Tests: `ZipsMadeWithAPasswordOpenWithIt`, `ExploreRouteTests` (4 more), `ArchiveUnlocksTests`, 3 specs, the real-app
  check.

## Phase 3 — Extract

Done 2026-10-26 ([archive-extraction](../features/archive-extraction.md); user docs
`docs/user-guide/archives.md#extracting`):

- [x] `FileJobKind.Extract` in `FileJobs` (progress in bytes of the archive read, cancel, clash questions with
  `forAll`, the panel, `resume()`); `POST api/files/jobs/extract` takes the listing's ids (folders, files; empty: all)
  and turns them into paths inside the archive (never a list of a million entries: a chosen folder is a prefix).
  Entries read in archive order (`ArchiveReader.ReadThrough`): a solid block unpacked once.
- [x] Where: **Extract to “name”** (a new folder beside it), **Extract…** (the picker, **Into a new folder** or
  straight in with the clash questions); one top folder doesn't nest twice.
- [x] Rights per run: `files.view` + `files.download` at the archive, `files.extract` (new, `since` 13, no group) at
  the target; replacing: the delete right there (the replaced item to the trash).
- [x] Writing into `name.sfe-partial`, then placed; leftovers removed at the next extraction there; free space before
  the first file and every 64 MB; times from the entries; links never made; every path checked inside the folder;
  the scan gate.
- [x] Passwords: the unlock id or a password in the request, held by the running job only; a wrong one fails the job
  and leaves nothing.
- [x] After: files indexed, folders by a scan, default tags, the listing told, a log line; left-out entries in the
  result.
- [x] Client: the item menu, the Extract dialog, the viewer's **Extract…** (folder shown, a row).
- [x] Tests: `ArchiveExtractorTests` (each format and sample, passwords, chosen entries, zip-slip, links, a lying
  size, a bad CRC, the ratio, duplicates, too many entries, the disk, cancel, long paths, a cut tar),
  `ExtractJobTests` (new folder, one top folder, clash, chosen, passwords never shown, rights, not an archive, left
  out, leftovers, free space), `ExploreRouteTests` (2), `extract.spec.ts` (7), the archive view spec.

- [x] The real-app check (`archive-viewer.mjs`: the menu, the dialog with a password, the panel, the folder listed).

## Phase 4 — Compress

- [ ] `FileJobKind.Compress`: `paths` (≤ 10 000, as downloads), `target` folder, `name`, `level` (0–9),
  `skipCompressed`, `password` (optional; held only while the job runs). Name: the item's or the common parent's,
  keep-both on clash; the dialog shows it editable.
- [ ] The walk of `Archives.Plan`/`WriteAsync` reused (move it to a shared walker, don't copy it): links never
  followed, `IsProtectedReal`, folders hidden by rules left out silently, refused/unreadable ones listed,
  `SFE - left out.txt` at the end; the new archive itself is never put into itself (target inside a chosen folder).
- [ ] `Core/Files/Compression/ZipWriter`: SFE's own zip writer (neither .NET nor SharpCompress encrypts): writes the
  partial file (seekable: each local header's sizes and CRC written back after its data, no data descriptors),
  Zip64 past 4 GB or 65 535 entries, UTF-8 names (flag 11), modified times (DOS + extended timestamp), deflate at
  levels 0–9 ("Compression level"), WinZip AES-256 AE-2 per entry with a password ("Passwords"). Checked against
  `ZipDirectory` (ours), SharpCompress, `ZipFile` and 7-Zip.
- [ ] Rights per item: `files.view` (+ `files.download`: compressing is a way to take the content) at each item,
  `files.compress` at the target folder.
- [ ] Free space (decided 2026-10-26: the only limit; `Files:MaxArchiveMegabytes` stays the downloads'): refused
  when the drive would keep less than `Files:MinimumFreeSpaceMegabytes`, from the index's sizes (an upper bound),
  checked again while writing.
- [ ] After: the new file indexed, default tags, change feed, log line, toast with **Open**.
- [ ] Client: **Compress…** in the selection bar and the item/folder menus: dialog (name; compression level: Store /
  Fastest / Normal / Maximum, with "Skip files already compressed"; password and confirm, with what it protects;
  where: here by default, or the picker), progress in the operations panel, the ratio achieved in its result.
- [ ] Tests: `ZipWriterTests` (levels, Zip64, names, AES: see "Passwords" and "Compression level"),
  `CompressJobTests` (a folder with sub-folders, a rule-hidden folder left out, a link not followed, the archive read
  back by `ArchiveReader` and by `ZipFile`, a password never in the job's view nor the log, cancel removes the
  partial),
  route rows, client specs.

Carried over (open points of the earlier steps, moved here 2026-10-26):

- [ ] Zip methods other than stored and deflate (deflate64, bzip2, lzma, zstd): SharpCompress' decompressors over
  `WindowStream`, if real archives need them.
- [ ] Search across the archive (`GET …/explore/search?path=&q=&after=`: names containing, on the catalog, paged,
  a time cap per page) and a name filter inside a folder (a prefix range on the folder's index).
- [ ] Open a picture / text / PDF entry in the viewer itself (today: a new tab): a `LibraryItem`-like entry whose
  file URL is the entry route; videos need Range on a decompressed stream (later).
- [ ] Item menu: "Open as archive" for `.cbz`/`.epub`/`.docx`… ([context-menu](../features/context-menu.md)); the
  viewer's Extract… with the selected entries (phase 3).
- [ ] Per-caller limits on folder pages and builds started (entry opens have `RequestLimits.Media`).
- [ ] Selection bar with one archive selected; several archives in one job.
- [ ] Asking the password again from the panel when an extraction fails on it (today: its message, then Extract…).
- [ ] Paths too long for Windows without long paths (.NET handles `\\?\` itself: only the index's limit applies now).
- [ ] Screenshots for the user docs: the viewer's password form (the docs library needs an encrypted archive: SFE
  writes them in this phase) and the Extract dialog (docs scenes).
- [ ] Desktop: drop an archive with Alt? (later).
- [ ] **Measured, not assumed**: a tool in `agents/tools/archives/` writes big archives into a temp folder (5 M
  entries, 10 000 nested folders, 100 000 files in one folder; zip, tar, and 7z/rar to check SharpCompress' memory
  with millions: its 7z archive holds every entry) and reports build time, peak memory, catalog size and page
  latency into the feature note. 1 M zip entries done by hand (see above).

## Phase 5 — Ship

- [~] Samples: `Documents/Mountain trip.zip` in the docs library (done, phase 2); later scenes for extract and compress.
- [ ] Security ([security](security.md)): new attack vectors (zip-slip variants, links in tar/7z, bombs by ratio and
  by entry count, an `.html`/`.svg` entry served inline, an entry route token with `..`, a hidden folder reached by
  compressing its parent, extracting into `.sfe` or a server-files junction, solid-archive DoS on the entry route;
  passwords: guessing through `unlock` (the limit, 7z's costly key), another account's or space's unlock id, an
  unlock id after sign-out or a change of the archive, the password in logs, errors, job views, URLs; encrypted names
  written in clear under the cache).
- [ ] User docs `docs/user-guide/managing-files.md` (compress, extract) and a page or section for the archive
  viewer, with screenshots ([user-docs](user-docs.md), [docs-screenshots](../environment/docs-screenshots.md));
  rights in the rights docs; configuration keys documented.
- [ ] Notes: `features/compression.md`, `features/archive-viewer.md`, lines in the `AGENTS.md` index; this file
  deleted.
- [ ] Changelog under `## Unreleased`; minor version at the merge into `main`.

## Production

- No change to the app's databases: jobs live in memory (as move/copy); archive catalogs are cache files in a new
  `archives/` folder of the library's cache (disposable: deleting it costs only a new read), capped in size.
  Upgrades need nothing; a catalog format change bumps the `v1` folder and the old one is deleted at start
  ([thumbnails](../features/thumbnails.md): a cache layout change needs its upgrade path).
- New rights with `since`: nobody gains them by upgrade; administrators grant them.
- New settings default to safe values; nothing to migrate. No external program, nothing to install.
- SharpCompress adds ~2.5 MB to the packages.

## Later, not in this epic

- Archives as folders of the listing (paths through archives, thumbnails and search of entries, the index).
- Creating tar/tar.gz (creating 7z: dropped, see above), split archives (`.7z.001`, `.part1.rar`, `.z01`), adding to or
  removing from an existing archive, videos played from inside an archive.
