# Folder cards (cover pictures)

Code: `src/Sfe.Core/Library/LibraryBrowser.cs` (`Covers`), route `POST api/browse/covers`; client `CoversService`,
`img[sfePreview]` (`PreviewImageDirective`), `PreviewsService`, `sfe-folder-item`.

- Grid and single views, `.folders.covers`; off when `Browse:FolderPictures` = 0, sent as `/api/config`
  `folderPictures`.
- An `IntersectionObserver` in `CoversService` batches the cards near view into `POST api/browse/covers` (≤ 100
  paths).
- `LibraryBrowser.Covers` keeps the first N images/videos by name (top-k over at most 5000 entries, videos only if
  `ThumbnailService.IsAvailable`), cached per folder by its `LastWriteTimeUtc` and N.
- The cells reuse `img[sfePreview]` (`PreviewImageDirective`: tries `PreviewsService.sources(item)` in turn), laid out
  ⌈√N⌉ columns wide (`--cover-columns`/`--cover-rows`).
- `CoversService` resets on each new listing (`ListingStore.generation`).
