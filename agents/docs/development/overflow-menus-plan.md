# Overflow menus (narrow screens): plan and progress (epic)

Code: planned `src/Sfe.Web/src/app/menus/overflow-row.ts` (`OverflowRow`, `[sfeOverflowItem]`, the "⋯" button),
`styles/overflow.css`; then each screen's component (listed in its step). Related: [frontend](frontend.md) (phone
sheets rule), [context-menu](../features/context-menu.md) (`ContextMenu.toggle`, `MenuEntry`),
[video-player](../features/video-player.md), [selection-and-keys](../features/selection-and-keys.md),
[visual-check](../environment/visual-check.md).

Goal: on a phone (and in any narrow window) some rows of buttons don't fit: the player's controls wrap onto a second
row, the selection bar's buttons on the right are cut off, others may be hidden. A row of buttons gets a **"⋯" button
that appears only when not every button fits**; it opens a small menu with the buttons that were left out. Nothing
wraps, nothing is cut, nothing becomes unreachable.

## How to use this file

- The epic is built over several sessions, **one step per session**: take the first step of the
  [checklist](#checklist) that isn't `[x]`, do it, and **tick it in the same commit** (`[x]` with the version). One step
  = one commit = one minor version (step 0 has nothing visible: it may ship with step 1).
- **Every screen step starts by asking the developer** which of that screen's buttons stay and which may leave (the
  step lists the buttons and what the default gives). Their answer is written into the step's **Decided** line before
  any code; a step whose Decided line is empty is not started.
