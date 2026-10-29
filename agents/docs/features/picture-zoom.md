# Pictures in the viewer: actual size, drag, pinch

Code: `library/viewer/viewer.component.ts` (`zoomIn`, `zoomTo`, `onPinch`, the picture's pointer handlers, `onWheel`,
`clampPan`), `styles/viewer.css`. Tests: `viewer.component.spec.ts`, e2e `navigation.spec.ts` (two fingers). User docs:
`docs/user-guide/viewer.md#actual-size`.

- A picture drawn smaller than it is (`zoomable`) goes to its actual size on a click, a tap or Z, the point clicked
  staying under the pointer (`zoomTo`); a drag moves it (`pan`), never leaving a gap at an edge, a side smaller than
  the stage centred (`clampAxis`); the wheel scrolls it; a click, Z or Esc fits it again. Zoomed means the `<img>` at
  its zoomed size in px (`actualSize`) placed by a translate: the browser draws it sharp, no scaled bitmap.
- Two fingers (decided 2026-10-29): `PinchGesture` of `@use-gesture/vanilla` (MIT, 10.3.1 pinned) on the stage,
  for touch, pointer and Safari's gesture events alike; the viewer keeps its own sizes and edges. From the fitted size
  (`fitSize`, measured while not zoomed) up to `PINCH_MAX_FIT` times it or the actual size if larger, around the point
  between the fingers; let go within `PINCH_FIT_SLACK` of the fitted size, it fits again. A pinch ends a drag and a
  tap in progress; the swipe sees two fingers and does nothing.
- `pinchOnWheel: false`: Ctrl + wheel (and a touchpad's pinch) stays the browser's zoom, as over a video or a document
  (the library would prevent it there too).
- `.viewer-stage:has(> img) { touch-action: none }`: the page doesn't pinch-zoom behind a picture. The swipe uses touch
  events, which still come.
- Considered: `@panzoom/panzoom` (transforms and its own containment: either inside the box or covering it, neither
  "no gap at the edges, centred when smaller", and it would have replaced the tested mouse behaviour).
