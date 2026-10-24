# Administration page

Code: `src/app/admin/` (`AdminPageComponent`, `admin-sections.ts`, `leave-admin-page.ts`, System section),
`src/app/settings/`, `src/app/tasks/`, `src/app/access/`; styles `styles/admin.css`. User docs: `docs/administration/`.

- The ⋮ menu keeps only the account line, Scan / Remove from index, the account's other libraries (`config.tenants`;
  switching reloads the page), Administration, Toggle fullscreen and Sign out/in, each by right; everything else lives
  in `AdminPageComponent`.
- Sections × sub-pages from `admin-sections.ts`, filtered by `visibleSections(canAny)` on each sub-page's `rights`;
  none = "Nothing to administer". The open section lists its sub-pages under it in the sidebar, phones show them as
  chips (`role=tab`) instead.
- `/admin` without `section` is the overview (`overview()`: computers, more than one section seen): `.admin-card`s by
  group, each linking to its section and sub-pages; no section is open in the sidebar then. A session seeing one
  section (only My account) gets that section instead; phones get the list of sections.
- Group headings of the overview have `groupId(group)` ids; the breadcrumbs' group links to `/admin#<id>` and an
  effect scrolls to it (no router `anchorScrolling`).
- A sub-page's `description` replaces the section's under the heading and in search results; sections with one
  sub-page keep the section's.
- Moving a sub-page: add `'oldSection/oldPage'` to `ADMIN_MOVED` (an effect replaces the URL) and never remove an
  entry: bookmarks and history keep old addresses. Video streams moved from System to Playback this way.
- Only the active sub-page is rendered, so sections poll from their constructor and stop on destroy.
- Breadcrumbs (`.admin-crumbs` in `.admin-head`: Administration › group › section › sub-page) link with `stayState()`
  (keeps `sfeFromList`); on phones the group is hidden by CSS and Administration is a button running `back()`.
- A settings sub-page shows one tab of `settings-schema.ts` (ids differ: `locateSettings(tab)` finds its section and
  sub-page). The per-browser theme is the `browserTheme` group action of Interface › Appearance, outside the form.
- It is opened with history state `sfeFromLibrary`; section/sub-page changes `replaceUrl` and carry that state, so its
  back arrow is `history.back()` (the listing restores scroll and items) and otherwise navigates to
  `ListingStore.route`. Read the state with `Location.getState()` (tests mock it).
- `SettingsForm` is provided by the page: unsaved changes survive section changes, mark the nav (`changedTabs`,
  `faultyTabs`; on the section, or on its sub-pages while it is open), and `leaveAdminPage` (canDeactivate) +
  `beforeunload` ask before leaving.
- The listing page is created again on return: its first creation restores from `initialHistoryState`, later ones
  from `history.state`.
- Phones (`(max-width: 760px)`, the breakpoint of `admin.css`, read with `matchMedia`): `/admin` without `section` is
  the list of sections (`sectionList`), a section hides it; links from the list push an entry with state
  `sfeFromList` (kept by `go`), so back (arrow or browser) returns to the list.
- `TagManagerDialog` stays on the listing page (sidebar, tag dialogs) around the same `TagListComponent`.
- Tests: `adminPageProviders(api)` + `RouterTestingHarness` ([testing.md](../development/testing.md)).
- Phones: a section's sub-page chips (`.admin-chips`) are one line scrolling sideways; `[sfeChipInView]` (with
  `showChip`) scrolls the selected one into view after each render ([overflow-menus](overflow-menus.md)).
