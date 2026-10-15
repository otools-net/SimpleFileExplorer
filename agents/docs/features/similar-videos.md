# Similar videos

Code: `src/Sfe.Core/Trickplay/VideoFingerprint.cs`, `src/Sfe.Core/FileTracking/` (`SimilarVideoFinder`,
`VideoMatcher`); client `src/app/tracking/`. Needs trickplay ([trickplay.md](trickplay.md)).

- `fingerprint.bin` = per trickplay frame a dHash of 9×8 grey pixels + contrast (`VideoFingerprint`), made from the
  sheets with ffmpeg `untile` (no image library); trickplay without one (ffmpeg failed) is not fingerprinted again
  until it is remade.
- `SimilarVideoFinder` (on request, background) reads them for indexed videos; `VideoMatcher` gets candidates from a
  sorted 3-band index with offset votes, scores at the best offset.
- Match % = matched frames / informative frames of the longer video.
- Results in memory only (no table), cursor `{run}:{index}`.
- Tests (`SimilarVideoTests`) need ffmpeg ([testing-backend.md](../development/testing-backend.md)).
