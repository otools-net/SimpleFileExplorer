# Scrollbar gutter

Code: `src/Sfe.Web/src/styles/base.css`.

- `html { scrollbar-gutter: stable }`: the grid's justified rows are taller on a wider page, so a listing overflowing
  by a few px only at full width kept a scrollbar it didn't need (the sidebar is exactly `100dvh` tall).
- The player page opts out.
- Headless shots hide scrollbars: `launch({ scrollbars: true })` ([visual-check.md](../environment/visual-check.md)).
