# Tags (TagStudio model)

Code: `src/Sfe.Core/Tagging/` (`TagService`, `TagGraph` (parent inheritance), built-in tags, colors), entities in
`Data/Entities/Tags/` (`TagDefinitions` table); client `src/app/tags/` (`TagPickerComponent`, `TagListComponent`,
`TagManagerDialog`, `FilesTagsDialog`). Tag model: https://github.com/TagStudioDev/TagStudio. Tagging a whole folder or
search: [scope-tagging.md](scope-tagging.md).

- The multi-file tags dialog (`FilesTagsDialog`) sends changes in parts of `config.maxFilesPerTagChange`.
- `TagManagerDialog` stays on the listing page (sidebar, tag dialogs) around the same `TagListComponent` as the admin
  page.
- Tag counts are cached briefly ([scale.md](../development/scale.md)).
- Browse attaches tags in `TagService.AttachIndexDataAsync` (with resolution, duration, `Indexed`).
- Files with a hidden tag stay hidden in search unless the search names it or an ancestor
  ([search-terms.md](search-terms.md)).
- `TagPickerComponent` keeps a module-level "pointer pressed" flag: a test sending `pointerdown` must send `pointerup`
  too. A dialog that focuses its picker on open needs `blur()` + `focus()` to load suggestions again.
- Tagged files that disappear become missing files: [missing-files.md](missing-files.md).
