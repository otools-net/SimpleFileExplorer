# Scale

Millions of files, folders of 100k+. No per-request work proportional to a folder or the index:

- browse reuses a sorted snapshot (`LibraryBrowser`);
- search/duplicates page by key (`after` cursor, no `OFFSET`; [search-sort.md](../features/search-sort.md));
- indexer batches of 1000 + `ChangeTracker.Clear()` (`DirectoryIndexer`);
- tag counts cached briefly;
- unwanted thumbnail jobs dropped before ffmpeg (`ThumbnailService`).
