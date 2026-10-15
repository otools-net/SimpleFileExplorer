# Thumbnails and previews

Code: `src/Sfe.Core/Thumbnails/` (`ThumbnailService` (disk cache, concurrency gate), `Ffmpeg` (locate + run)),
`Core/Upgrades/Steps/V1_40_0/ShardCacheLayout`; client `PreviewsService`, `PreviewImageDirective`,
`ConfigService.isLargeImage`, `FileTileComponent`.

- Cache layout: `{k[0..2]}/{k[2..4]}/{k[4..]}.bin`, `k` = `ThumbnailService.CacheKey` = lower hex SHA1 of
  `{SHA1(folder)}/{SHA1(file)}` (the pre-1.40 folder/file names, so the update renames without the sources); empty
  file = no thumbnail. Trickplay uses the same key ([trickplay.md](trickplay.md)).
- Unwanted thumbnail jobs are dropped before ffmpeg ([scale.md](../development/scale.md)).
- Images/thumbnails are sent `no-store`; trickplay sheets are cacheable (URL carries its `version`).
- `ConfigService.isLargeImage` (size > `config.largeImageSize`, thumbnails on) picks thumbnail-only vs original in
  listings and the viewer.
- `Thumbnails:SingleViewOriginals` (`/api/config` `singleViewOriginals`) makes `FileTileComponent` ask
  `PreviewsService.sources(item, true)` in the `single` view.
- Cancelling thumbnails: `ThumbnailService.CancelAll` ([background-tasks.md](background-tasks.md)).
- A cache layout change needs an upgrade path ([production-upgrades.md](../development/production-upgrades.md)).
