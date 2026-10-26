# Reading archives (formats, entry names, catalogs)

Code: `src/Sfe.Core/Files/Compression/` (`ArchiveFormats`, `ArchiveEntryNames`, `ZipDirectory`, `ArchiveReader`,
`ArchiveCatalog`, `BoundedStreams`), `ArchiveOptions` (`Files:Archives:*`) in `SfeOptions.cs`. Tests:
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

`.cbz`/`.cbr`, `.epub`, `.docx` are zips too but keep their own viewer (an "Open as archive" is in
[archives-next-plan](../development/archives-next-plan.md)).

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
  `Locator` = the local header's offset: opening an entry is one seek. Stored and deflate only (others answer that
  the method isn't read).
- **7z**: SharpCompress' archive (the header is at the end and read whole); `Locator` = the entry's index.
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
- Schema (`Version` 2): `Folders(Id, ParentId, Name NOCASE, Depth, Time, Folders, Files, AllFiles, Size REAL,
  Packed REAL)`, root `Id` 1; `Entries(Id, FolderId, Name NOCASE, Kind, Size, Packed, Time, Encrypted, Locator,
  Method, Crc, Cipher, CipherInfo)`; `LeftOut` (first 1000); `Info` (version, format, entries, folders, leftOut,
  complete, stopped, needsPassword). A version change moves catalogs to a new `v*` folder; the old one is deleted.
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

## Measured (2026-10-26, this machine, Release)

- 1 000 000 zip entries in 1 940 folders (147 MB zip): catalog built in 11 s, 77 MB; process memory 7 MB before,
  49 MB at the peak; a page of 200 in ~3 ms, the root's in ~2 ms.

## Gotchas

- The test helper is `TestArchives` (`ArchiveReaderTests.cs`): `Archives` is the download feature's class.
- `UPDATE … FROM (subquery)` in SQLite: give the subquery's columns other names than the table's (`Size = Size + c.Size`
  is "ambiguous column name").
- `ArchiveCatalog` has no method named `File`: inside the class it would hide `System.IO.File`.
