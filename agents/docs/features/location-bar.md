# Compact location bar

Code: `src/app/library/location/` (`LocationStripComponent`, `LocationComponent`, `LocationLinks`,
`nav[sfeFolderNav]`), `src/app/library/scan/` (`ScanPanelComponent`).

- `LocationStripComponent` (before `section[sfeLocation]` in `.page`) is `position: sticky` with height 0, its bar
  absolute, so showing it moves nothing; shown while `LocationLinks.headHidden` (set by an `IntersectionObserver` on
  `.location-head` in `LocationComponent`).
- `LocationLinks` holds the crumbs and folder links for both, `nav[sfeFolderNav]` draws the buttons.
- It sticks below the scan panel: `ScanPanelComponent` writes its height to `--scan-panel-h` on `<html>`
  (ResizeObserver, 0 while hidden).
