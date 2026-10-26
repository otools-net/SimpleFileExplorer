# Reading archives (formats, entry names, catalogs)

Code: `src/Sfe.Core/Files/Compression/` (`ArchiveFormats`, `ArchiveEntryNames`, `ZipDirectory`, `ArchiveReader`,
`ArchiveCatalog`, `BoundedStreams`), `ArchiveOptions` (`Files:Archives:*`) in `SfeOptions.cs`; measurements: `agents/tools/archives/`. Tests:
`ArchiveReaderTests`, `ArchiveCatalogTests`; samples `tests/samples/archives/` (7z, by
`agents/tools/samples/make-archives.mjs`) and `tests/samples/third-party/sharpcompress/` (RAR, from SharpCompress' tests). What is left: [archives-next-plan](../development/archives-next-plan.md).

The core under exploring and extracting archives: nothing here unpacks an archive, and nothing costs memory in
proportion to its entries.

## Formats

| Format | Explore / extract | Create | What listing reads (never the data) |
|---|---|---|---|
| `.zip` (Zip64, UTF-8 and code-page names) | yes | yes ([compression](compression.md)) | the central directory at the end (one sequential read) |
| `.7z` (solid or not, LZMA/LZMA2) | yes | no (dropped) | the header at the end (may itself be LZMA-packed: only it is unpacked) |
| `.rar` (4, 5) | yes, read only | no | one header per entry, spread over the file: seeks past each entry's data |
| `.tar` | yes | later, if asked | one header per entry: seeks past each entry's data |
| `.tar.gz`/`.tgz`, `.tar.bz2`, `.tar.xz` | extract; explore only up to `MaxCompressedTarMegabytes` | later, if asked | **the whole stream decompressed**: no index exists |
| `.gz`, `.bz2`, `.xz` alone (one file) | extract only | no | nothing (one file) |

`.cbz`/`.cbr`, `.epub`, `.docx` are zips too but keep their own viewer: "Open as archive" explores them
([archive-viewer](archive-viewer.md)).

- `ArchiveFormats.Detect(path)`: the first 512 bytes decide the family (zip `PK\3\4` or `PK\5\6`, 7z, `Rar!`, gzip,
  bzip2, xz, `ustar` at 257); the name only tells a compressed tar (`.tar.gz`, `.tgz`…) from one file compressed
  alone (`.gz` = `Single`); an old tar without a mark is trusted by name when its length is whole 512-byte blocks.
  Null = not an archive SFE reads, whatever the name.
- `IsExplorable(format, size, options)`: never `Single`; compressed tars only up to
  `Files:Archives:MaxCompressedTarMegabytes` (2048: listing one decompresses all of it). The client applies the
  same rule (phase 2).

## Reading

- `ArchiveReader.ReadEntries(path, format)` streams `ArchiveEntry`s (raw name, kind, sizes, time, encrypted,
  `Locator`, zip `Method`); `OpenEntry(path, format, entry)` gives one entry's data. Errors of the archive are
  `ArchiveException` (a message for the person, no path); SharpCompress' own errors are wrapped (a password one says
  "protected by a password").
- **Zip: our own central-directory reader** (`ZipDirectory`), never `ZipArchive` (one object per entry: GBs for
  millions). End record in the last 64 KB, Zip64 locator/record, prepended data (SFX) shifts offsets, split zips
  refused, a count claiming more records than the directory holds refused, records read to the directory's end (the
  16-bit count is wrong past 65 535 without Zip64). Names: UTF-8 when flagged or valid UTF-8, else code page 437.
  Extended timestamp (`0x5455`) over DOS time. Unix mode in the external attributes gives links and devices.
  `Locator` = the local header's offset: opening an entry is one seek. Stored and deflate by .NET; Deflate64 (9,
  what Windows writes for files over 2 GB), BZip2 (12), LZMA (14: a 4-byte header, then its properties), Zstandard
  (93), XZ (95) and PPMd (98: 2 bytes of order and memory) by SharpCompress' decoders over the entry's window
  (`ZipDirectory.OtherMethod`); any other method answers that it isn't read. Samples `tree-<method>.zip` by 7-Zip
  (it writes no Zstandard zip: `AZstandardZipEntryIsRead` makes one).
