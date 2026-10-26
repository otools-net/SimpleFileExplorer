# Compression (zips made in the library)

Code: `Core/Files/Compression/ZipWriter.cs` (the writer), `Core/Files/Archives.cs` (`CompressAsync`, the walk shared with
downloads: `IZipTarget`, `DownloadTarget`, `CompressTarget`), `FileJobs.CompressAsync` (the job), `ArchiveBuildGate.Compressions`
(one at a time per server), `FileEndpoints.Compress` (`POST api/files/jobs/compress`), `Api/Security/ArchiveAccess.cs`;
client `files/compress.dialog.ts`, `FileTransfers.compress`/`canCompress`, `ItemMenu` (`compress`), the selection bar.
Tests: `ZipWriterTests`, `CompressJobTests`, `FileJobRouteTests` (one), `compress.spec.ts`; downloads' walk:
`ArchivesTests`. User docs: `docs/user-guide/managing-files.md#compress-into-a-zip`. The plan:
[archives-next-plan](../development/archives-next-plan.md).

## Decisions (2026-10-26)

- **Zip only.** Making 7z was dropped: a 7-Zip program would mean a setting, an install step and a process per job;
  7z is still explored and extracted. SharpCompress has had a managed 7z writer since 0.50.x: if 7z is ever wanted,
  it needs no program. tar / tar.gz: later, if asked.
- **AES-256 only** for passwords (AE-2): secure, opened by 7-Zip, WinRAR, macOS `ditto` and Windows 11's newer zip
  support, not by older Windows Explorer nor macOS Archive Utility (the dialog says so). ZipCrypto is read, never
  written. AE-2 keeps no CRC (a CRC leaks a small file's content).
- The level is the person's, per zip: the dialog's four choices (0, 1, 6, 9), the API takes 0–9.

## The writer (`ZipWriter`)

- Ours because neither `ZipArchive` nor SharpCompress 0.50.7 encrypts, and `CompressionLevel` has four values:
  `DeflateStream(…, new ZLibCompressionOptions { CompressionLevel = 0–9 })`; 0 = stored (method 0).
- Seekable output: local header written with zero sizes, the data, then CRC and sizes written back (no data
  descriptors). Zip64 local extra only when the input may reach 4 GB (`Zip64Threshold`, or a stream of unknown
  length); central Zip64 fields only for what overflows; Zip64 end records past 65 535 entries or 4 GB.
- UTF-8 names (flag 11), DOS time (local) + extended timestamp `0x5455` (UTC), made-by MS-DOS (0x20 / 0x10 attributes).
- With a password: WinZip AES-256 AE-2 (`0x9901`, method 99, version 51): 16-byte salt per entry
  (`RandomNumberGenerator`), PBKDF2-HMAC-SHA1 1000 rounds (key, HMAC key, 2-byte verifier), AES-CTR with a
  little-endian counter from 1, HMAC-SHA1 of the encrypted data (10 bytes) after it, CRC 0 (AE-2: a CRC leaks small
  files). Password bytes UTF-8.
- An empty deflated file gets the final empty block `03 00` (.NET's `DeflateStream` writes nothing; 7-Zip calls that
  a data error).
- The central directory goes to a `DeleteOnClose` temp file until `Finish`: memory flat at millions of entries.
- Checked (`ZipWriterTests`): .NET's `ZipFile`, our `ArchiveReader`, SharpCompress, and 7-Zip's `7z t` when installed
  (`SFE_TEST_SEVENZIP` or `C:/Program Files/7-Zip/7z.exe`). **7-Zip 26.01 refuses non-ASCII zip passwords** ("The
  parameter is incorrect" when making, "Wrong password" when testing ours): the dialog says so.

## The job (`FileJobKind.Compress`)

- `CompressRequest {paths ≤ 10 000, target, name?, level 0–9 (6), skipCompressed (true), password?, conflicts}`;
  `api/files/jobs` refuses the kind. The password lives on the `Job` while it runs only.
- Rights: `files.compress` at the target; the items and every folder walked through `IFileJobAccess.ArchiveAccessAsync()`
  (an `ArchiveAccess` of the person's access then: `files.view` + `files.download`; hidden folders left out silently,
  refused ones and links named in `SFE - left out.txt`). One result item for the whole job (`Total` 1).
- `Archives.Plan` (as downloads: nested items dropped, ` (2)` for same names); name: the request's, else the plan's
  `FileName`; `.zip` added; `FileNames.Check`.
- Free space: refused when the index's sizes (`IndexedSizeAsync`, an upper bound) would pass the margin; read again
  every 64 MB written (`NoRoomException`).
- `ArchiveBuildGate.Compressions(CompressJobs)` (1): waited for before writing (the panel says "Waiting for its turn"
  while nothing is read).
- Written to `{name}.sfe-partial` (a file; leftovers removed at the next extraction or compression there; the scan gate
  holds it), never into itself (`CompressOptions.Exclude`: the partial's real path, skipped by the walk). Then the clash
  (`ClashAsync`, the partial as the source side), then `File.Move`, indexed at once, a log line (counts, level,
  "encrypted"; never the password). Cancel or failure: the partial file deleted.
- Skipped when `skipCompressed`: `Archives.AlreadyCompressed` (pictures, audio, video, archives, office zips, fonts…)
  stored at level 0.
- Result message: `N files: in → out (p %)`, ", encrypted", left out count.

## Client

- **Compress…** in the item menu (the selection when the item is in it) and the selection bar (`compressible`: the
  right, view + download on an item, the dialog there). `CompressDialog`: name (`defaultName`), level select (Store 0,
  Fastest 1, Normal 6, Maximum 9: `COMPRESSION_LEVELS`), skip, password twice with Show and notes (names visible,
  short, non-ASCII, plain HTTP, old Windows). The target is the folder listed (`compressTarget`; search: the first
  item's folder).
- Panel: icon `compress`, "Compressing into …", bytes compressed, then "“x.zip” made in …" with the message (ratio);
  the toast's **Show** reveals the zip.
- Not done: a folder picker for the target, the size before starting, an estimate of the time at Maximum.
