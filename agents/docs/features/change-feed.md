# Change feed (folders changed elsewhere)

Code: `LibraryBrowser.FolderChanged` (raised by `Invalidate`, after the cache is cleared), `Api/Endpoints/ChangeEndpoints.cs`
(`GET api/files/changes`, SSE; what one feed listens to and sends: `ChangeFeedQueue.cs`), the `X-SFE-Tab` header
(`TAB_ID` in `api.service.ts`); client `library/change-feed.service.ts` (`ChangeFeed`), `ListingStore.refresh`.
Tests: `ChangeFeedRouteTests`, `FileOperationsTests.ANewFileIsEmptyIndexedAndNeverOverAnother`, `comfort.spec.ts`.
Decided 2026-10-17: a live feed (not a refresh on focus).

- Every file operation already calls `LibraryBrowser.Invalidate` for the folders it touched (creates, renames, moves,
  copies, trash, restore, uploads): that is the one place SFE's own changes come from. Changes outside SFE: below
  (only the folders open in a tab are watched, never the library).
- One connection per open listing tab: subscribes to its library's `LibraryBrowser` (the request's container), queues
  changes in a bounded channel (handlers run in the changing request; nothing is written from there), writes
  `event: changed` / `data: {"path","origin"}` for folders the caller sees (`library.browse`, access read when it
  opened), a `ping` event after 25 s without anything (`ChangeEndpoints.PingEvent`: an event the page sees, unlike a
  comment, and with data, else it isn't dispatched), ends after 10 minutes (the browser reconnects, reading session and
  rights anew), 12 per caller (429). `retry: 5000`.
