# Overflow menus and narrow screens

Code: `src/Sfe.Web/src/app/menus/overflow-row.ts` (`OverflowRowDirective`, `OverflowItemDirective`,
`OverflowMoreComponent`), `styles/overflow.css`; the screens below. Related: [frontend](../development/frontend.md)
(the rule), [context-menu](context-menu.md) (`ContextMenu.toggle`, `MenuEntry`), [visual-check](../environment/visual-check.md).

A row of buttons that may not fit a narrow screen stays on **one line**: the buttons that don't fit leave, and a
**"⋯"** (horizontal dots, `more-horizontal`, unlike the ⋮ of item menus) at the row's end opens a menu with them.
Nothing wraps, is cut or becomes unreachable. Built as an epic (2026-10-23/24); the plan is in git history.

## Target sizes

- Minimum **360 px wide** (360×800, the narrowest common phone; StatCounter, August 2026). Checked at 360×800,
  390×844, 414×896 portrait and 800×360 landscape (CSS pixels; the window size, not the exact viewport). 320 px must
  stay usable, not checked.
- Rows react to their **own width**, not a breakpoint: phones, narrow desktop windows and small viewers alike.

## How `OverflowRow` works

```html
<div class="…" sfeOverflowRow="Player controls">          <!-- the menu's title on phones -->
    <button … [sfeOverflowItem]="more.play" overflowStay>…</button>
    <button … [sfeOverflowItem]="more.loop">…</button>     <!-- () => MenuEntry -->
    <span … [sfeOverflowItem]="null" [overflowPriority]="-1">…</span>  <!-- leaves first, no entry -->
    <button sfeOverflowMore></button>                       <!-- last child -->
</div>
```

- **Default order: right to left.** Equal priorities leave from the right; a lower `overflowPriority` leaves first;
  `overflowStay` never leaves. Each screen's developer decides what stays (table below). Both are read once, when the
  item is added.
- **Every child of the row is an item** except the "⋯": only items are measured, so a spacer goes outside the row
  (or becomes `margin-right: auto` on an item). A text that shortens is two items: the long part with priority -1
  and a null entry (leaves first, adds nothing to the menu), the short part shown by CSS once it has left.
- Measuring: `@fluentui/priority-overflow` 9.4.3, **pinned exactly** (its API is `@internal`; it reads
  `process.env.NODE_ENV` at run time, hence `define` in `angular.json`). It compares the row's `clientWidth` with
  the visible items' `offsetWidth` plus the "⋯"'s; a hidden item gets `data-overflowed` (`display: none`). It counts
  **no gaps, margins or padding**: `fitGaps` adds the row's padding (and border with `overflowIn`) and the gaps of
  the items shown to its `padding` option. Space items with the row's `gap`, never margins; items have
  `flex-shrink: 0`.
- `fitGaps`: at a new width it starts from the gaps of the items always there, then grows to the items shown, and
  within one width never shrinks (shrinking back looped). Its second pass is a microtask: the library reports nothing
  when the row didn't change.
- `[overflowIn]="element"`: a row as wide as its content (the selection bar, centred) can't measure its room; it is
  measured in a full-width element around it.
- The "⋯" stays in the row unseen while nothing left (`.overflow-more:not(.shown)`: absolute, hidden), so its width is
  known before it shows; `.shown` only when a hidden item has an entry. It opens the app's one menu
  (`ContextMenu.toggle`, target kind `overflow`) with the hidden items' entries in row order, computed when it opens:
  a phone sheet on phones. `MenuEntry.checked` gives a check mark (`menuitemcheckbox`), `MenuEntry.disabled` a greyed
  entry that doesn't run; a `submenu` works as in other menus (speed, zoom, view switch).
- The library hides and shows items synchronously while it measures: `data-overflowed` is set on the element in
  `onUpdateItemVisibility`, not through a signal, or it would measure the old layout.
- A row hidden elsewhere on the page (the selection bar behind the viewer) measures 0 and has its "⋯" shown: scripts
  click the "⋯" inside the row they check.

## Where it is used, what stays

| Screen | Code | Stays | Leaves (first to last) |
|---|---|---|---|
| Video player (viewer, player page) | `video-player.component.ts`, `.player-row` | Play, Mute, Fullscreen, the position | the length (no entry), Cinema, Save frame, Loop, Convert, Speed (submenu of rates), Forward, Back |
| Selection bar | `selection-bar.component.ts`, `.selection-bar` in `.selection-dock` | Clear, "N selected", Select all, Tags | the long count (no entry), Download, Copy, Move, Delete, Rename, Quick links |
| Document toolbars (text, editor, Markdown, JSON, CSV, subtitles) | `formats/*/…-view.component.ts`, `.text-toolbar`; shared entries `formats/toolbar-entries.ts` | editing actions (`sfe-text-edit-actions`), fields (filters, subtitles' shift) | Aa, Find, Line numbers, the format's buttons, the view switch (submenu) |
| PDF toolbar | `pdf-view.component.ts` | Previous, page, Next, Smaller, Larger; find as a magnifier on phones | Dark pages, Outline, the zoom list (submenu) |

Phone layouts of the same work that are not overflow rows:

- Text editor: Save a copy and Done show only their icon ≤ 640 px (`.text-action-label`).
- Location bar: ≤ 640 px the folder's buttons and the pin give way to one "⋯" (`.location-more`) opening the folder's
  own menu ([location-bar](location-bar.md)).
- Administration: a section's sub-page chips are one line scrolling sideways ([admin-page](admin-page.md)).
- List view: a narrow list (container `files-list` ≤ 560 px) gives each file two lines, the name then type · size ·
  resolution (`.row-meta`, [listing-page](listing-page.md)).
- Moved files: a narrow missing file (container `tracking-missing` ≤ 480 px) puts Choose file… and Forget on a line
  below its text ([missing-files](missing-files.md)).
- Text viewer: long lines unwrapped scroll sideways, with a fade at the right edge while a line goes on
  ([text-viewer](text-viewer.md), `lineContinues`). The JSON tree, subtitle cues and CSV cells keep their "…" on purpose.
- Viewer bar, picture editor, top bar, EPUB: fine at every size (audited), unchanged.

## Checking

- `agents/tools/visual-check/overflow.mjs`: each scene's rows on one line, inside the screen, at the target sizes and
  1280×800; opens the row's "⋯" for a menu screenshot; optional `probe` per scene (list rows, long lines, a finger
  swipe over the text).
- `agents/tools/visual-check/narrow-audit.mjs`: every screen (~60 scenes) at the phone sizes: off screen, cut buttons,
  buttons over text, rows of 3+ buttons on several lines, page wider than the screen. Slow: run it in the background.
- Specs: `overflow-row.spec.ts` stubs `ResizeObserver` and gives widths through `data-width` (jsdom has no layout, no
  gaps); wait with `setTimeout`, not one `Promise.resolve()`. Without `ResizeObserver` nothing leaves.
- Before offering the developer choices of what stays, measure with `overflow.mjs`: an estimate was wrong once.
