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
- **Phone sheets (rule, 2026-10-23):** on a phone (≤ 640 px) every menu or popup that opens in the lower part of the
  screen is a sheet like the item menu: full width with no side margins, against the bottom edge (safe area padded),
  top corners rounded, over a dimming backdrop that closes it, and the page behind locked so the sheet and backdrop
  cover the scrollbar's room. Use the shared classes of `styles/phone-sheets.css`: `phone-sheet` on the popup,
  a `sheet-backdrop` next to it (a click on it closes; it is what locks the page). Applied to the item menu, the
  viewer's ⋮ menu, the default tags panel, the player's speed menu. A popup under a transformed or clipping ancestor
  goes in the top layer (`popover="manual"`, `showPopover()`) with the backdrop inside it, else `position: fixed` is
  relative to that ancestor (`DefaultTagsBadgeComponent`). Modal dialogs (`dialog.sheet`) already do this below
  761 px (`trickplay.css`, `base.css`). Not sheets: toasts, the operations panel and the selection bar (status that
  stays while the page is used), and the top bar's popovers (they open from the top).