- **7z**: SharpCompress' archive (the header is at the end and read whole); `Locator` = the entry's index. Its list is
  held whole (~570 bytes an entry): `SevenZipList.UnpackedSize` reads the list's size first (the start header; for a
  compressed list, the unpack size of its streams, a few bytes, coders parsed but nothing unpacked) and every opening
  (list, entry, extraction) refuses past `Files:Archives:MaxSevenZipListMegabytes` (32: ~400 000 entries); headers it
  can't follow give null and SharpCompress decides.
- **rar, tar, compressed tars**: SharpCompress' forward reader; opening an entry walks to its index.
- **Passwords**: `ReadEntries(…, password)` (7z, rar lists encrypted) and `OpenEntry(…, password)`; without one,
  `ArchiveException.NeedsPassword`. 7z and rar through SharpCompress' `ReaderOptions.Password` (a wrong one throws a
  `CryptographicException`: wrapped). SharpCompress 0.50.7 needs the password even to list a RAR 5 whose names are in
  clear: such an archive is treated as one whose list is encrypted.
- **Zip encryption is ours** (`ZipEncryption`): ZipCrypto (keys from the password, the 12-byte header's last byte
  against the CRC's high byte, or the time's with flag 3; the CRC checked at the end by `CrcCheckStream`: the check
  byte passes a wrong password 1 time in 256) and WinZip AES (extra `0x9901`: AE-1/AE-2, strength 1–3, the real
  method; salt 8/12/16 bytes, PBKDF2-HMAC-SHA1 1000 rounds into the AES key, the HMAC key and a 2-byte verifier;
  AES-CTR, a little-endian counter from 1; HMAC-SHA1 of the encrypted data, its first 10 bytes after the data,
  checked at the end; AE-1 also its CRC). Password bytes: UTF-8. Checked against 7-Zip's AES-128, AES-256 and
  ZipCrypto zips (`tests/samples/archives/tree-*.zip`).
- **Truncated tars**: a gzip cut short just ends, and SharpCompress' tar reader stops quietly (checked 2026-10-26).
  Compressed tars are decompressed by us through `TailStream` (keeps the last 1 KB); a tar must end with its end
  mark (whole 512-byte blocks, the last 1 KB zero), plain tars checked on the file's end. No mark = "cut off".
