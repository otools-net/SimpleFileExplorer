# Previews of connection files (thumbnails, content cache, viewer, player)

Code: Core `Connections/Previews/` (`ConnectionCacheStore`, `ConnectionContentCache`, `CachedContent`,
`ConnectionThumbnailStore`, `RemoteThumbnailBatches`, `ConnectionPreviews`, `PreviewAsk`, `RemoteCacheKey`,
`MediaSniffer`), `Connections/Dropbox/DropboxThumbnails`, `IRemoteStorage.ThumbnailsAsync`, `RemoteMedia`,
`RemoteThumbnail`, `Indexing/RemoteMediaRows`, `Options/ConnectionCacheOptions`, `Settings/ConnectionCacheSettings`,
`Ffmpeg.ConnectionInputAsync`, `ThumbnailService.RenderCachedAsync`, `MediaDimensions.ReadCachedAsync`,
`VideoStreamer.ProbeCachedAsync` / `TryBeginCached`; Api `ConnectionPreviewEndpoints` (thumbnails, text),
`ConnectionTextEndpoint`, `ConnectionStreamingEndpoints`, `ConnectionPreviewFiles`, `ConnectionCacheProblems`,
`ConnectionCacheWorker`; client `core/connection-place.ts` (`atPlace`, `placeRoute`), `formats/file-source.ts`,
`formats/formats.ts` (`PLACE_FORMATS`), `connections/connection-settings.ts`. Tests: `ConnectionContentCacheTests`,
`ConnectionPreviewRouteTests`, `ConnectionPreviewFfmpegTests` (need ffmpeg), `MediaSnifferTests`,
`DropboxThumbnailTests`, `connection-previews.spec.ts`, e2e `connection-viewer.spec.ts`. Plan: [connections-index-plan](../development/connections-index-plan.md)
(phase 5). User docs: [Previews of a connection's files](../../../docs/administration/connections.md#previews-of-a-connections-files).

## The cache (one for the server)

- `{Thumbnails:CacheDirectory}/connections/{connection id}/{2 hex}/{key}{suffix}`, the **configuration's** cache
  folder (not a library's `tenants/{slug}`): registered in `AddSfeAccess` (`AddConnectionCache`), the stores handed
  to each library like `FfmpegLimits` (`TenantHost.AddServerServices`). A server connection used by three libraries
  is cached once.
- Key (`RemoteCacheKey`): 20 hex of SHA-256 of the provider's id (else the path, lowered when the provider ignores
  case), `-`, 20 hex of SHA-256 of the version. **Version = the provider's content hash** (Dropbox `content_hash`),
  else size and date: the owner's "rev" read as "what changes when the content changes" (the index rows hold the
  hash, not `rev`; a `rev` bump with the same bytes keeps the entry). The key is made from what the provider or the
  library's index row says of the file, **never from the request**.
- Suffixes: `.bin` (the file), `.t{64|128|256|640|1024}.jpg` (a thumbnail; empty = it has none, not asked again until
  the file changes), `.media` (`w h seconds`, `-` unknown: what the provider or ffmpeg told), `.part` (downloading).
- Limits (server settings `connections.cache.*`, `ConnectionCacheOptions`): `maxSizeMegabytes` 4096 (0 = off),
  `maxFileMegabytes` 2048, `maxAgeDays` 30. Read every 10 s from `SettingLayers.ServerLevel()`.
- Eviction (`ConnectionCacheStore.MakeRoom`): the size is measured once, then counted; past the limit the files used
  longest ago (last write time, touched on each use) go down to 90 %. A file held (`CachedContent`, until disposed:
  a stream playing, ffmpeg reading) or being written (`.part` younger than a day) stays.
- `ConnectionCacheWorker` (Api, every 5 min once the update is confirmed): at its first run, turns the cache off when
  its folder is the web root, `Sfe:Root` or a library's root, or inside one (`IsServed`, `Disable`: logged as an
  error); then `Sweep`: files unused for `maxAgeDays`, folders of connection ids no longer in the accounts' database.
  `DELETE api/connections/{id}` removes the connection's folder at once (`Forget`).

## Content cache (`ConnectionContentCache`)

- `OpenAsync(ContentAsk)`: refuses (`ConnectionCacheException`, `CacheRefusal`) when off, over `maxFileMegabytes`
  (by the size told, and again while reading: a provider sending more is cut off), without content (Paper), or busy.
- One download per file whoever asks (others wait for it); at most `DownloadsPerCaller` (2) per caller
  (`RequestLimits.Caller`: account or address) and `MaxDownloads` (4) for the server. A download nobody waits for any
  more stops (waiters counted, as `ThumbnailService`).
- The download opens the connection itself (`ConnectionService.OpenAsync`) so a request leaving doesn't dispose the
  storage under the others, takes one turn of `ConnectionGates` for the read, writes a `.part`, checks the length,
  moves it in place, then deletes the other versions of the file.
- Routes answer: 413 `too-large`, 429 `connection-cache-busy` (+ `Retry-After` 5), 404 (off, no content).

## ffmpeg and the cache

