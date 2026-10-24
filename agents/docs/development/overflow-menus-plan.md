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
| [3](#step-3--audit-of-the-other-screens) | Audit of the other screens (finds the next steps) | `[x]` done (tools and plan only: no version) |
| [4](#step-4--viewer-bar) | Viewer bar | `[-]` fine at every size (audit) |
| [5](#step-5--document-viewers-toolbars) | Document viewers' toolbars (text, PDF, EPUB, Markdown, JSON, CSV, subtitles) | `[x]` committed (version at the merge into main) |
| [6](#step-6--picture-editor) | Picture editor | `[-]` fine at every size (audit) |
| [7](#step-7--top-bar-and-location-bar) | Location bar: the folder actions (the top bar is fine) | `[x]` committed (version at the merge into main) |
| [8](#step-8--administration-toolbars) | Administration tabs (logs and tasks toolbars are fine) | `[x]` committed (version at the merge into main) |
| [10](#step-10--file-names-in-the-list-view) | File names in the list view, buttons cut in landscape (found in step 2) | `[ ]` decided, to build |
| [11](#step-11--moved-files-on-a-phone) | Moved files (missing files): buttons over the file's text on phones (found in step 8) | `[ ]` decided, to build |
| [12](#step-12--long-lines-in-the-text-viewer) | Text viewer: wrap long lines, or scroll sideways when off (asked by the developer) | `[ ]` |
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

- [x] `agents/tools/visual-check/narrow-audit.mjs` (documented in the tools README; a script of its own: `overflow.mjs`
      checks the rows already done): 31 scenes (listing views, menus, dialogs, each viewer, the editors, the player and
      view pages, administration pages) at the phone sizes; reports elements off the screen, buttons cut off by a box
      that doesn't scroll, rows of 3+ buttons on several lines, the page wider than the screen. ~12 minutes.
- [x] Results written below (2026-10-23, at ae2ce24): steps 4 and 6 dropped, 5, 7, 8 and 10 confirmed.
- [x] Developer asked (2026-10-23): **every toolbar goes on one line** with the "⋯", also those that only wrap
      (nothing cut); no other crooked places seen.

Results (360×800, 390×844, 414×896, 800×360):

| Screen | Found | Step |
|---|---|---|
| Text viewer | "Aa" (reading settings) off the screen, every size | 5 |
| Text editor | toolbar on 2 lines; "Aa" cut in half at 360 | 5 |
| CSV | toolbar on 3 lines at 360 ("Aa" alone), 2 at 390 / 414. The table's columns off screen: it scrolls sideways, fine | 5 |
| JSON, subtitles, PDF, Markdown on the `view?path=` page | toolbar on 2 lines, nothing cut | 5 |
| Folder actions under the breadcrumbs (`.location-head`) | on 2 lines, nothing cut (portrait) | 7 |
| Administration tabs (`.settings-tabs`: Duplicates, Tasks) | on 2 lines, nothing cut | 8 |
| List view, landscape 800×360 | names one letter wide, tag and ⋮ cut off (the sidebar shows above 760 px) | 10 |
| Viewer bar, picture editor, top bar, menus, dialogs (properties, folder tags), EPUB, Markdown in the viewer, logs, accounts, settings | nothing | 4, 6 dropped |
| Again 2026-10-24, every administration page added (33) and a check for **buttons over text** (by-design overlays left out: menus, fixed / sticky bars, the viewer's arrows, text clipped by its scrolling box) | Moved files: Choose file… and Forget over the file's text, every size | 11 |

## Step 4 — viewer bar

Code: `app/library/viewer/viewer.component.ts` (`.viewer-bar`, `.viewer-actions`). Buttons, left to right: *title,
size, tags* · Tags · ⋮ (already a menu) · Close. Probably fine (few buttons, the title shrinks): confirm with the
audit; if it needs work, ask which stay (Close surely) and whether Tags may go into the existing ⋮ rather than a "⋯".

Decided: _(empty: ask first)_

- [-] Audit: nothing wrong at any size (2026-10-23).

## Step 5 — document viewers' toolbars

Code: `app/formats/` (`text/text-view`, `text/text-edit`, `pdf/pdf-view`, `epub/epub-view`, `markdown/markdown-view`,
`json/json-view`, `csv/csv-view`, `subtitles/subtitles-view` components), `formats/view-page.component.ts`.
Each toolbar is its own question: list its buttons left to right at the start, with what the default hides first.
May be split into one step per viewer if the audit finds many.

Decided (2026-10-23, one rule for the document toolbars): the **editing actions** (Edit; Save, Save a copy,
Download, Done) and the **fields** (filters, the subtitles' shift with its button, PDF page number and find) **stay**,
the fields narrower on phones; everything else leaves **right to left** (Aa first, then Find, Line numbers, the
format's own buttons, the view switch). **PDF:** page navigation (Previous, page, Next) and **zoom** (Smaller, list,
Larger) stay with the find field; Dark pages then Outline leave. EPUB had nothing wrong: unchanged.
Then, as what stays didn't fit 360 px (asked again): in the **text editor** Save a copy and Done show **only their
icon** on phones (`.text-action-label` hidden ≤ 640 px; 309 px of editing buttons otherwise); in the **PDF** the
**find field becomes a magnifier** on phones (`.pdf-find-toggle`: the field on a line under the bar, absolute so the
item's width is the button's; Ctrl+F opens it, Esc on an empty field hides it) and the **zoom list leaves** (a submenu
of sizes; Smaller and Larger stay). An estimate given to the developer was wrong (find alone freed too little): measure
with `overflow.mjs` before offering choices.

- [x] Text view / editor: `more` entries (parts of a large file as a submenu, Follow, Wrap, Line numbers, Find, Go to
      line, Aa); `sfe-text-edit-actions` one staying item.
- [x] PDF: pages and Smaller / Larger stay, find a magnifier on phones, zoom list / Outline / Dark pages leave.
- [-] EPUB: nothing wrong (audit); unchanged.
- [x] Markdown, JSON, CSV (Header checked, Separator a submenu), subtitles (the shift field and its button stay).
- [x] Shared entries in `formats/toolbar-entries.ts` (view switch as a submenu, Line numbers, Find, Aa). Margins
      after groups made padding (`.text-edit-actions`, `.text-parts`; the view switch's dropped): margins aren't
      measured. Fields 110 px on phones.
- [x] Found and fixed on the way: `.text-view` had no column size, so a long line (a log) made the whole viewer, its
      toolbar too, wider than the screen ("Aa" off screen): `grid-template-columns: minmax(0, 1fr)`. And
      `OverflowRow` ignored the row's own padding (in its `clientWidth`): now counted.
- [x] Specs: `toolbar-entries.spec.ts`, CSV on a narrow screen (from the right, filter stays, Header / Separator /
      Aa entries), PDF magnifier and zoom submenu; Ctrl+F test waits for the field to show.
- [x] `overflow.mjs` scenes `text-viewer`, `text-editor`, `json-viewer`, `csv-viewer`, `subtitles-viewer`,
      `pdf-viewer`, `markdown-viewer`, `markdown-page`: one line, inside the screen at every size.
- [x] `docs/user-guide/text-files.md` + `text-viewer-phone-more.png` (a JSON file: a log's bar fits at 390 px),
      the text viewer scene's shots again; `CHANGELOG.md` (Changed, and Fixed for the "Aa").
- [x] `npm test` passes; no server change.
- [x] Committed on `edge` with step 3; the version comes with the merge into `main`.

## Step 6 — picture editor

Code: `app/editor/image-editor.dialog.ts`, `styles/editor.css`. List its toolbar's buttons at the start; ask which stay
(Save and Cancel surely).

- [-] Audit: nothing wrong at any size (2026-10-23; the file name in its bar is shortened, the buttons fit).

## Step 7 — top bar and location bar

Code: `app/library/topbar/topbar.component.ts`, `app/library/location/`, `styles/top-bar.css`, `styles/location.css`.
The top bar already hides some things below 760 px (brand, labels, layout buttons cycle). List the buttons at the start;
ask which stay.

Decided (2026-10-24, shown as mockups): on a phone **one "⋯" for all the folder's actions**, the pin included
("everything can go there"); the breadcrumbs keep the whole line. Not an overflow row: all or nothing below 640 px.
The "⋯" opens the **folder's own menu** (`FolderMenu.target()`, the listing's right-click one): it already holds
every action and more (Paste, Select all, Refresh, Properties), which phones couldn't reach without a right click.

- [x] Asked, Decided written. The top bar: nothing wrong (audit).
- [x] `location.component.ts`: the pin and the action buttons get `.location-action`, a `.location-more` button
      (horizontal dots, "Actions on this folder" / "on this search") toggles `ContextMenu` with `FolderMenu.target()`;
      `location.css`: below 640 px the actions hide and the "⋯" shows (above, hidden: nothing changes on a computer).
- [x] Spec in `topbar.spec.ts` (LocationComponent: the "⋯" opens and closes the folder's menu, a search's title).
- [x] `narrow-audit.mjs` listing scenes: nothing found any more (the actions' 2nd and 3rd lines are gone).
- [x] `docs/user-guide/browsing.md` (On a phone) + `phone-folder-menu.png` (scene `phone`), `CHANGELOG.md`,
      [location-bar](../features/location-bar.md).
- [x] `npm test` passes; no server change.
- [x] Committed on `edge`; the version comes with the merge into `main`.

## Step 8 — administration toolbars

Code: `app/admin/admin-page.component.ts` (`.admin-chips`, `ChipInViewDirective`, `showChip`), `styles/admin.css`.
The audit found only the sub-page chips of a section on phones wrapping onto 2 lines (Duplicates & moves: 4 chips);
the logs, tasks and scan panel toolbars are fine.

Decided (2026-10-24): the chips are **one line that scrolls sideways** (not a "⋯": they are navigation).

- [x] Asked, Decided written.
- [x] `.admin-chips` (≤ 640 px): `nowrap`, `overflow-x: auto`, edge to edge (`margin: 0 -12px`), no scrollbar; a chip
      cut at the edge shows there are more. `[sfeChipInView]` on the strip scrolls the selected chip into view (12 px
      room) after each render: an effect in the page's constructor ran before the chips existed (the section loads
      first) and never scrolled.
- [x] Spec: `showChip` (scrolls right, left, or not at all) in `admin-page.spec.ts`; `narrow-audit.mjs` scene
      `admin-tracking-settings` (the last chip): nothing found, the strip scrolled to it (checked in the screenshot).
- [x] `docs/administration/README.md` (On a phone), `CHANGELOG.md`.
- [x] `npm test` passes; no server change.
- [x] Committed on `edge`; the version comes with the merge into `main`.

## Step 10 — file names in the list view

Found in step 2 (2026-10-23, `overflow.mjs` scene `selection` at 360×800, list view of `Photos`): a file's name breaks
mid-word over three lines ("Dese / rt road. / png", "Sunri / se.jp / g"): the name column of `.row` is squeezed by
the type, size, tag and ⋮ columns (`responsive.css`: `grid-template-columns: 20px 44px minmax(0, 1fr) 44px 76px
auto`). Not a row of buttons: maybe no "⋯" at all, but a layout change.

Code: `items/file-tile.component.ts` (`FileRowComponent`), `styles/items.css`, `styles/responsive.css`.

Ask the developer: on a phone, what should a list row show and what may go (the type badge, the size, the
resolution, the tag button, the ⋮)? Names on two lines at most, ellipsis after, or whole names wrapped at spaces?

Decided (2026-10-24, shown as mockups): when the **list itself is narrow** (a phone, and landscape with the sidebar:
a container query on the list, not a screen breakpoint), a row has **two lines**: the name on the whole first line,
type · size · resolution under it in small text (then the tags); picture on the left, tag and ⋮ on the right.

- [x] Questions asked, Decided written.
- [ ] Fix; specs if the template changes; `overflow.mjs` scene for the list view at the target sizes; screenshots.
- [ ] `docs/` if visible, `CHANGELOG.md`.
- [ ] `npm test` passes.

## Step 11 — moved files on a phone

Found in step 8 (2026-10-24, `docs/images/phone-admin-section.png`, 390×844, Duplicates & moves › Moved files): a
missing file's **Choose file…** and **Forget** buttons are drawn over its text ("Was in Photos/2025 I… · ce 23"):
the row's buttons don't leave room for the path and the date. `narrow-audit.mjs` doesn't see overlaps (only things off
screen, cut or wrapped): check the screenshots too.

Code: `app/tracking/` (missing files panel, `tracking-parts.ts`), `styles/` (tracking rows).

Ask the developer: on a phone, the buttons under the file's text (a line of their own), or in a "⋯"?

Decided (2026-10-24, shown as mockups): when the row is narrow, **Choose file… and Forget go on a line below** the
file's text, right-aligned; on a computer, as today when there is room.

- [x] Questions asked, Decided written.
- [ ] Fix; specs if the template changes; screenshots at the target sizes.
- [ ] `docs/` if visible, `CHANGELOG.md`.
- [ ] `npm test` passes.

## Step 12 — long lines in the text viewer

Asked by the developer (2026-10-24): in the text viewer a line too long for the screen is **cut off**. Wanted: an
option to **wrap long lines** and, when it is off, **scrolling sideways** to read the rest of the line.

Known before starting: the toolbar already has **Wrap long lines** (`own().wrap`, a reading preference per profile;
CodeMirror's `lineWrapping`), and step 5 gave `.text-view` `grid-template-columns: minmax(0, 1fr)` (before, a long
line widened the whole viewer). To check first, on a phone and a computer, every reader (text, log, code, Markdown and
JSON sources, the editor): is the line cut with wrapping off (no horizontal scroll in `.cm-scroller`), is the option
easy to find, does a swipe sideways reach the end of the line (and not go to the next file: the viewer's swipe)?

Code: `app/formats/text/codemirror.ts`, `text-view.component.ts`, `app/formats/reading/`, `styles/reader.css`,
`library/viewer/viewer.component.ts` (swipes).

Ask the developer: wrapping on by default on phones? Where the option lives (the toolbar's button, the "Aa" panel, both)?
A sideways swipe on the text: scroll the line, and change files only from the arrows / the swipe outside the text?

Decided: _(empty: ask first)_

- [ ] Checked how it behaves now (screenshots at the target sizes and on a computer); questions asked, Decided written.
- [ ] Fix: wrapping option, horizontal scroll when off; specs.
- [ ] `docs/user-guide/text-files.md`, `CHANGELOG.md`.
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
