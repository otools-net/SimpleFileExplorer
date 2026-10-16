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
