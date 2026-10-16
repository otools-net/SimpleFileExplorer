# Video streams (ffmpeg conversion)

Code: `src/Sfe.Core/Streaming/` (`VideoStreamer`, `VideoStreams`), `Thumbnails/Ffmpeg.cs` (`RunAsync`), route
`api/streaming/*`; client `ConfigService.videoStream(path)`, `VideoPlayerComponent`.

- `Sfe:Streaming` (`Mode` Off/Fallback/Always is per library, the rest server keys).
- `GET api/streaming/play?path=&t=&id=` (`files.stream` + `files.view`) runs ffmpeg from `-ss t` into the response
  (H.264/AAC fragmented MP4, `Accept-Ranges: none`, no length); H.264 8-bit 4:2:0 is copied when `CopyVideo`.
- `VideoStreams` is a root singleton handed to each library (like `FfmpegLimits`): it counts `MaxStreams` over every
  library and a new request with the same `{user}:{id}` stops that player's previous stream (seeks race the browser's
  abort).
- `Ffmpeg.RunAsync` kills ffmpeg on any failure (a write to a gone viewer throws `IOException`, not OCE).
- Client: `ConfigService.videoStream(path)` (null unless `/api/config` `streaming`) feeds
  `VideoPlayerComponent.stream`; the player switches on `error` or `videoWidth === 0` at `loadedmetadata` (HEVC plays
  black with sound), keeps `playback = { url, offset, streamed }`, shows `offset + currentTime` and the probe's
  duration, seeks inside `video.seekable` and otherwise sets a new url from the target (a drag waits for release:
  `pendingSeek`).
- Make the `VideoStream` once per item (`computed`): a new object resets the player.
- Safari may refuse it (no ranges).
- Playback › Video streams polls `api/streaming/sessions` ([system-info.md](system-info.md)).
