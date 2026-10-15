# Resolution (Width / Height, HR/LR marks)

Code: `src/Sfe.Core/Indexing/MediaDimensions.cs`, `DirectoryIndexer`, `Search/` (`DimensionFilter`,
`ResolutionLevel`, `SearchPredicateBuilder`, `FileSearchService`); client `ConfigService.resolution(item)`,
`resolutionMark(item)`, `FileTileComponent`.

- `Files.Width`/`Height` (null = not read yet, 0 = read but not understood, so not read again until the file changes).
- The indexer reads them (`Scan:ReadResolution`) for new/changed files and for unchanged ones still null, like missing
  fingerprints; videos only while ffmpeg is available (`MediaDimensions.CanRead`).
- Images: headers of JPEG (with EXIF orientation 5–8 swapping the sides), PNG, GIF, BMP, WebP, else `ffmpeg -i`;
  videos: `ffmpeg -i` stderr, first non-`attached pic` video stream, rotation ±90 swaps.
- A changed file is attached with its stored Width/Height so that clearing them is written.
- Listings: `LibraryItem.Width/Height` only when > 0, browse fills them in `TagService.AttachIndexDataAsync` (with the
  tags), search from its row.
- Search: `w:`/`h:`/`r:WxH` → `DimensionFilter`s (no operator = equal), predicates require `> 0`.
- Display: `Browse:ShowResolution` → `/api/config` `showResolution`, `ConfigService.resolution(item)`.
- HR/LR marks: `Browse:ResolutionMark` + `Image|VideoHighResolutionPixels` → `/api/config` `resolutionMark`
  (`{ image, video }`, null = off), `ConfigService.resolutionMark(item)` compares the shorter side with its kind's;
  tiles put it in `.preview-marks` with the media `.badge` (bottom right), rows in the thumbnail; the trickplay hover
  canvas is inserted under them.
- `r:hr`/`r:lr` (`ResolutionLevel`, `SearchGroup.Levels`) use the same thresholds whether the marks are on or not:
  `FileSearchService` builds a `HighResolution` (thresholds + `MediaClassifier.Extensions(kind)`) for
  `SearchPredicateBuilder.Build`; HR = both sides ≥ the kind's threshold (= shorter side), LR = known and below.
- Sorting by resolution: [search-sort.md](search-sort.md) (`Files.Pixels`).
