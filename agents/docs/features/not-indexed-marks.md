# Not indexed marks

Code: `TagService.AttachIndexDataAsync`, `LibraryItem.Indexed`; client `styles/items.css` (`.unindexed`),
`ListingStore.applyFileTags`/`applyScopeTags`.

- Browse only: `AttachIndexDataAsync` sets `LibraryItem.Indexed` (files and folders, null when the index can't be read
  and in search results).
- A connection's listing read from the provider marks its items the same way from the library's index of it
  (`RemoteIndexListings.IndexedAsync`); one read from the index is all indexed
  ([connection-files](connection-files.md)).
- The client fades `indexed === false` (`.unindexed`, `styles/items.css`) and `ListingStore.applyFileTags`/
  `applyScopeTags` clear it, since tagging indexes the files.