- `Ffmpeg.ConnectionInputAsync(CachedContent)` is the only way a cached file reaches ffmpeg: the path must be inside
  the cache's folder, and its first bytes, **read again now**, must be media (`MediaSniffer`: JPEG, PNG, GIF, WebP,
  TIFF, BMP, HEIC/AVIF, ISO/QuickTime, Matroska/WebM, AVI, ASF, FLV, MPEG-PS/TS, RealMedia, MP3/ADTS, WAV, FLAC, Ogg,
  AIFF, APE, WavPack, AMR); then the arguments of `LibraryInputAsync` (`-protocol_whitelist file`,
  `-format_whitelist` without playlists and scripts). A playlist, concat script, page or SVG named `.mp4` is cached
  (the bytes are needed to know) but never given to ffmpeg: `InvalidOperationException`.
- Used by `ThumbnailService.RenderCachedAsync` (in the server-wide thumbnail gate), `MediaDimensions.ReadCachedAsync`,
  `VideoStreamer.ProbeCachedAsync` / `TryBeginCached` (the stream holds the cached file until it ends).

## Thumbnails (`GET api/connections/{id}/thumbnails/{**path}`)

- Only image and video names (`MediaClassifier`); 404 without provider thumbnails and without ffmpeg. The file's
  version from the library's index row (`RemoteMediaRows.FindAsync`, no provider call), else `GetAsync`; checked by
  folder rules at the path asked and at the item (path and id).
- Order (`ConnectionPreviews.ThumbnailAsync`): the cache; the provider through `RemoteThumbnailBatches` (requests for
  one connection and size within 30 ms, up to 25, in one `ThumbnailsAsync` call and one gate turn, on a storage the
  batch opens); then, for a **picture** the provider doesn't thumbnail (Dropbox: over 20 MB, other formats),
  ffmpeg from the content cache; a **video** only from a copy already cached (played): a thumbnail never downloads a
  video. A refusal that may pass (busy, ffmpeg missing) keeps no "none" mark.
- A row of the index may be up to a sync behind the provider (≤ 10 min after a browse): its thumbnail is of the
  version the index knows.
- Dropbox (`DropboxThumbnails`): one file through `get_thumbnail` (its `media_info` read), several through
  `get_thumbnail_batch` (25 a call; base64; no `media_info` since 2019, read anyway); `unsupported_*` = none.

## Width, height, duration

- Written on the library's `RemoteFiles` row (`RemoteMediaRows.FillAsync`: same key, hash and size, only fields still
  null) when a thumbnail's answer tells them (`media_info`) or when a file passes through the content cache
  (`MediaDimensions.ReadCachedAsync`: picture headers, else ffmpeg). Kept beside the file (`.media`) so every library
  using the connection gets them without reading again; told once per file and version per library (memo of 10 000).
- `RemoteIndexKeys.Describe` forgets them when the hash or size changes. Never a pass downloading everything.
- Listings get them in one query per page (`RemoteMediaRows.AttachAsync`, `ConnectionBrowsing`); search rows already
  carry them, so `w:` `h:` `r:` `d:` find what is known.

## Viewer and player on a connection: what works

| Format | Works | How |
|---|---|---|
| Pictures | yes | `files/raw` (ranges); thumbnails as above; large pictures through their thumbnail as in the library |
| Video the browser plays | yes | `files/raw` with ranges |
| Other video (HEVC, AVI, MKV…) | yes, when streaming is on and ffmpeg is there | `streaming/probe` and `streaming/play?path=&t=&player=` from the content cache: the first probe waits for the whole download; not over `maxFileMegabytes` |
| Audio | yes | `files/raw` |
| PDF | yes | pdf.js on `files/raw` |
| Text, code, logs (Open as text too) | read only | `files/text` (whole, ≤ 10 MB, 413 / 415); no parts, Follow, edit or save |
| Markdown, JSON, CSV/TSV, subtitles | read only | the same text route through `FILE_SOURCE` (`library: false`); Markdown folder links stay in the connection; subtitles don't look for their video |
| EPUB, Flash, archives | no | they read the file through library routes (`api/files/epub`, `flash`, `explore`); the file opens in a tab / downloads |
| Picture editor, text editor | no | saving to a connection is left for later |

- Client: `atPlace` keeps `config.streaming` (the library's mode) and `features.thumbnails` = the provider's or the
  library's; `createUrls(…, place)` builds `connections/{id}/streaming/play` (`player`: the route's `{id}` is the
  connection); `Api.textFile` / `streamProbe` take the connection (`placeRoute`); `FILE_SOURCE.library` is false at a
  place; `PLACE_FORMATS` = `FORMATS` without EPUB, Flash, archive.
- Streams of an account's own connection are listed as "(a file of a private connection)" in Playback › Video
  streams (administrators read it).

## Gotchas

- The batch and the download open their own storage: a provider's storage is disposed with the request that opened
  it (`RegisterForDisposeAsync`), and another request may be the one waiting.
- `FakeStorage.ThumbnailsAsync` records one call `thumbnails a|b|c`; `FakeAccount.Thumbnailed` says which files get
  one (and their `RemoteMedia`); `FakeAccount.HoldReads` holds reads (a slow download).
- The connection's settings tab lives in `connections/connection-settings.ts` (as `tokens/token-settings.ts`):
  `ApiTests.EverySettingOfTheClientsSettingsDialogExists` reads it too.
