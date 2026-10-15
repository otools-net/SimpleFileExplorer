# Duration (videos and audio)

Code: `src/Sfe.Core/Indexing/MediaDimensions.cs` (`ParseDuration`, `MediaProperties`), `DirectoryIndexer`
(`LacksMediaProperties`), `Search/` (`DurationFilter`); client `ConfigService.duration(item)`, `FileTileComponent`
(`durationBadge`).

- `Files.Duration` (seconds, `double?`; null = not read, 0 = ffmpeg gave none) is read with the resolution, from the
  same `ffmpeg -i` run (`MediaDimensions.ParseDuration`, the input's `Duration:` line), for videos and audio
  (`MediaProperties`; audio has `Size` null, so its Width/Height stay null and `w:`/`h:`/`r:` treat it as unknown, as
  before).
- Added in 1.33.0 (migration `VideoDuration`): `DirectoryIndexer.LacksMediaProperties` re-reads a video whose Width or
  Duration is null and an audio file whose Duration is null, so the first scan after the upgrade runs ffmpeg once over
  every indexed video and audio file.
- Listings: `LibraryItem.Duration` only when > 0 (`AttachIndexDataAsync` reads audio rows too).
- Display: `Browse:ShowDuration` → `/api/config` `showDuration`, `ConfigService.duration(item)`: tiles show it as a
  `.badge.duration` in `.preview-marks.start` (bottom left, before a video's trickplay mark, which moved into that
  group) and leave it out of `.meta` (`FileTileComponent.durationBadge`): videos with a preview, and audio always,
  over its placeholder (`.tile.kind-audio` overrides the rule hiding badges until an image loads); preview-less videos
  keep it in `.meta`. Rows put it in the size cell, the viewer in its details.
- Search: `d:` → `DurationFilter` (predicates require `> 0`); quirks in [search-terms.md](search-terms.md).
