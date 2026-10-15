# Trickplay (timeline sheets)

Code: `src/Sfe.Core/Trickplay/TrickplayService.cs`, `src/Sfe.Api/TrickplayWorker.cs`; client `src/app/trickplay/`.
Hover preview: [hover-preview.md](hover-preview.md). Fingerprints: [similar-videos.md](similar-videos.md).

- Core's `TrickplayService` run by the Api's `TrickplayWorker`.
- Per video `{CacheDirectory}/trickplay/{k[0..2]}/{k[2..4]}/{k[4..]}/` (same key as the thumbnail): `info.json`,
  `{n}.jpg`, `fingerprint.bin`.
- Fresh = size + mtime + `ImageKey`; failures recorded.
- `PassSettings` changes cancel and restart the pass.
- ffmpeg `fps=1/N:round=up` (default rounding is half an interval early), then a second run tiles.
- A pass = one discovery task filling `_pending` (`PendingVideos`) + `Parallelism` workers; player requests go to
  `_requested` first.
- `GetStates` / `GetActivity` feed the listing marks and the Trickplay › Activity tab; the viewer polls while waiting.
- Trickplay's own `RecheckHours` pass stays besides schedules; its time is `TrickplayStatus.NextPass`
  (`automaticNextRun`) ([schedules.md](schedules.md)).
- Sheets are cacheable (URL carries its `version`).