- Server lost (`ChangeFeed.serverLost`, the ⋮ button's yellow ! and a "Server not answering" line for everyone): an
  `error` not followed by `open` within 10 s (`LOST_AFTER`: the 10-minute reopen after `retry: 5000` isn't shown), or
  nothing at all for 60 s (`SILENCE`: a hang that keeps the connection open; the feed is then reopened). After an
  error status the browser stops trying (`readyState` CLOSED): reopened every 5 s (`RETRY`). Back on `open`, and the
  folder listed is refreshed (changes made meanwhile weren't told). Checked in Edge by
  `agents/tools/visual-check/server-lost.mjs` (stops and restarts the app).
- The feed is closed on `pagehide` and opened again on `pageshow` (when it was open): a page kept by the browser for
  Back held its connection, and browsers allow 6 per server over HTTP/1.1, so after a few full page loads the next
  page waited for minutes (found by the context menu check's many `goto`s).
- Also closed once the tab stays hidden `HIDDEN_GRACE` (30 s; a quick app switch keeps it): the `ping` every 25 s and
  the 10-minute reopen kept phones' radios waking all day ([battery probe](../environment/battery-probe.md)). Shown
  again: opened, and on `open` the folder listed is refreshed and `trashChanges` bumped (changes meanwhile weren't
  told); `serverLost` stays quiet while closed.
- `origin` is the `X-SFE-Tab` header of the request that changed it, read through `IHttpContextAccessor` in the handler
  (null for background jobs after their request ended): a tab ignores its own changes, which it already applied.
- Client: `EventSource('api/files/changes')` opened by the library page (closed when leaving it); a change of the folder
  listed (not a search) refreshes it 400 ms later: `refresh()` fetches the loaded count again and replaces the items in
  place (status stays `ready`, scroll stays, the selection keeps what is still there).
- `ChangeFeed.listen(listener)` (1.69.0): other parts hear the changes made by other tabs, whatever is listed (an open
  text file checks its version when its folder changed).
- It also carries `event: keepAlive` (`{ state }`, sent first and on each change) for the ⋮ indicator:
  [keep-alive.md](keep-alive.md). Its channel holds `object` items (`FolderChange`, `KeepAliveSignal`, `TrashSignal`);
  tests read events by name (`NextEventAsync`).
- And `event: trash` (`data: {}`, `ChangeEndpoints.TrashEvent`) when `Trash.EntriesChanged` fires, only to feeds
  whose caller has `files.delete` or `files.deletePermanently`, at most once per write (emptying the trash fires once
  per entry); no origin filter, so the deleting tab hears it too. Client: `ChangeFeed.trashChanges` (a counter), which
  the sidebar watches to read the count again ([trash.md](trash.md)).
- And `event: task` (a background task's run that ended: to its starter, failures to `tasks.view`; `?since=` for
  those missed): [notifications.md](notifications.md#task-runs-that-ended-step-45b).
- Proxies must not buffer it (`X-Accel-Buffering: no` for nginx); documented in `docs/installation.md`.
- And `event: writing` (`{ path, items }`): how far the items being written in a library folder are, a few times a
  second at most per folder ([partial-files](partial-files.md)); the client keeps it in `WritingProgress` and
  refreshes the folder listed when an item comes or ends.
- Connections' folders (2026-11-15): `ConnectionChanges.FolderChanged` (server-wide, told by the connection write
  routes, uploads and transfer endpoints) queues a `ConnectionFolderChange`; `ConnectionFeed` sends it as `changed`
  with `connection` to feeds whose caller uses that connection in its library and sees the folder (folder rules),
  read once per connection per feed. The client refreshes only when `connection` matches the place shown
  (`route.c`); `listen` listeners hear library folders only.

## Changes made outside SFE (2026-11-23, release 2.0 step 3.5a)

Code: `Core/Library/Watching/` (`FolderWatchers`, `WatchedFolder`, `FolderWatch`, `FolderSignature`,
`FolderWatchTimings`), `LibraryBrowser.Forget`, Api `ChangeFeedTabs`, `ChangeEndpoints.WatchAsync`
(`PUT api/files/changes/watch`), `ChangeFeedQueue.Watch`; client `ChangeFeed.watch`, `Api.watchFolder`; setting
`browse.liveChanges`. Tests: `FolderWatchersTests`, `ChangeFeedRouteTests` (`…OutsideSfe…`, `OnlyTheTabsOwnFeed…`),
`library/change-feed.spec.ts`.

- **What is watched:** the folder each tab lists. The feed is opened with `?tab=<TAB_ID>` (kept in `ChangeFeedTabs` by
  caller and tab while open); the tab names its folder with `PUT files/changes/watch` (`{ path }`, `X-SFE-Tab`), on
  each move and each time the feed opens (the server forgets with the feed; every 10 minutes). Null for a search or
  the setting off; a connection's folder: below. The folder must exist and be `Allowed` to browse for the feed's caller;
  the feed's library must be the request's (`TenantId`), else `watching: false`.
- **Holds:** `FolderWatchers.Watch` returns a `FolderWatch`; one watcher per folder, reference counted, let go with
  the last hold (the feed's next folder is taken before the previous one is let go: a refresh doesn't restart it). At
  most `MaxFolders` (256) per library, then nothing more (one warning).
- **How:** `FileSystemWatcher` on the folder only (`IncludeSubdirectories` off: one inotify watch on Linux), names,
  sizes, last writes. **Polled** instead (`FolderSignature`: count and an order-free hash of name, size, last write,
  attributes; read at least every 10 s, and never more than a tenth of the time: a read of 3 s waits 30 s) when the
  folder is on a network mount outside Windows (`NetworkFolders.IsNetwork`: inotify there sees only this computer's
  changes), when the watcher can't be made (inotify's `max_user_watches`, a folder gone), or when it fails (not on
  a buffer overflow: that only tells a change). Windows shares are watched (SMB change notifications).
- **Told:** a change settles 300 ms, a folder is told at most every 2 s (a file written for minutes); then
  `LibraryBrowser.Forget` (the kept listings, without `FolderChanged`) and `FolderWatchers.Changed`, queued by every
  feed of the library as `changed` with `outside: true` and no origin, filtered by rights like the others (another
  tab watching the folder doesn't matter: the event says only "changed").
- **Left out:** items ending `.sfe-partial` (SFE's writes: the `writing` event tells them) and the work folder at the
  top. SFE's own changes: `FolderChanged` marks the folder; a watcher's change within 1 s of it is SFE's, not told
  again; a polled folder takes its next read as the new base. A change outside SFE within that second is told by
  SFE's own event anyway (the folder is refreshed). An operation that touches a folder for longer (deleting many
  files) may still be told twice: a second refresh in place, harmless.
- **Off switch:** `browse.liveChanges` (user level, on by default): off, the client names no folder and drops
  `outside` events (another user's tab may watch the same folder).
- Tests share one process: `ChangeFeedTabs` is static (like the feed count), so route tests use a tab name of their
  own.

## Changes made in a connection's storage (2026-11-23, release 2.0 step 3.5b)

Code: Core `Connections/Indexing/` (`ConnectionWatchers`, `ConnectionWatch`, `ConnectionFollow`,
`ConnectionFollowResult`, `ConnectionIndexes.FollowAsync`, `ConnectionIndexer.FollowAsync`,
`RemoteChangeReader.Folders`); Api `ChangeFeedQueue.WatchAsync`, `ConnectionFeed.WatchableAsync`; client
`ChangeFeed.shownFolder`. Tests: `ConnectionIndexRouteTests` (`AWatchedFolderHears…`, `AFolderIsNoLongerRead…`),
`ConnectionIndexerTests.AQuickLook…`, `library/change-feed.spec.ts`.

- **Named** as a library folder is: `PUT files/changes/watch` with `{ path, connection }` (the path from the
  connection's root). Followed when the caller uses the connection, sees the folder (its folder rules) and the
  provider has `ChangeCursor`; else `watching: false`. The folder's existence isn't checked (it would cost a
  provider call; nothing is read per folder anyway).
- **Holds:** `ConnectionWatchers` (per library: the index is the library's), reference counted per connection and
  folder, at most 256 folders; a connection with a hold has one loop reading its changes every 30 s
  (`PollEvery`, shorter in tests), stopped with its last hold.
- **Read through the index**, not a cursor of its own: the listing of an indexed folder comes from the index, so
  the index must hold the change before the client lists again. `ConnectionIndexes.FollowAsync` runs a **quick look**
  (`ConnectionIndexRun.Quiet`: in `_running`, so it never overlaps a sync, but not in `Running` nor the status; a
  sync asked meanwhile waits for it; no task history run, failures logged at Debug) that reads and writes the changes
  as a sync does (`ConnectionIndexer.FollowAsync`), only when the index is `Following` with a cursor of this root
  and account. Otherwise (never made, being listed, cursor reset) it does what opening the connection does
  (`OpenedAsync`: a sync when the index is over 10 minutes old, at most a check a minute).
- **Cost of a look with no change:** one provider call (in a gate turn) and one write of `LastSync`; counts and the
  purge of removed rows only when changes came (both read the connection's rows). `LastSync` kept fresh keeps the
  index's listings whole (`MaxAge` 30 min) and stops opening the connection from starting a sync.
- **Told:** the reader notes the parents of changed items and changed folders themselves (1,000 at most, then
  `TooManyFolders`: every watched folder of the connection is told); the watched folders among them (compared by the
  provider's case rule) raise `Changed`, queued by every feed of the library as `changed` with `connection` and
  `outside: true`, filtered by `ConnectionFeed.SeesAsync` like SFE's own. The event carries the path as the tab named
  it. The server's kept listings of the connection (`RemoteListings`) are forgotten when changes came.
- SFE's own changes come back from the provider's cursor at the next look and are told again (a second refresh in
  place, harmless); not filtered, as the window would hide real changes for 30 s.
- Client: `shownFolder()` gives the connection (`route.c`) too; a library and a connection folder of the same path
  are different keys; `changed()` already refreshes only when `connection` matches the place shown.
