# Not indexed marks

Code: `TagService.AttachIndexDataAsync`, `LibraryItem.Indexed`; client `styles/items.css` (`.unindexed`),
`ListingStore.applyFileTags`/`applyScopeTags`.

- Browse only: `AttachIndexDataAsync` sets `LibraryItem.Indexed` (files and folders, null when the index can't be read
  and in search results).
- The client fades `indexed === false` (`.unindexed`, `styles/items.css`) and `ListingStore.applyFileTags`/
  `applyScopeTags` clear it, since tagging indexes the files.