- Gotchas found while building go in [Found while building](#found-while-building).
- **A screen found at any moment** (step 3's audit, or while working on any other step: a row that wraps, a cut or
  hidden button, something sticking out at the [target sizes](#target-sizes)) **is told to the developer right away** (which screen, what is
  wrong, a screenshot if there is one) **and added to the plan in the same session**: a new step before step 9, with
  the same shape (code, buttons left to right, what the default hides first, the question, an empty Decided line, its
  checklist), and a line in the [checklist](#checklist). It is not fixed on the side of the current step.
- When the last step is done: lasting facts go into `agents/docs/features/overflow-menus.md` (indexed in `AGENTS.md`)
  and a rule bullet in [frontend](frontend.md); then this file is removed (rule in `AGENTS.md`).

Status legend: `[ ]` to do, `[~]` started, `[x]` done (version), `[-]` dropped (why).

## Checklist

| Step | Screen | Status |
|---|---|---|
| [0](#step-0--the-shared-piece) | The shared piece (`OverflowRow`) | `[x]` committed with step 1 (version at the merge into main) |
| [1](#step-1--video-player-controls) | Video player controls (viewer and player page) | `[x]` committed (version at the merge into main) |
| [2](#step-2--selection-bar) | Selection bar (several files selected) | `[x]` committed (version at the merge into main) |
| [3](#step-3--audit-of-the-other-screens) | Audit of the other screens (finds the next steps) | `[ ]` |
| [4](#step-4--viewer-bar) | Viewer bar | `[ ]` |
| [5](#step-5--document-viewers-toolbars) | Document viewers' toolbars (text, PDF, EPUB, Markdown, JSON, CSV, subtitles) | `[ ]` |
| [6](#step-6--picture-editor) | Picture editor | `[ ]` |
| [7](#step-7--top-bar-and-location-bar) | Top bar and location bar | `[ ]` |
| [8](#step-8--administration-toolbars) | Administration toolbars (logs, tasks, scan panel) | `[ ]` |
| [10](#step-10--file-names-in-the-list-view) | File names in the list view (found in step 2) | `[ ]` |
| [9](#step-9--finishing) | Finishing (notes, rule, plan removed) | `[ ]` |

## Target sizes

The narrowest screen that must look right is a regular smartphone's, taken from the most common mobile resolutions
(StatCounter, mobile, worldwide, August 2026: 414×896 13.6 %, 360×800 9.3 %, 390×844 6.8 %; these are CSS pixels):

- **Minimum: 360 px wide** (360×800, the narrowest of the common ones, most Android phones). At 360 px every button
  is reachable (in the row or in the "⋯"), nothing wraps, is cut or sticks out. Narrower screens (320 px, old small
  phones) must stay usable but are not checked.
- **Checked at:** 360×800, 390×844 and 414×896 portrait, and 800×360 landscape (the player, the viewer). The page's
  visible height is smaller than the screen's (the browser's bars): the visual checks use the screen size as the
  window size, an orientation, not an exact viewport.
- Desktop narrow windows are covered by the same rule (the row reacts to its width, not to a breakpoint).

## Design

- **Client only.** No route, database, setting or stored file changes: no migration path needed
  ([production-upgrades](production-upgrades.md)); no new right (each entry keeps its button's right check).
- **Default order: right to left.** When the row is too narrow, the rightmost button that may leave goes first, then
  the next one to its left, and so on. Per screen the developer says (step's Decided line):
  - which buttons **stay** (never go into the menu);
  - optionally, an order other than right to left.
- **"⋯" at the right end of the row**, where the hidden buttons were; shown only while something is hidden. Its icon
  is **horizontal dots** (`more-horizontal`, decided 2026-10-23), unlike the vertical ⋮ of the file menus.
- **One shared piece** (`menus/overflow-row.ts`, built in step 0), used in a template like this:

  ```html
  <div class="…" sfeOverflowRow="Player controls">          <!-- the menu's title on phones -->
      <button … [sfeOverflowItem]="entries.play" overflowStay>…</button>
      <button … [sfeOverflowItem]="entries.loop">…</button>  <!-- () => MenuEntry -->
      <button … [sfeOverflowItem]="entries.mute" [overflowPriority]="-1">…</button>
      <button class="…" sfeOverflowMore></button>           <!-- last child -->
  </div>
  ```

  - **Every child of the row is an item** (`overflowStay` for those that never leave), but the "⋯": the measuring adds
    up the items' widths only, so anything else in the row (a spacer) goes outside it. A text that shortens is two
    items: the long part with a lower priority and a null entry (leaves first, no "⋯" for it). `overflowStay` and
    `overflowPriority` are read once, when the item is added.
  - `[overflowIn]="element"`: a row as wide as its content (the selection bar, centred) is measured in a full-width
    element around it; the row's padding and border are then counted as room taken.
  - Measuring and choosing: `@fluentui/priority-overflow` (`createOverflowManager`): the row's `clientWidth` against
    the visible items' `offsetWidth` + the "⋯"'s; a hidden item gets `data-overflowed` (`display: none`); equal
    priorities leave from the right. A `ResizeObserver` on the row and on each item (a label changing width) asks it
    again.
  - The "⋯" (`button[sfeOverflowMore]`, `title="More actions"`) stays in the row unseen while nothing left
    (`.overflow-more:not(.shown)`: absolute, hidden), so its width is known before it shows. It opens the app's one
    menu (`ContextMenu.toggle`, target kind `overflow`) with the entries of the items that left, in the row's order,
    computed when it opens: a phone sheet on phones, keyboard and Esc work ([frontend](frontend.md) rules).
  - `MenuEntry.checked` (a check mark, `menuitemcheckbox`) and `MenuEntry.disabled` (greyed, focusable, not run).
  - Reacts to the row's width, not a breakpoint: phones, narrow desktop windows and small viewers alike.
  - Without `ResizeObserver` (jsdom) nothing leaves; specs stub it and give widths through `data-width`
    (`overflow-row.spec.ts`).

## Step 0 — the shared piece

Ask the developer:
- On/off buttons (Loop, Mute, Cinema) in the menu: a check mark (add `checked?` to `MenuEntry`) or the state in the
  label ("Loop: on")?
- A disabled button (Select all when all are selected): greyed in the menu (add `disabled?`) or left out?

Decided (2026-10-23): on/off buttons show a **check mark** in the menu (`MenuEntry.checked`); a disabled button is
shown **greyed out**, not clickable, its title saying why (`MenuEntry.disabled`).

- [x] Questions above asked and the answers written in Decided.
- [x] `@fluentui/priority-overflow` evaluated and chosen with the developer (2026-10-23): 9.4.3 pinned (MIT,
      framework-free, ~21 kB unminified source, priorities, pinned items, overflow-menu slot); its API is marked
      `@internal` (Fluent's React bindings use it), hence the exact version and the spec. Needs
      `"define": { "process.env.NODE_ENV": "'production'" }` in `angular.json` (it reads it at run time).
- [x] `OverflowRowDirective` (`[sfeOverflowRow]`), `OverflowItemDirective` (`[sfeOverflowItem]`),
      `OverflowMoreComponent` (`button[sfeOverflowMore]`); `MenuEntry.checked` / `.disabled`, `ContextMenu.closeIf`,
      target kind `overflow`; `styles/overflow.css`, menu entry states in `file-management.css`.
- [x] `overflow-row.spec.ts` (8): all fit (no "⋯"); the rightmost leave first with room for the "⋯"; staying
      buttons kept; a lower priority leaves before those on its right; wide again shows everything; buttons coming and
      going; the menu (title, row order, runs the entry); check marks and disabled entries.
- [x] `npm test` passes (1063); `dotnet test` too (1273, run before the rule below).
- [x] Shipped with step 1 (nothing visible on its own: no `CHANGELOG.md` line of its own).

## Step 1 — video player controls

Code: `app/player/video-player.component.ts` (`.player-row`, `minWidth`, `menuEntries()`), `styles/video-player.css`,
`styles/responsive.css`. Today the row wraps onto a second line on phones (`flex-wrap`).

Buttons, left to right: Back N s · Play/Pause · Forward N s · *time* · Speed · Convert (only some videos) · Loop ·
Save frame · Mute · Cinema (viewer only) · Fullscreen.

Default (right to left) would hide Fullscreen first, then Cinema, Mute, Save frame, Loop, Convert, Speed.

Ask the developer: which of these stay (e.g. Play, Back, Forward, Fullscreen)? Keep right to left for the rest, or
another order? Should the time shorten when narrow (`0:12 / 1:05:00` → `0:12`)? Speed in the menu: a submenu with the
rates, or opening the existing speed sheet?

Decided (2026-10-23): **Play, Mute and Fullscreen stay**; the others leave **right to left** (Cinema, Save frame, Loop,
Convert, Speed, Forward, Back); the **time shortens first** (the length leaves alone, no "⋯" for it); **Speed** in the
menu is a **submenu of rates**.

- [x] Questions asked, Decided written.
- [x] `flex-wrap` removed: one row through `OverflowRow` (`.player-row`; the spacer replaced by `margin-right: auto` on
      the last part of the time shown).
- [x] `minWidth` counts only what stays (play, mute, fullscreen, "⋯", the position).
- [x] The speed menu still works from its button; from "⋯" its rates open in place.
- [x] Checked with `agents/tools/visual-check/overflow.mjs`: the viewer, the viewer in fullscreen (the menu shows above
      it), the player page, at 360×800, 390×844, 414×896, 800×360 and 1280×800: one line, nothing cut, the page never
      wider than the screen. Cinema mode switched on was not shot (it only makes the player wider).
- [x] Specs in `video-player.component.spec.ts` (a narrow control row: all fit, the time first, buttons from the
      right with play / mute / fullscreen kept, the menu and its loop check mark, the speed submenu; `minWidth`).
- [x] `docs/user-guide/video-player.md` + `docs/images/player-phone-more.png` (scene `video` of `shots.mjs`),
      `CHANGELOG.md`, [video-player](../features/video-player.md).
- [x] `npm test` passes (1067); no server change.
- [x] Committed on `edge` with step 0; the version comes with the merge into `main`.

## Step 2 — selection bar

Code: `app/library/selection-bar.component.ts`, `styles/selection.css`. Today the buttons on the right are cut off on
phones (labels already hidden below 760 px).

Buttons, left to right: Clear (×) · *count* · Select all · Quick links · Rename · Delete · Move · Copy · Download ·
Tags. Each shows only with its right, so the row's length varies.

Default (right to left) would hide Tags first, then Download, Copy, Move, Delete, Rename, Quick links, Select all.

Ask the developer: which stay (e.g. Clear, Tags)? Keep right to left for the rest? Should the count shorten first
(`3 folders and 12 files selected` → `15 selected`)?

Decided (2026-10-23): **Clear, the count, Select all and Tags stay**; the others leave **right to left** (Download,
Copy, Move, Delete, Rename, Quick links); the **count shortens first** (`15 selected`, no "⋯" for it).

- [x] Questions asked, Decided written.
- [x] Buttons through `OverflowRow`; labels still go first (760 px rule), then buttons. The host is now a full-width
      strip (`.selection-dock`, fixed, clicks pass through it) with the pill (`.selection-bar`) centred in it: a bar
      as wide as its content can't measure its room, so it is measured in the strip (`overflowIn`, new). The count is
      two spans (`.selection-count` priority -1, `.selection-count-short` shown in its place by CSS) and a
      `.visually-hidden` live region with the full wording. Entries match the item menu's (Rename…, Delete…, Move to…).
- [x] `overflow.mjs` scene `selection` (a folder and files, every button): one line, inside the screen at every size;
      at 360 px everything but Clear, count, Select all, Tags in the "⋯"; 414 px keeps Quick links and Rename.
      Few rights (no "⋯"): not shot; the specs cover the bar with every button only.
- [x] Specs in `items.spec.ts` (SelectionBarComponent on a narrow screen: room, the count first, buttons from the right
      with what stays, Download first, the menu's entries).
- [x] `docs/user-guide/tags.md` + `docs/images/selection-bar-phone.png` (scene `tags`), `CHANGELOG.md`,
      [selection-and-keys](../features/selection-and-keys.md).
- [x] `npm test` passes; no server change.
- [x] Committed on `edge`; the version comes with the merge into `main`.

## Step 3 — audit of the other screens

- [ ] `agents/tools/visual-check/overflow.mjs` (documented in the tools README): at the [target sizes](#target-sizes) it opens the
      listing (list and grid), a selection, the viewer (picture, video, text, PDF, EPUB, Markdown, JSON, CSV,
      subtitles), the text editor, the picture editor, the player page, the administration (logs, tasks, settings), and
      reports every element whose content is wider than it (`scrollWidth > clientWidth`) or that sticks out of the
      screen, and rows of buttons taller than one line; a screenshot of each.
- [ ] Results written below, per screen: needs a step (steps 4–8 confirmed or dropped `[-]`, new steps added), or fine.
- [ ] Developer asked whether they have seen other crooked places.

Results: _(empty)_

## Step 4 — viewer bar

Code: `app/library/viewer/viewer.component.ts` (`.viewer-bar`, `.viewer-actions`). Buttons, left to right: *title,
size, tags* · Tags · ⋮ (already a menu) · Close. Probably fine (few buttons, the title shrinks): confirm with the
audit; if it needs work, ask which stay (Close surely) and whether Tags may go into the existing ⋮ rather than a "⋯".

Decided: _(empty: ask first)_

- [ ] Audit result checked; questions asked, Decided written (or `[-]` fine).
- [ ] Fix, specs, visual check, `CHANGELOG.md`, `docs/` if visible.

## Step 5 — document viewers' toolbars

Code: `app/formats/` (`text/text-view`, `text/text-edit`, `pdf/pdf-view`, `epub/epub-view`, `markdown/markdown-view`,
`json/json-view`, `csv/csv-view`, `subtitles/subtitles-view` components), `formats/view-page.component.ts`.
Each toolbar is its own question: list its buttons left to right at the start, with what the default hides first.
May be split into one step per viewer if the audit finds many.

Decided: _(empty: ask first, per toolbar)_

- [ ] Text view / editor: asked, decided, done.
- [ ] PDF: asked, decided, done.
- [ ] EPUB: asked, decided, done.
- [ ] Markdown: asked, decided, done.
- [ ] JSON: asked, decided, done.
- [ ] CSV: asked, decided, done.
- [ ] Subtitles: asked, decided, done.
- [ ] Specs, visual check, `CHANGELOG.md`, `docs/` screenshots.

## Step 6 — picture editor

Code: `app/editor/image-editor.dialog.ts`, `styles/editor.css`. List its toolbar's buttons at the start; ask which stay
(Save and Cancel surely).

Decided: _(empty: ask first)_

- [ ] Audit result checked; asked, Decided written.
- [ ] Fix, specs, visual check, `CHANGELOG.md`, `docs/` screenshots.

## Step 7 — top bar and location bar

Code: `app/library/topbar/topbar.component.ts`, `app/library/location/`, `styles/top-bar.css`, `styles/location.css`.
The top bar already hides some things below 760 px (brand, labels, layout buttons cycle). List the buttons at the start;
ask which stay.

Decided: _(empty: ask first)_

- [ ] Audit result checked; asked, Decided written.
- [ ] Fix, specs, visual check, `CHANGELOG.md`, `docs/` screenshots.

## Step 8 — administration toolbars

Code: `app/logs/logs-section.component.ts`, `app/tasks/task-history.component.ts`,
`app/library/scan/scan-panel.component.ts`, others the audit finds. One question per toolbar.

Decided: _(empty: ask first, per toolbar)_

- [ ] Logs: asked, decided, done.
- [ ] Task history: asked, decided, done.
- [ ] Scan panel: asked, decided, done.
- [ ] Specs, visual check, `CHANGELOG.md`, `docs/` screenshots.

## Step 10 — file names in the list view

Found in step 2 (2026-10-23, `overflow.mjs` scene `selection` at 360×800, list view of `Photos`): a file's name breaks
mid-word over three lines ("Dese / rt road. / png", "Sunri / se.jp / g"): the name column of `.row` is squeezed by
the type, size, tag and ⋮ columns (`responsive.css`: `grid-template-columns: 20px 44px minmax(0, 1fr) 44px 76px
auto`). Not a row of buttons: maybe no "⋯" at all, but a layout change.

Code: `items/file-tile.component.ts` (`FileRowComponent`), `styles/items.css`, `styles/responsive.css`.

Ask the developer: on a phone, what should a list row show and what may go (the type badge, the size, the
resolution, the tag button, the ⋮)? Names on two lines at most, ellipsis after, or whole names wrapped at spaces?

Decided: _(empty: ask first)_

- [ ] Questions asked, Decided written.
- [ ] Fix; specs if the template changes; `overflow.mjs` scene for the list view at the target sizes; screenshots.
- [ ] `docs/` if visible, `CHANGELOG.md`.
- [ ] `npm test` passes.

## Step 9 — finishing

- [ ] `agents/docs/features/overflow-menus.md` (how `OverflowRow` works, the default order, where it is used and what
      each screen keeps), indexed in `AGENTS.md`.
- [ ] Rule bullet in [frontend](frontend.md): a row of buttons that may not fit uses `OverflowRow` (right to left by
      default, the developer says what stays), never `flex-wrap` or cut buttons.
- [ ] This plan removed with its index line and links (`check-links.mjs`).

## Found while building

- **Tests:** only `npm test` for client-only work (the developer, 2026-10-23); `dotnet test` when the server changes.
- The library counts no gaps between items (nor margins: space items with the row's `gap`). A fixed
  `gap × (items − 1)` reserve wasted a gap per item gone (48 px at 360 px in the selection bar: a whole button), so
  `fitGaps` reserves the gaps of the items shown: at a new width it starts from those always there, then grows to the
  items shown, and within one width never shrinks (an item that fits without its gap leaves and stays out; shrinking it
  back looped). Its second pass is a microtask: the library reports nothing when the row didn't change. Specs wait
  with `setTimeout`, not one `Promise.resolve()`.
- A row hidden elsewhere on the page (the selection bar behind the viewer) measures 0 and has its "⋯" `.shown`:
  scripts click the "⋯" inside the row they check.
- The library hides and shows items synchronously while it measures (`onUpdateItemVisibility`): the attribute is set
  on the element there, not through a signal, or it would measure the old layout.
- The initial bundle was already over its 600 kB budget before this epic (816.9 kB at 6621c7a; a warning only).
