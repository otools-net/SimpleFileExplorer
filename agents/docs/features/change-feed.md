# Change feed (folders changed elsewhere)

Code: `LibraryBrowser.FolderChanged` (raised by `Invalidate`, after the cache is cleared), `Api/Endpoints/ChangeEndpoints.cs`
(`GET api/files/changes`, SSE), the `X-SFE-Tab` header (`TAB_ID` in `api.service.ts`); client
`library/change-feed.service.ts` (`ChangeFeed`), `ListingStore.refresh`. Tests: `ChangeFeedRouteTests`,
`FileOperationsTests.ANewFileIsEmptyIndexedAndNeverOverAnother`, `comfort.spec.ts`. Decided 2026-10-17: a live feed
(not a refresh on focus).

- Every file operation already calls `LibraryBrowser.Invalidate` for the folders it touched (creates, renames, moves,
  copies, trash, restore, uploads): that is the one place the event comes from. Changes on the disk outside SFE are not
  told (no file-system watcher over millions of files).
- One connection per open listing tab: subscribes to its library's `LibraryBrowser` (the request's container), queues
  changes in a bounded channel (handlers run in the changing request; nothing is written from there), writes
  `event: changed` / `data: {"path","origin"}` for folders the caller sees (`library.browse`, access read when it
  opened), a `: keep-alive` comment every 25 s, ends after 10 minutes (the browser reconnects, reading session and
  rights anew), 12 per caller (429). `retry: 5000`.
- `origin` is the `X-SFE-Tab` header of the request that changed it, read through `IHttpContextAccessor` in the handler
  (null for background jobs after their request ended): a tab ignores its own changes, which it already applied.
- Client: `EventSource('api/files/changes')` opened by the library page (closed when leaving it); a change of the folder
  listed (not a search) refreshes it 400 ms later: `refresh()` fetches the loaded count again and replaces the items in
  place (status stays `ready`, scroll stays, the selection keeps what is still there).
- `ChangeFeed.listen(listener)` (1.69.0): other parts hear the changes made by other tabs, whatever is listed (an open
  text file checks its version when its folder changed).
- It also carries `event: keepAlive` (`{ state }`, sent first and on each change) for the ⋮ indicator:
  [keep-alive.md](keep-alive.md). Its channel holds `object` items (`FolderChange` or `KeepAliveSignal`); tests read
  events by name (`NextEventAsync`).
- Proxies must not buffer it (`X-Accel-Buffering: no` for nginx); documented in `docs/installation.md`.
