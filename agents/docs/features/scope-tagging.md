# Folder / search tagging (scope tags)

Code: `src/Sfe.Core/Tagging/ScopeTagger.cs`, route `api/file-tags/scope` in `src/Sfe.Api/Endpoints/`; client
`ScopeTagsDialog` (in `src/app/tags/`), location header's tag button, `.folder-tags-button` in `sfe-folder-item`.

- `ScopeTagsDialog` (location header's tag button, `.folder-tags-button` in `sfe-folder-item`'s `.folder-actions`)
  never lists the files: `GET|POST api/file-tags/scope` (`path`, `recursive`, `q`) → Core `ScopeTagger`, which counts
  tags with one GROUP BY and changes files by batches of 1000 keyed on `Files.Id` (a search whose matches depend on the
  changed tag still reaches every file).
- A folder scope runs `DirectoryIndexer.IndexAsync` on it first (409 while a scan runs); a search scope reuses
  `FileSearchService.Matches` (hidden-tag rule included).
- Every change is confirmed in the dialog's `.scope-confirm` box; a new tag is created only on confirming.
- A folder's dialog also holds its default tags: [default-tags.md](default-tags.md).
- The listing is patched with `ListingStore.applyScopeTags` (by path for folders; for a search, every listed file when
  the route is that search), not reloaded.