- Every entry's data goes through `DeclaredSizeStream`: one byte more than declared, or the end before it, throws.
  That is what stops a zip bomb whose directory lies. Zip entries also may not run into the directory nor start
  outside the data (`WindowStream` bounds the decompressor's input).

## Entry names

- `ArchiveEntryNames.Split(name, maxDepth)`: `\` → `/`, empty and `.` segments dropped; refused (null + reason):
  `..`, a root (`/x`, `\\server`), a drive (`C:`), control characters, a segment of only dots and spaces (`...` is
  `..` on Windows), a segment over 1024 or a path over 32 K characters, deeper than `Files:Archives:MaxDepth`
  (10 000). Refused entries are left out and listed, never fixed into another place.
- Names valid in an archive but refused by the system (`CON.txt`, `a:b`) are listed: `FileNames` checks them when
  extracting.

## Catalogs

- `ArchiveCatalog.Build(archive, format, catalogPath, options, progress, cancel)` reads the list once into a SQLite
  file (written as `….building`, moved in at the end: a cut build leaves nothing). Not pooled connections, opened per
  question. `journal_mode = OFF`, `synchronous = OFF`, 16 MB page cache: it is a cache, made again if lost.
- Schema (`Version` 3): `Folders(Id, ParentId, Name NOCASE, Depth, Time, Folders, Files, AllFiles, Size REAL,
  Packed REAL)`, root `Id` 1; `Entries(Id, FolderId, Name NOCASE, Kind, Size, Packed, Time, Encrypted, Locator,
  Method, Crc, Cipher, CipherInfo)`; `LeftOut` (first 1000); `Info` (version, format, entries, folders, leftOut,
  complete, stopped, needsPassword). A version change moves catalogs to a new `v*` folder; the old one is deleted
  (catalogs are a cache: read again on demand).
- Indexes (made after the read): `FoldersByParent`, `FoldersByDepth`, `EntriesByFolder`, and `EncryptedFiles` (partial:
  `Encrypted = 1 AND Kind = 0`, on `(Size, Id)`): `SmallestEncrypted` is asked on every page (the `protected` flag),
  and without it scanned every entry (600 ms a page at 5 M, found by `agents/tools/archives/measure.mjs`; version 3).
- **In memory** (`BuildInMemory`): the list of an archive whose names are encrypted, read with its password into a
  shared-cache SQLite memory database (`Mode=Memory`, `Cache=Shared`) that lives while its keeper connection is open;
  the same queries. Its names never reach the disk.
- Folders are rows by id: no path is stored whole. While reading, the folders of the last entry's path are kept as a
  stack (archives list folder by folder) plus a `(parent, name)` cache of 200 000, then the exact-name index.
- After the read: indexes, direct counts from `Entries`, then totals rolled up **one depth level per statement**
  (deepest first): no recursion anywhere, a 5 000-deep tree is tested. Subtree sizes are REAL: sizes a hostile
  directory claims can't overflow a sum.
- Questions: `Children(folder, after, limit)` (folders, then files, by name in any case, keyset `(Name, Id)`: the
  same cost on any page), `Folder`, `FindFolder(path)` (as written, else any case), `Trail(id, last)` (the last few
  folders and how many are skipped: cost `last`, not the depth), `PathOf`, `Entry`, `LeftOut`, `Summary`,
  `IsCurrent(file)`.
- Where they live, built when, deleted when: `ArchiveCatalogs` ([archive-viewer](archive-viewer.md), "Catalog store").
- Stops: more than `Files:Archives:MaxEntries` (10 000 000), or an `ArchiveException` mid-way, keep what was read
  with `complete = 0` and the reason; never shown as the whole archive.

## Measured (2026-10-26, `agents/tools/archives/measure.mjs`)

i7-8550U, 16 GB, Windows; the docs build (Debug, Development) over HTTP, signed in; "before" is the app's own peak
after its start. Pages: the root's median of 10 (200 rows), a folder's first page of 1 000.

| Archive | Read | Peak memory (before) | Catalog | Root page | Folder page | Find in archive (a name at the end) | Other |
|---|---|---|---|---|---|---|---|
| zip, 5 000 000 files in folders of 1 000 (548 MB) | 35 s | 205 MB (143) | 387 MB | 10 ms | 18 ms | 2.3 s, 3 pages | Find in this folder 17 ms |
| zip, 100 000 files in one folder (10 MB) | 1.4 s | 209 MB (145) | 7 MB | 10 ms | 34 ms | 0.1 s | every page of 1 000: median 17 ms, the last 18 ms; Find in this folder 145 ms |
| zip, 9 999 folders nested (2 MB) | 0.6 s | 199 MB (147) | 1 MB | 11 ms | | 1.2 s (its `where`: 9 999 lookups up) | Go to 1 500 folders down 149 ms |
| tar, 1 000 000 empty files (488 MB) | 11.5 s | 201 MB (145) | 70 MB | 10 ms | 17 ms | 0.6 s | Find in this folder 19 ms |
| 7z, 1 000 000 empty files (38 MB, a header only) | 8.4 s | **717 MB** (145) | 62 MB | 10 ms | 17 ms | 0.9 s | Find in this folder 17 ms |

- Zip and tar reads stream: memory stays flat whatever the count. **A 7z's list is held whole by SharpCompress**:
  ~570 bytes per entry (1 M: +570 MB; `MaxEntries` 10 M would be GBs): hence `MaxSevenZipListMegabytes` (this 7z's
  38 MB list is now refused in 0.3 s, 149 MB peak).
- Pages cost the same on any archive (keyset ranges); before catalog version 3 every page scanned all entries for
  `SmallestEncrypted` (600 ms at 5 M).
- Go to by `at` is bounded by the address: Kestrel's 8 KB request line, each `/` sent as `%2F` (~2 000 levels).
- Earlier, by hand (Release, the catalog alone): 1 000 000 zip entries built in 11 s, 49 MB peak, ~3 ms a page.

## Gotchas

- The test helper is `TestArchives` (`ArchiveReaderTests.cs`): `Archives` is the download feature's class.
- `UPDATE … FROM (subquery)` in SQLite: give the subquery's columns other names than the table's (`Size = Size + c.Size`
  is "ambiguous column name").
- `ArchiveCatalog` has no method named `File`: inside the class it would hide `System.IO.File`.
