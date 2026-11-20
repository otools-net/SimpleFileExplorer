# Partial files (`.sfe-partial`)

Code: `LibraryPaths.PartialSuffix` / `IsPartial` (`FileJobs.PartialSuffix` is the same), `DirectoryIndexer.IsIgnoredFile`
/ `IsIgnoredDirectory`, `Files/Jobs/PartialItems.cs` (free name, placing, removing, leftovers), progress:
`Files/Writing/` (`ItemsBeingWritten`, `ItemWrite`, `ItemWriting`), `JobRun.Writing`, `ChangeFeedQueue.WritingEvent`;
client `files/partial-files.ts` (`isPartial`, `shownName`, `partialEntries`), `files/writing-progress.ts`
(`WritingProgress`), `ChangeFeed.writing`, `PartialMarkComponent` (`library/items/item-parts.ts`), `styles/items.css`
(`.partial`, `.partial-mark`, `.write-progress`). Tests: `IndexAndSearchTests.PartialFilesAndFoldersAreIndexedOnlyOnceRenamed`,
`ItemsBeingWrittenTests`, `FileJobsTests.ACopyIsWrittenAsAPartialItem…`, `ChangeFeedRouteTests.ItemsBeingWritten…`,
`library/items/partial-items.spec.ts`, `files/comfort.spec.ts` (ChangeFeed). Release 2.0 step 1.4 (2026-11-20): progress
on the item, as macOS Finder shows it.

- Who writes them, in the destination folder, renamed when complete: copies inside the library (a file or a whole
  folder, [move-and-copy](move-and-copy.md)), transfers into the library (a file, [transfers](transfers.md)),
  compression (a file, [compression](compression.md)), extraction (a folder,
  [archive-extraction](archive-extraction.md)). Moves don't (a rename; another drive: `FileTree.Move` copies itself).
- **Copies: a partial, then a rename** (decided 2026-11-20, rather than Finder's real name while copying): scans,
  search, duplicates and leftovers already handle the suffix, a copy cut off by a restart is visibly unfinished
  instead of looking whole, and one mechanism serves every writer. The client shows the name without the suffix, so
  people see the real name anyway. The partial is created first (`CreateNew` for a file: two jobs never share one),
  the scan turn holds the partial and the final name; at the end `PartialItems.Place` renames it, or gives it a
  keep-both name when an item of that name appeared meanwhile (the clash was settled before the copy started).
- Leftovers (server stopped during a job) are removed by `PartialItems.RemoveLeftovers` at the next copy (once per
  job, in its target), extraction or compression in that folder, unless a running job writes into one
  (`board.PartialsOfOthers`: transfers share the board).
- **Progress** (`ItemsBeingWritten`, one per library, memory only): a writer `Begin`s an `ItemWrite` (partial path,
  final name, total bytes) and counts bytes on it with `Interlocked` (nothing raised per write). Jobs set
  `JobRun.Writing` (`PartialItems.Writes`, which also invalidates the folder so listings show the item at once), so
  `AddBytes` / `SetBytes` feed it with no new code in the loops; transfers count in `LibraryTransferWrite.WriteAsync`.
  Totals: a copy's source size, a compression's sources, an extraction's archive (its position in it). A 300 ms timer
  runs only while something is written and raises `FolderProgressed` for folders whose items moved, started or ended,
  and for every folder each 3 s (a listing opened during a stall learns of it). Tests set `Manual` and call `Tick`.
- **Change feed** `writing` event (`{ path, items: [{ name, finalName, bytesDone, bytesTotal }] }`, read when sent so
  the latest): to every feed whose caller sees the folder, items filtered by `files.browse` at their own path; no
  origin filter (the copying tab wants it too). Several signals of one folder in one batch make one event.
- **Client**: `WritingProgress` keeps the items by path (a folder's event replaces that folder's); the mark shows a
  `<progress>` with "N % written (X of Y)" in its hint while the feed tells it (none for a leftover). `ChangeFeed.writing`
  refreshes the folder listed when an item of the event isn't listed yet or one ended (it took its name), so the
  copying tab, whose own `changed` events are ignored, shows it too. Names: `shownName` cuts the suffix in tiles, rows,
  folders and their hints.
- **Scans leave them out** (case-insensitive ending, files and folders), so search, duplicates, missing-file matching
  and Everywhere never see them; one indexed by an older version goes at the next scan of its folder (not seen on
  disk), no migration step. Once renamed, the job indexes the final name as before.
- **Listings** (the server lists them as ordinary items, no API field): the client knows them by name (`isPartial`),
  fades them with a "being written" chip and a hint, gives the file links no `href` (`[attr.href]` null), no
  preview, and a click opens nothing (`ItemActions.openFile`/`view`; a folder's click is stopped in its capture
  handler). The item menu keeps only Delete, Copy full path and Properties (`partialEntries` at the end of
  `ItemMenu.entries`).
- Not covered yet: uploads in progress (they write in the work folder, not the target: release 2.0 step 4.3), a
  folder transfer from a connection (each file shows, not the folder's total), a move to another drive.
- Not covered yet: the selection bar still offers its actions on a selection holding a partial item (they act on it
  like on any item; rename or move would make the job fail when it renames). The menu registry of release 2.0 step
  2.9 is the place to share the rule.
