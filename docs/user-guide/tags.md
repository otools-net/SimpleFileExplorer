# Tags

Tags label files so you can find them again: `t:beach` in the [search box](search.md) finds every file tagged
**Beach**. Tags follow [TagStudio](https://github.com/TagStudioDev/TagStudio)'s model.

## How tags work

- A tag has a **name** (not necessarily unique), an optional **shorthand** and **aliases**, all searchable: `t:lol`
  finds files tagged "League of Legends" if "LoL" is its shorthand. Double quotes in a tag name are ignored when
  searching: `t:"12 Vinyl"` finds the tag `12" Vinyl`.
- **Parent tags:** a file tagged `Shrek` (parent `Character`) is found by `t:character` too, through any number of
  levels. Loops are refused.
- **Disambiguation:** a parent can be shown after the name, e.g. "Freddy (FNAF)".
- **Category** tags group the tags inheriting from them in a file's tag list.
- **Hidden** tags keep their files out of search results unless the search names the tag (or a parent).
- New spaces start with **Favorite** and **Archived** (hidden), under **Meta Tags**. They can be edited, not deleted.
- Colors come from TagStudio's standard palette.

Tags are created, edited and deleted in [Administration › Tags](../administration/tags.md) (also **Manage tags** under
the tag sidebar, and in each tag dialog), by accounts with the right `tags.manage`. Tagging files needs `tags.assign`.

## One file

![The tags of a file](../images/file-tags-dialog.png)

The tag button on a file (on hover with a mouse; or <kbd>T</kbd> in the [viewer](viewer.md)) opens its tags: type to
add an existing tag or create one, × removes one. Click a tag on a file to edit it, a tag chip in the listing to search
for it. Tagging a file that isn't indexed yet indexes it first.

## Several files at once

![Three files selected](../images/selection-bar.png)

Tick their checkboxes (top-left of a tile, first column of the list). Shift-click selects a range,
<kbd>Ctrl</kbd>+<kbd>A</kbd> everything loaded, <kbd>Esc</kbd> clears. While files are selected, clicking a file
selects it. The bar at the bottom says how many are selected, with **Select all**, **Quick links** (adds them to your
[quick links](browsing.md#quick-links)) and **Tags**.

![Tags of several files](../images/files-tags-dialog.png)

**Tags** lists the tags on all of them and on some (with how many): adding a tag puts it on every file, clicking a
partial tag completes it, × removes it from all. The selection is cleared when you open another folder or search.

## A whole folder, or every search result

![Tags of every file of a folder](../images/scope-tags-dialog.png)

The tag button next to the location (right of the breadcrumbs) opens the tags of every file of the folder you are in,
or of every result of the current search (all of them, not only the ones loaded). The tag button on a folder (shown on
hover; always on touch screens) does the same for that folder.

- For a folder, **Include sub-folders** extends it to every file below it.
- The dialog counts the files and shows the tags on all of them and on some; each change (adding a tag, completing a
  partial one, removing one) says how many files it touches and waits for you to confirm it.
- A folder is indexed first, so its files not indexed yet are tagged too (this can take a while for a large tree, and
  is refused while a scan runs); search results are the indexed files the search finds.

## The tag sidebar

The sidebar at the left of the library page (a drawer on phones; the button at the top left shows or hides it) shows a
few tags in each section, with their number of files. Click a tag to search for it.

| Section | |
|---|---|
| **Most popular** | The tags with the most files |
| **Last searched** | Tags searched with `t:` |
| **Last viewed** | Tags of the files opened lately |
| **I feel lucky** | Random tags; the ↻ button shows others |

The history is shared by everyone using the space; it is turned off or cleared in
[Administration › Tags › Settings](../administration/tags.md#settings).

## When files move

A file's tags survive rescans while it keeps its path, and follow it when it is moved or renamed: see
[Moved files](../administration/duplicates-and-moves.md#moved-files).
