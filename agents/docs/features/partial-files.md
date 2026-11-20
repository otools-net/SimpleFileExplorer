# Partial files (`.sfe-partial`)

Code: `LibraryPaths.PartialSuffix` / `IsPartial` (`FileJobs.PartialSuffix` is the same), `DirectoryIndexer.IsIgnoredFile`
/ `IsIgnoredDirectory`; client `files/partial-files.ts` (`isPartial`, `partialEntries`), `PartialMarkComponent`
(`library/items/item-parts.ts`), `styles/items.css` (`.partial`, `.partial-mark`). Tests:
`IndexAndSearchTests.PartialFilesAndFoldersAreIndexedOnlyOnceRenamed`, `library/items/partial-items.spec.ts`.

- Who writes them, in the destination folder, renamed when complete: transfers into the library (a file,
  [transfers](transfers.md)), compression (a file, [compression](compression.md)), extraction (a folder,
  [archive-extraction](archive-extraction.md)). **Copies and moves inside the library don't**: they write the final
  name (`FileJobs.CopyFileAsync`, `CreateNew`, deleted on failure).
- Leftovers (server stopped during a job) are removed by `FileJobs.RemoveLeftovers` at the next extraction or
  compression in that folder, unless a running job writes into one (`board.PartialsOfOthers`).
- **Scans leave them out** (case-insensitive ending, files and folders), so search, duplicates, missing-file matching
  and Everywhere never see them; one indexed by an older version goes at the next scan of its folder (not seen on
  disk), no migration step. Once renamed, the job indexes the final name as before.
- **Listings** (the server lists them as ordinary items, no API field): the client knows them by name (`isPartial`),
  fades them with a "being written" chip and a hint, gives the file links no `href` (`[attr.href]` null), no
  preview, and a click opens nothing (`ItemActions.openFile`/`view`; a folder's click is stopped in its capture
  handler). The item menu keeps only Delete, Copy full path and Properties (`partialEntries` at the end of
  `ItemMenu.entries`).
- Not covered yet: the selection bar still offers its actions on a selection holding a partial item (they act on it
  like on any item; rename or move would make the job fail when it renames). The menu registry of release 2.0 step
  2.9 is the place to share the rule.
