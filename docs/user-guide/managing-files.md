# Managing files

You can create folders, rename and delete files and folders from the listing, if your groups have the rights
`files.createFolders`, `files.rename`, `files.delete` (to the trash) and `files.deletePermanently`
([Rights](../reference/rights.md)). No group has them until an administrator gives them. Folder access rules also apply: in a read-only folder these actions are not offered, and in a folder you
can't see they don't exist.

Moving, copying, uploading and downloading several files at once will come in later versions.

## The item menu

Right-click a file or folder for its menu, or use its **⋮** button (it shows when the pointer is over the item, and
always on phones and tablets; on Android, pressing and holding the item opens the menu too). The menu offers what you
may do with that item:

| Entry | Key | |
|---|---|---|
| **Rename…** | F2 | Gives it another name (below) |
| **Delete…** | Delete | Moves it to the trash ([below](#delete)); with items selected, all of them |
| **Tags…** / **Tag its files…** | | The tags of a file; adding tags to every file of a folder ([Tags](tags.md)) |
| **Copy full path** | | Where it is on the server, when [copying paths](browsing.md) is on |

![The menu of a folder](../images/item-menu.png)

Arrow keys move through the menu and Esc closes it. To get the browser's own menu (to open a link in a new tab, for
example), hold **Shift** while you right-click. Without the rights above there is no item menu: a right click opens the
browser's menu as before.

## New folder

The **New folder** button next to the folder's name (or **Shift+N**) asks for the name of a folder to create in the
folder you are viewing. In a search there is no New folder button.

![New folder](../images/new-folder-dialog.png)

## Rename

**Rename…** in the item menu, or **F2** with one file selected (or the item focused with the keyboard), asks for the new
name. The name is selected up to its extension, so typing replaces the name but keeps `.jpg`. The item keeps its place
in the listing until you reload it, and is highlighted for a moment.

![Renaming a folder](../images/rename-dialog.png)

What SFE knows about a file or folder goes with it to the new name: its tags, thumbnails and trickplay, and for a
folder, its folder access rules, quick links, scheduled scans and the settings that name it (excluded folders, trickplay
folders, the folder for edited pictures).

On Windows servers you can change only the case of a name (`photo.jpg` to `Photo.jpg`).

## Delete

**Delete…** in the item menu, **Delete** in the selection bar, or the <kbd>Delete</kbd> key (for the selection, or the
item with the focus) asks first, naming what goes and its size, then moves it to the space's **trash**. A message in
the corner offers **Undo** for a few seconds, which puts everything back.

![Deleting a folder and a file](../images/delete-dialog.png)

<kbd>Shift</kbd>+<kbd>Delete</kbd> deletes for good, skipping the trash, for those with the right
`files.deletePermanently`. A space can also **always delete permanently**
([File management](../administration/file-management.md)): then every delete is for good, and only those with that
right can delete.

- A folder goes with everything in it; you can delete a folder only if you may delete everywhere inside it.
- A link (a shortcut or junction to another folder) is deleted as a link: what it leads to stays.
- A folder holding links that sits on another drive than the trash can't go to the trash: SFE says so and offers to
  delete it permanently instead.

## The trash

**Administration › File management › Trash** lists what was deleted to the trash: what you deleted, and what was deleted
in folders where you may delete. **Restore** puts an item back where it was, with the tags of its files and a folder's
access rules (its folder is made again if it is gone; a number is added to its name if that is taken there); a restored
folder is scanned again. **Delete** and **Empty the trash** delete for good (right `files.deletePermanently`).

![The trash](../images/admin-files-trash.png)

What stays in the trash longer than the space's **Days in the trash** (30 by default) is deleted for good by the
**Trash purge** task ([Background tasks](../administration/background-tasks.md)).

## Names that can't be used

SFE refuses a name, and says why, when it:

- is already used by a file or folder in that place (even one you can't see): nothing is ever replaced;
- contains `/`, or is `.` or `..`;
- contains a character Windows refuses (`< > : " \ | ? *`), ends with a dot or a space, or is a device name such as
  `CON`, `NUL`, `COM1`. A Linux server accepts these when the space's setting
  [Only names Windows accepts](../administration/file-management.md) is off;
- is longer than 255 characters (255 bytes on Linux), or would make the full paths of files inside a folder too long
  for the index;
- is `.sfe` at the library's root, which SFE keeps for its own work files.

A name starting with a dot (`.config`) is a hidden item: the listing shows it only when hidden items are shown
([Scanning › What to scan](../administration/scanning.md#what-to-scan)).

## When it can't be done

- **A scan is running** over that folder: try again when the scan has finished.
- **The file is in use**: another program has it open, or a video is playing. Close it and try again.
- **The server's account may not change files there**: the server was installed with read-only access to the library
  ([Installation](../installation.md) says how to give it write access).
- **Folder access rules are set for a folder of that name** (rules left from a folder that was deleted or moved outside
  SFE): an administrator can remove them or point them elsewhere in
  [Folder access](../administration/spaces-and-accounts.md#folder-access).
- A folder can be renamed only by someone who may rename **everywhere inside it**: a sub-folder you can't see, or that
  is read-only for you, keeps you from renaming the folder holding it.
