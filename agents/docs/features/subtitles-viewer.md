# Subtitles viewer

Code: client `app/formats/subtitles/` (`subtitles.ts` model, `subtitles-view.component.ts`), `SUBTITLES_FORMAT` in
`formats/formats.ts`, `VirtualRows` (`formats/virtual-rows.ts`), the subtitles part of `styles/reader.css` (it reuses
the JSON and CSV views' `.json-pane`, `.json-filter`, `.csv-scroller`, `.csv-body`). Server: `Core/Files/Subtitles.cs`,
`GET api/files/text/video` in `TextEndpoints`. Tests: `subtitles.spec.ts`, `subtitles-view.component.spec.ts`, a row
in `formats.spec.ts`, `SubtitlesTests`, `TextRouteTests.ASubtitleFileFindsItsVideoAsTheRightsAllow`. User docs:
`docs/user-guide/text-files.md#subtitles`. Plan: [file-viewers-plan.md](../development/file-viewers-plan.md).

## Parsing (`parseSubtitles`)

- **media-captions** (vidstack's, MIT, ESM): the W3C WebVTT parsing rules, SRT through its own parser (a lazy chunk
  of its own), `errors: true` gives problems with their **line**. Async (`parseText` streams the lines): the view drops
  an earlier parse finishing after a later one (`parses` counter).
- Its prod build does `class VTTCue extends window.VTTCue` when it loads: every browser has one, jsdom doesn't
  (`TestVTTCue` in `src/testing/setup.ts`).
- Cues don't carry their place in the source: each cue's **timing line** is the next `-->` line whose start time
  (`parseVTTTimestamp`, `,` read as `.`) is the cue's start, so cues dropped as invalid don't shift the others.
- Text: `tokenizeVTTCue` → runs (italic, bold, underline, `<v Ann>` as a voice run "Ann: "); tags WebVTT doesn't know
  (SRT's `<font>`) keep their text. `plain` (filter) strips the tags; lines joined with ` / ` in the list.
- `overlaps`: starts before the latest end so far (red start time, counted in the status bar).

## Playing a cue

- `GET api/files/text/video?path=` (`files.view` at the subtitle file, and the video must be `files.view` for the
  caller): `Subtitles.FindVideo` tries the name without its last marks (`Film.en.forced` → `Film.en` → `Film`, at most
  3 marks of at most 12 characters) with each of the library's video extensions (`FileTypes.Videos` of the effective
  settings), as written and in capitals: a few `File.Exists`, never a listing. The answer is the file's name as it is
  on disk (`Directory.EnumerateFiles` with the exact name: Windows finds `Film.MKV` for `Film.mkv`).
- Asked once the cues show, only when the host says `files.view`. **Play** is a link to
  `config.urls().player(video, start)` (`player?path=…&t=…`) in a new tab.

## Shift times (`shiftEdits`)

- Only the two times of each timing line change (`TIMING_TIMES`), written as they were: `,` or `.`, hours or not (a VTT
  time without hours gets them past an hour), never below 0. Applied with `TextEditor.edit`: one undo, dirty.
- `parseShift`: `2.5`, `-2,5`, `+1.2 s`. Offered while editing (the source must be editable), beside Edit.

## Formats

- `srt`, `vtt`. SSA / ASS stay text (media-captions reads them, but Shift times would need their own time format).
