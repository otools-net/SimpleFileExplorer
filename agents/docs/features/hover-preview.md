# Hover preview (trickplay on tiles and rows)

Code: `TrickplayHoverService` (client, `src/app/trickplay/`), `TrickplayService.Get(path, queue: false)`.

- `TrickplayHoverService.attach` (called by the tile/row after render) draws the cells on a `<canvas>` in the thumbnail
  box, started by hovering the whole tile/row, or on touch by holding it (`pointerdown` + `HOLD_DELAY_MS`; the click
  after a hold is swallowed in the capture phase, `contextmenu` prevented, callout/selection off in CSS). The
  `contextmenu` listener captures: the item's own (a long press selects it, `ContextMenu.fromEvent`) on the same
  element came first and selected the video while its preview played; a video with a preview is selected by its box
  (e2e `trickplay.spec.ts`, `@ffmpeg`).
- Off with `Trickplay:HoverPreview` (`/api/config` `trickplayHover`).
- It asks with `?queue=false` (`TrickplayService.Get(path, queue: false)`) so hovering never queues a video.
- Resets on each new listing (`ListingStore.generation`).
- The gestures serve any `HoverPlayer` (`enabled`, `ready`, `isReady`, `play`) passed to `attach`: the trickplay one by
  default, animated images ([animated-hover.md](animated-hover.md)). `insertUnderMarks` puts an overlay under the marks.
