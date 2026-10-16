# Administration search

Code: `src/app/admin/admin-search.ts` (`searchAdmin`, `markWords`), the search box and `revealSetting` in
`AdminPageComponent`, `.admin-search*` and `.setting-found` in `styles/admin.css`. User docs:
`docs/administration/README.md#finding-a-setting`.

- Pure client search over `ADMIN_SECTIONS` × `TABS`, no request: only the `sections()` the session sees are searched.
- Every word must be in the text (title, `where`, hint, key, group title/hint, select/preset labels), normalized by
  `normalize` (NFD, marks removed, lower case); results whose title holds every word come first, order kept otherwise.
  The page shows `ADMIN_SEARCH_LIMIT`.
- Results replace the groups in `.admin-nav` while the box has a word; on phones the box is on the list of sections only
  (a section page has no nav). Result links use `linkState()` / `replaceUrl` like the section links.
- The box has its own clear button (`.admin-search-clear`, shown while `query()` has text); the native
  `::-webkit-search-cancel-button` is hidden so there is only one, in every browser.
- Opening a setting sets `reveal`; an effect waits for `form.loaded()` and the sub-page, then scrolls to
  `#setting-<key>`, focuses it and flashes `.setting-found`. A setting outside `SIMPLE_SETTINGS` turns
  `form.showAll` on without writing the preference (as the form does for errors).
- `markWords` maps normalized positions back to letters; the template that renders the parts is on one line so no
  whitespace text nodes split the words (tests compare `textContent`).
- A new setting or section is searchable with no extra work; `admin-search.spec.ts` checks every key of `TABS` is found.
