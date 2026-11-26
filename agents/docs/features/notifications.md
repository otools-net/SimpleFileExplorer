# Notification centre

Code: `core/notifications.ts` (`Notifications`), `core/toast.service.ts` (`ToastService`, `ToastLevel`, `ToastAction`),
`notifications/notifications.component.*` (the bell and its list), `styles/notifications.css`, the levels' colours in
`styles/toasts.css`, `library/library-route.ts` (`folderLink`). Tests: `notifications/notifications.spec.ts`. User
docs: [browsing](../../../docs/user-guide/browsing.md#notifications) (`notifications.png`, `phone-notifications.png`:
scene `notifications`). Release 2.0 step 4.5a (2026-11-26).

## Toasts

- **Every toast is kept:** `ToastService.show` records it with `Notifications.record`; nothing else writes there.
- **Levels** (`ToastLevel`): `info` (default), `success` (`toasts.success`: an action done — renamed, moved, deleted,
  saved, uploaded, extracted…), `warning` (`toasts.warning`: done in part), `error` (`toasts.error`). They replace the
  old `error` flag (option `{ level: 'error' }`). Colour on the toast's left edge and the list's dot (`--level`);
  the CSS class `.toast.error` stays (e2e specs read it).
- **Actions:** `run` (code, this tab only) and/or `link` (a page of the app for `Router.navigateByUrl`, kept after a
  reload). `ToastService.perform` runs `run` if any, else opens `link`: in the tab the in-memory `run` (keeping the
  sort, `store.navigate`), after a reload the link. Folder links: `folderLink(path, c)` (`/?c=&path=`).
- `gone()` on an action hides it in the list once it can't be done: the Undo of `UndoHistory.add` is gone once its
  entry was undone (by Ctrl+Z, the ⋮ menu or the toast).

## Store

- `localStorage` key `sfe.notifications.<space id>.<account id>` (0 for none): each browser, account and space its
  own (a link to a folder means that space). Newest first, at most **200**, for **30 days** (`prune`, on each change and
  load). Before the configuration is known (a toast at start), kept in memory and merged into the first key; another
  account or space then starts from its own.
- Other tabs follow through the `storage` event (the whole list is read again).
- **Unread:** a warning or an error, or anything recorded while `document.visibilityState` isn't `visible`; an info or
  success seen in its toast is read. Opening the list marks all read (`markAllRead`); the component remembers which
  were unread (`wasUnread`) to highlight them while it stays open.
- Signing out doesn't clear them (said in the user docs: **Clear all** on a shared computer).
- Copied paths' toasts ("“…” copied.") are kept too: `copy-path.service.spec.ts` ignores these keys.

## Bell

- `sfe-notifications` in the library's top bar (before ⋮) and the administration's (at the right end); count on the
  bell (99+), `aria-label` "Notifications: N unread". One popover at a time with ⋮ (`Popovers`: `'notifications'`);
  closes on a press outside it, Escape, or a button run. On phones (≤ 640 px) a phone sheet with its backdrop; the
  list scrolls inside.
- A screen reader hears the level before the message ("Error: …", visually hidden).

## Not done yet (step 4.5b)

- Server events through the change feed (task finished or failed while away, for the user who started it;
  administrators: failed background tasks) and a deep link to one task run in the task history.
