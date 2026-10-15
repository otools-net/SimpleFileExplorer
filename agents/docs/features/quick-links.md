# Quick links

Code: `src/Sfe.Core/Access/QuickLinkService.cs`, entity in `Data/Entities/Access/`, route `api/me/quick-links` in
`src/Sfe.Api/Endpoints/`; client `src/app/library/quick-links/` (`QuickLinksService`, `QuickLinksComponent`,
`button[sfePin]`), `ListingStore.reveal`.

- `QuickLinks` table in the accounts' database (user, tenant, path, `IsFolder`, `Position`; no unique index, since
  paths compare as the host file system does: `QuickLinkService` keeps one row per path, max 100).
- `api/me/quick-links` (`account.quickLinks`, never guests): GET, POST `{ paths, position }` (adds, or moves those
  already there, before the link now at `position`; null = new ones at the end), DELETE `?path=`; all answer the whole
  list with `exists`/`kind` read from the disk.
- Client: `QuickLinksService` (root), `section[sfeQuickLinks]` first in the sidebar, `button[sfePin]` (location head,
  folder actions, viewer), the selection bar's **Quick links**.
- Drags: `QuickLinksComponent` listens to `document:dragstart` and takes the nearest `[data-path]` (tiles, rows,
  folders, breadcrumbs; the selection when a selected file is dragged) as `DRAG_ITEMS`; links move with `DRAG_LINK`.
- A file link calls `ListingStore.reveal`: navigates to the parent, `load()` loads pages until the file is there
  (`REVEAL_LIMIT`), then opens media in the viewer or highlights it (`revealed`).
- Phones reorder in the edit mode (↑/↓), not by dragging.
