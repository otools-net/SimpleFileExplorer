# Frontend conventions (Angular)

Code: `src/Sfe.Web/src/app/`, styles in `src/Sfe.Web/src/styles/`. Build mechanics: [build.md](../environment/build.md).

- Standalone components with `ChangeDetectionStrategy.OnPush`, `input()`/`output()`/`viewChild()`, state in signals
  (the app is zoneless: a change outside a signal is not drawn), `inject()`, built-in control flow
  (`@if`/`@for`/`@switch`), 4-space indents, `strict` + `strictTemplates`.
- Only `core/api.service.ts` makes HTTP requests (promises; an `AbortSignal` cancels); URLs stay relative (virtual
  directory).
- Styling is global CSS in `src/styles/` by class name (`styles.css` imports one partial per area), no component
  styles; hosts carry the classes (`host: { class: 'tile' }`, attribute selectors like `article[sfeFileTile]`) so the
  DOM and the CSS stay as they were.
- Icons: `<svg sfeIcon="name">` from `IconSpriteComponent`.
- Per-browser preferences through `core/preferences.ts`.
- Every button (and icon link) gets a `title` hint saying what it does, with its shortcut if any.
- A template event handler whose expression evaluates to `false` makes Angular call `preventDefault()`:
  `(pointerdown)="flag = a === b"` silently cancelled the mouse events of every press in the dialogs (native
  `<select>`s stopped opening). Put assignments with a boolean result in a method.
- A `<select>` whose options come from `@for`: bind `[selected]` on each `<option>`, not `[value]` on the select
  (Angular sets it before the options exist, so it shows the first option; a spec checks the shown value).
- The client hides what a right forbids: `config.can(...)`, or `ListingStore.can(right, item)` for what depends on the
  folder (folder rules) ([rights.md](../features/rights.md)).
- **Right clicks (rule, 2026-10-23):** every right click goes through the app's one menu, `ContextMenu`
  (`menus/context-menu.ts`, [context-menu](../features/context-menu.md)): an element with something to offer declares
  a `MenuTarget` (`[sfeContextMenu]`, or `useContextMenu()` for a component's host) whose entries are what its click,
  buttons or keys already do, with the same right checks, and **Open in new tab** (a `href` entry) wherever it leads
  to a file, a folder or a page. Never `(contextmenu)` handlers of your own, nor `preventDefault` on `contextmenu`
  (touch gestures excepted, as the player's).
- **Phone sheets (rule, 2026-10-23):** on a phone (≤ 640 px) every menu or popup that opens in the lower part of the
  screen is a sheet like the item menu: full width with no side margins, against the bottom edge (safe area padded),
  top corners rounded, over a dimming backdrop that closes it, and the page behind held (touch: the backdrop takes no
  pan and the sheet's scrolling stops at its ends, the root untouched, else Chrome's address bar comes back and the
  bottom bars jump; mouse: the root locked, the sheet and backdrop covering the scrollbar's room). Use the shared
  classes of `styles/phone-sheets.css`: `phone-sheet` on the popup, a `sheet-backdrop` next to it (a click on it
  closes; it is what holds the page). Applied to the item menu, the
  viewer's ⋮ menu, the default tags panel, the player's speed menu. A popup under a transformed or clipping ancestor
  goes in the top layer (`popover="manual"`, `showPopover()`) with the backdrop inside it, else `position: fixed` is
  relative to that ancestor (`DefaultTagsBadgeComponent`). Modal dialogs (`dialog.sheet`) already do this below
  761 px (`trickplay.css`, `base.css`: touch, the dialog `touch-action: none` and its `.sheet-body`
  `overscroll-behavior: contain`, the root untouched; mouse, the root locked). Not sheets: toasts, the operations panel and the selection bar (status that
  stays while the page is used), and the top bar's popovers (they open from the top).
- **Touch (rules, 2026-10-29; e2e `tests/e2e`):** text fields are ≥ 16 px on touch screens (`base.css`, `pointer:
  coarse`; else iOS zooms the page into them); a list option that must keep the focus in its field prevents
  `mousedown`, never `pointerdown` (WebKit then cancels the tap: no click, the option not taken on an iPhone); buttons
  are ≥ 24 px each way on phones (`expectTapTargets`), a flex row's icon button `flex-shrink: 0`. Every item of an
  overflow row is as tall as the row's others (32 px in the document toolbars): a taller one leaving into the "⋯"
  changes the row's height inside its ResizeObserver callback, which the browser reports as a "ResizeObserver loop"
  error (Angular logs it) and the content below shifts. Nor does an item's label change width with its state ("Save"
  → "Saving…"): show the state another way (`aria-busy`, a class).
- **Closing on a tap outside (rule, 2026-10-29):** listen to `document:pointerdown`, not `click`: iOS Safari sends no
  click for a tap on what isn't clickable (text, an empty area), so the popup stayed open on iPhones (main menu,
  sidebar). A long press on iPhones: `ContextMenu.holdStart` ([context-menu](../features/context-menu.md)).
- **Rows of buttons on narrow screens (rule, 2026-10-24):** a row of buttons that may not fit a 360 px phone is an
  overflow row (`sfeOverflowRow`): one line, the buttons leaving right to left into a "⋯" menu, the developer saying
  which stay. Never `flex-wrap` or buttons cut off; space its items with `gap`, not margins. Check with
  `overflow.mjs` and `narrow-audit.mjs`: [overflow-menus](../features/overflow-menus.md).
- **Battery (rule, 2026-10-26):** anything that polls or ticks uses `visibleInterval` (`core/visible-interval.ts`):
  nothing while the tab is hidden, once right away when shown. A timer only while it has work (an `effect` on the state,
  not a 1 s interval that checks). Animate `transform`/`opacity`, never `left`/`width` in an infinite animation (a
  layout per frame). Measure with the [battery probe](../environment/battery-probe.md).
