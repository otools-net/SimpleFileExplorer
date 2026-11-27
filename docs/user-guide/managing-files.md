# Managing files

You can create folders and empty files, rename, move, copy and delete files and folders from the listing, if your
groups have the rights `files.createFolders`, `files.createFiles`, `files.rename`, `files.move`, `files.copy`,
`files.upload`, `files.delete` (to the trash) and `files.deletePermanently` ([Rights](../reference/rights.md)). No group has them until an administrator gives them.
Folder access rules also apply: in a read-only folder these actions are not offered, and in a folder you can't see
they don't exist.

Downloading several files or a whole folder as one zip file needs only the right to download (`files.download`),
which members have ([below](#download-several-files-or-a-folder)).

## The item menu

Right-click a file or folder for its menu, or use its **⋮** button (it shows when the pointer is over the item, and
always on phones and tablets). With the keyboard, press the **menu key** (or **Shift+F10**) on the item that has the
focus. On a phone the menu opens at the bottom of the screen, under the item's name, and pressing and holding an item
**selects** it instead ([On phones](#on-phones)). The menu offers what can be done with that item, in groups kept
apart by a line, in the order of a desktop file browser:

| Entry | Key | |
|---|---|---|
| **Open** | | A folder: lists it; a file: shows it in the viewer |
| **Open in new tab** | | The folder, or the file (its viewer page for text and documents), in a tab of its own |
| **Open containing folder** | | In search results: lists the folder the item is in, and shows it there |
| **Open as ▸** | | **Open as text** for files the viewer doesn't show ([Text files](text-files.md#other-files-open-as-text)), **Open as archive** for books, comics and office documents ([Archives](archives.md)); shown alone when only one applies |
| **Edit** | | Text files: opens it in the viewer, ready to be changed ([Editing text files](editing-text.md)) |
| **Crop…** | | Pictures: opens it in the [picture editor](picture-editor.md) (right `files.edit`) |
| **Cut**, **Copy** | Ctrl+X, Ctrl+C | Keeps it (or the selection) to move or copy with **Paste** ([below](#move-and-copy)) |
| **Paste into …** | | On a folder, after Cut or Copy: puts what was kept into it |
| **Rename…** | F2 | Gives it another name; several selected: [renamed together](#rename-several-at-once) |
| **Move to…**, **Copy to…** | | Moves or copies it (or the selection) into a folder you pick |
| **Tags…** / **Tag its files…** | | The tags of a file (or of the files selected); adding tags to every file of a folder ([Tags](tags.md)) |
| **Add to quick links** / **Remove from quick links** | | Keeps it (or the selection) in your [quick links](browsing.md#quick-links), in the sidebar |
| **Scans and previews…** | | A folder kept out of scans, trickplay or thumbnails ([Scanning](scanning.md)), for administrators |
| **Folder access…** | | A library folder's [folder rules](../administration/spaces-and-accounts.md#folder-access): who may see it and change its files, a new rule started on it; for those who manage the space |
| **Archive ▸** | | **Extract to “name”**, **Extract each here**, **Extract…** for archives ([Archives](archives.md#extracting)) and **Compress…** ([below](#compress-into-a-zip)); **Compress…** alone for anything else |
| **Download** / **Download as zip** | | Saves the file to your device; a folder, or several items selected, as one zip ([below](#download-several-files-or-a-folder)) |
| **Copy full path** | | Where it is on the server, when [copying paths](browsing.md#copying-full-paths) is on |
| **Delete…** | Delete | Moves it to the trash ([below](#delete)); with items selected, all of them |
| **Previous versions…** | | The versions of a file kept when it was saved over: open one, or restore it ([below](#previous-versions)) |
| **Properties** | Alt+Enter | Its size, dates and what SFE knows of it; a folder's size; a file's checksum ([below](#properties)) |

What doesn't apply to the item isn't shown (no **Crop…** on a video, no **Extract…** on a picture). What your rights
don't let you do here is **greyed out**: its hint says why ("You may not delete “beach.jpg”."), and on a phone the
reason is written under it.

![The menu of a folder](../images/item-menu.png)

Arrow keys move through the menu and Esc closes it; in a submenu (**▸**), **Back** or the Left arrow goes back. With
several items selected, the menu of one of them acts on all of them (and has no Open entries). Everyone who may browse
has the menu, with **Properties** at least. The selection bar's buttons are the same actions, with the same hints.

### Other menus

Right-clicking elsewhere on the page also opens SFE's menu, for what is under the pointer:

- **The page itself** (an empty part of the listing, the counts, the buttons): the folder you are viewing. Refresh,
  Open in new tab, New folder, New file, Upload, Paste, Select all, its tags, quick link, Copy full path,
  Properties. In a search: Search again, Open in new tab, Select all, Tag the results, Search in, Copy link.
- **A folder's name in the path** at the top, the **up / previous / next** buttons, and the folder shown under a
  search result: Open, Open in new tab, Paste into, quick link, Copy full path, Properties.
- **A quick link** in the sidebar: the same for its folder or file, then Move up, Move down and Remove.
- **A recent search** suggested by the search box: Search, Open in new tab, Remove from the history.
- **Any other link** of the app (in the ⋮ menu, in the administration): Open, Open in new tab, Copy link address.
- **The administration page** elsewhere: Back, Reload, Open in new tab, Copy page link.

![The menu of the folder listed](../images/background-menu.png)

In text boxes, and on text you have selected, a right click still gives the browser's own menu (to paste, or copy
the text). Hold **Shift** while you right-click to get the browser's own menu anywhere.

In windows over the page (properties, the picture editor, the viewer's bar) a right click opens no menu, except on a
link. Documents in the viewer (text, PDF, books, tables…) keep their own right click.

## New folder

The **New folder** button next to the folder's name (or **Shift+N**) asks for the name of a folder to create in the
folder you are viewing. In a search there is no New folder button.

![New folder](../images/new-folder-dialog.png)

## New file

The **New file** button next to it asks for the name of an **empty** file to create there (`New file.txt` to start
with; the name is selected up to its extension). It needs the right `files.createFiles`. A text file (`.txt`, `.md`,
`.json`…) then opens in the viewer, ready to be written: see [Editing text files](editing-text.md).

## Rename

**Rename…** in the item menu, or **F2** with one file selected (or the item focused with the keyboard), asks for the new
name. The name is selected up to its extension, so typing replaces the name but keeps `.jpg`. The item keeps its place
in the listing until you reload it, and is highlighted for a moment.

![Renaming a folder](../images/rename-dialog.png)

What SFE knows about a file or folder goes with it to the new name: its tags, thumbnails and trickplay, and for a
folder, its folder access rules, quick links, scheduled scans and the settings that name it (excluded folders, trickplay
folders, the folder for edited pictures).

On Windows servers you can change only the case of a name (`photo.jpg` to `Photo.jpg`).

A message in the corner offers **Undo** for a few seconds, which gives the item its old name back (unless something
took that name meanwhile).

## Rename several at once

With several files or folders of one folder selected, **Rename** in the selection bar (or **Rename…** in the menu, or
**F2**) renames them together:

- **Find and replace**: a text in their names replaced by another (**Match case** to tell `IMG` from `img`);
- **Numbered**: a new name for each, `#` standing for the number (`Beach trip ##` gives `Beach trip 01`,
  `Beach trip 02`…), counting from the number you choose, in the listing's order.

Extensions stay. The preview shows every new name before anything changes; names that can't be given are shown in red
and left out: empty, holding `/`, given to two items, already taken by another item of the folder, or the current name
of another item renamed (rename in two steps then). **Undo**, in the message afterwards, gives them all their names back.

![Renaming several files at once](../images/batch-rename-dialog.png)

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

**Administration › File management › Trash**, also opened by **Trash** at the bottom of the library's sidebar (its icon
shows whether the trash is empty, and a number how many items you see in it), lists what was deleted to the trash: what
you deleted, and what was deleted in folders where you may delete. **Restore** puts an item back where it was, with the
tags of its files and a folder's access rules (its folder is made again if it is gone; a number is added to its name if
that is taken there); a restored folder is scanned again. **Delete** and **Empty the trash** delete for good (right
`files.deletePermanently`).

![The bottom of the sidebar: the space used and free, and the Trash link with two items in the trash](../images/sidebar-trash.png)

![The trash](../images/admin-files-trash.png)

What stays in the trash longer than the space's **Days in the trash** (30 by default) is deleted for good by the
**Trash purge** task ([Background tasks](../administration/background-tasks.md)).

## Previous versions

When a file is saved over in SFE (the [text editor](editing-text.md), the [picture editor](picture-editor.md)), the
content it had is kept in the trash, marked **previous version**. **Previous versions…** in the menu of a file (or
**Show…** next to **Previous versions** in its [Properties](#properties)) lists them, newest first: when the file was
saved over, by whom, and the size of the version.

![The previous versions of a text file](../images/previous-versions.png)

- **Open** shows the version in a new tab (to download it, the right `files.download`).
- **Restore a copy** copies it next to the file, with a number added to its name (`notes (2).txt`); the version
  stays. You need the right to create files in the folder (`files.createFiles`).
- **Restore** puts it in place of the file, after asking: the file as it is now becomes a version in turn, so nothing
  is lost. The file keeps its tags. You need the rights of saving over the file (as for the editors); not in a space
  that always deletes for good.

Anyone who may open the file sees its versions. They follow the file when it is renamed or moved in SFE, and go
with the trash's purge, like everything in it. A file of a connection has none: the storage service keeps its own.

## Move and copy

There are several ways to move or copy files and folders, as in a file browser on your computer:

- **Move to…** or **Copy to…** in the item menu, or **Move** or **Copy** in the selection bar, opens a folder picker:
  click through the folders (the names at the top go back up), then **Move here** or **Copy here**. Folders where you
  may not move or copy are refused, and a folder can't go into itself.
- **Cut** (Ctrl+X) or **Copy** (Ctrl+C) keeps the selection, or the item with the keyboard focus; open the folder
  they should go to and press **Ctrl+V**, or use **Paste here** in the panel at the bottom right, or **Paste into …**
  in a folder's menu. Cut items look faded until they are pasted. What was copied can be pasted again elsewhere.
- **Drag** files and folders (all the selected ones when you drag one of them) onto a folder of the listing, or onto a
  folder of the location bar above it: they are moved there. Hold **Ctrl** when you drop them to copy them instead.

![Picking the folder to move to](../images/move-dialog.png)

Moves and copies run on the server in the background: you can go on browsing. A panel at the bottom right shows each
one with its progress; **Cancel** stops it (what is done stays, nothing half copied does). When it is over, the panel
says what was done and what was not, and why; close it with ×. A right click on it gives the same (Cancel, or Close)
and opens the folder it puts things into, also in a new tab. After a move, a message offers **Undo** for a few
seconds: the items go back into the folders they came from. After a copy into the library, **Undo** moves the copies to
the trash (see [Undo](#undo)).

When the folder already holds an item with the same name, the panel asks, showing the size and date of both:
**Replace** (the one there goes to the trash: you need the right to delete there), **Skip** (leave this one out) or
**Keep both** (a number is added to the new one's name). Tick **Do the same for the next ones** to answer once for the
rest. A copy into the folder it is in always keeps both.

![A copy asking about a name already taken](../images/operations-panel.png)

When a folder meets a folder of the same name, the panel also offers **Merge**: what the folder holds goes into the one
there, and SFE asks about each name they share in the same way (a file: replace, skip or keep both; a folder: merge it
too, and so on down). Nothing else in the folder there changes. Answers given for all are kept apart for folders and
for files: replacing every file never replaces a whole folder without asking. After a merge:

- a **moved** folder is gone once everything it held went in; what was skipped stays where it was, in its folder;
- the panel says how many items were left out and why;
- **Undo** doesn't move a merged folder back (it would take what the folder there held before).

Merge is not offered when the folder there holds the one coming, when either is a link to another folder, or where you
may not move, copy or extract into the folder there. Extracting an archive into a folder merges in the same way.
Transfers between the library and a connection merge too; moves and copies inside a connection don't.

![Copying a folder where one of its name is: Merge, Replace, Skip or Keep both](../images/merge-folders.png)

**Moving** keeps what SFE knows about the files and folders, as a [rename](#rename) does: tags, thumbnails and
trickplay, a folder's access rules, quick links, scheduled scans and settings naming it. A folder takes along the
access it has in its own rules; what it got from the folder it was in changes to what the new place gives. When more
people would see or change a folder in its new place, SFE says who, and offers to **Keep the access they have now**
(rules on the moved folder keep it as before). You can move a folder only if you may move everywhere inside it.

**Copying**:

- **Copy their tags too** (asked each time) gives the copies the tags of the files they come from.
- Copies are added to the index at once (a copied folder is scanned), with the thumbnails and trickplay of their
  originals.
- Only what you may see is copied: sub-folders you can't see or open are left out, and the panel says which.
- Links (shortcuts or junctions to other folders) inside a copied folder are left out too: SFE never copies what they
  lead to.
- A read-only folder's files can be copied elsewhere.
- A copy is refused when it would leave less free space on the drive than the space's
  [Free space kept by copies](../administration/file-management.md#settings) (1 GB by default).

## Compress into a zip

With the right to compress (`files.compress`, which no group has until an administrator gives it), files and folders
can be put in a zip saved in the library: **Compress…** in the selection bar, or in a file's or folder's menu. The zip
goes into the folder listed (in search results, the folder of the first item); **Another folder…** picks another one
by clicking through the library. The dialog says how much will be read, from the index (folders never scanned are
named, not counted).

![Compress…: name, compression and a password typed twice](../images/compress-dialog.png)

- **Name**: the only item's name, or the folder's; `.zip` is added.
- **Compression**: **Store** (no compression: the fastest), **Fastest**, **Normal** (the default) or **Maximum** (the
  smallest, several times slower).
- **Skip files already compressed** (ticked by default): pictures, videos, music and archives are stored as they are,
  since compressing them again gains almost nothing.
- **Password** (typed twice): the files' content is encrypted with AES-256. Their **names stay visible** to anyone with
  the zip. 7-Zip, WinRAR and recent Windows 11 open such zips; older Windows zip folders don't. 7-Zip takes only
  passwords made of A–Z, digits and common signs.

The zip is made on the server in the background, one at a time for the whole server, in the panel at the bottom
right: its progress and the time left, **Cancel** (nothing half made stays), then the ratio achieved ("12 MB → 4 MB (33 %)"); **Show**
in the message shows it in its folder. When a zip of that name is already there, the panel asks (Replace, Skip, Keep
both). What it holds is what you could download: folders you can't see are left out, links and folders you may not
download are named in a file `SFE - left out.txt` inside the zip. A zip is refused when the drive would keep less than
the space's [Free space kept by copies](../administration/file-management.md#settings).

Downloading several items as a zip ([below](#download-several-files-or-a-folder)) doesn't change: it saves the zip on
your device, not in the library.

## Extract an archive

**Extract to “name”** and **Extract…** in a zip, 7z, rar or tar archive's menu write its files into the library, in
the background; see [Archives](archives.md#extracting).

## Upload

Files and folders from your computer go into the folder you are viewing:

- **Upload files** (the arrow next to the folder's name) picks files; **Upload a folder** picks a folder, uploaded with
  everything in it (its sub-folders are made as they come).
- Or **drop** files and folders from your computer onto the page: they go into the folder listed, or into a folder of
  the listing (or of the location bar) when you drop them on it.

![Uploading files](../images/upload-panel.png)

Before sending anything, SFE checks the names. When some are already taken there, the panel asks for each:
**Replace** (the file there goes to the trash: you need the right to delete there), **Skip**, or **Keep both** (a
number is added to the new name); tick **The same for the other names taken** to answer once.

The panel at the bottom right shows each upload with its progress and speed. Files are sent in pieces, two files at a
time, each with a checksum the server compares before keeping it, so a piece damaged on the way is sent again
instead of spoiling the file. **Pause** and **Resume** stop and go on, a piece that fails is sent again by itself,
**Retry** goes on with a
file that failed, and **Cancel** stops (what was sent of an unfinished file is removed); a right click on an upload
has them too, and the folder it goes into (also in a new tab). The browser warns you before
you leave the page while files are being sent.

**Interrupted uploads go on:** if the page is reloaded or closed, or the connection drops, the panel lists the
unfinished files when you come back (also those into a connection such as Dropbox, and after the server restarted). Pick or drop the same files into the same folder again: they go on where they
stopped. What was sent is kept on the server for the space's **Keep unfinished uploads for** (48 hours by default)
after the last piece arrived.

An uploaded file keeps its date from your computer and is added to the index at once.

- A space can set the **largest file** one may upload (no limit by default); larger files are left out before sending.
- An upload is refused when it would leave less free space on the drive than the space's **Free space kept by copies
  and uploads** (1 GB by default).
- Behind a reverse proxy, SFE sends smaller pieces by itself when the proxy refuses large requests
  ([Installation](../installation.md#reaching-it-from-outside-https-and-a-reverse-proxy)).

## Undo

**Ctrl+Z** (⌘Z on a Mac) undoes your latest action, as the **Undo** in its message would, even once the message is
gone; press it again for the one before. **Undo** in the ⋮ menu does the same and shows, under it, what it would undo.

![Undo in the ⋮ menu, after a new folder](../images/undo-menu.png)

| What you did | What Undo does |
|---|---|
| Rename, rename several | gives the old names back |
| Delete (to the trash) | puts the items back |
| Move | moves the items back into the folders they came from |
| Copy into the library | moves the copies to the trash |
| New folder, new file | moves it to the trash |
| Remove a duplicate | puts the copy back |

- A copy, new folder or new file that changed since (a file saved over, something added to the folder) stays where it
  is: a message says so. Nothing is ever deleted for good by Undo.
- Each browser tab remembers your last 20 actions until it is reloaded or closed. What you did in another tab, on
  another device, or what other people did, is not undone.
- A rename in a [connection](../administration/connections.md) is undone only while that connection is open: Undo in
  the ⋮ menu is greyed and says where to go.
- Ctrl+Z in a text field, in the text editor or in the picture editor undoes the typing or the edit there, not a file
  action. There is no redo.

## Seeing changes made elsewhere

When someone else (or you, in another tab) creates, renames, moves, copies, uploads or deletes something through SFE,
the folder you are viewing is listed again by itself a moment later: what was added shows, what went away goes,
without losing your place or what you selected.

The same goes for changes made **outside SFE**: a file saved by another program, a folder renamed in the file manager,
someone else's change on a network share. They show a moment later in the folder you are viewing (on a network folder,
within about 10 seconds; a file being written for a long time refreshes the listing at most every 2 seconds). Only the
folders open in a browser are followed, not the whole library. In a [connection](../administration/connections.md)'s
folder, what was changed at the storage service (Dropbox's website or app, another computer) shows within about 30
seconds. To stop it, untick **Show changes made outside SFE** in your own
[settings](../administration/my-account.md): such changes then show when you open the folder again.

## Files being written

A file or folder being copied (in the library or from a connection), a zip being made and an archive being extracted
show in their folder at once, under the name they will have, faded and marked **being written**, with a bar showing
how far they are (point at it for the percentage and the size written so far). Everyone viewing that folder sees it
move, whoever started the copy. They can't be opened or previewed until they are complete; their menu only offers
**Delete**, **Copy full path** and **Properties**, and search and duplicates never find them. When the copy ends, the
item takes its place in the listing like any other.

![A folder being copied, with its progress in the listing](../images/partial-file.png)

On the disk they are written under a temporary name ending in `.sfe-partial`, renamed once complete. One left behind
by an operation that stopped (the server was restarted during it) shows the mark without a bar and can be deleted;
SFE also removes it at the next copy, extraction or compression into that folder.

## On phones

- **Press and hold** a file or folder to select it (as phone file managers do), on Android and iPhones; press and
  hold others, or tick their boxes, to add them, then use the selection bar.
- Press and hold a tag, a folder of the path, a recent search or a row of a list to open its menu.
- The **⋮** button of each item opens its menu, as a sheet at the bottom of the screen. Android's **Back** closes it
  (and stays on the page); it stays open when you turn the phone.

  ![The item menu on a phone](../images/phone-item-menu.png)

- Items are not dragged with a finger: use **Move to…** and **Copy to…** in the menu or the selection bar.

## Download several files or a folder

Select the files and folders, then **Download** in the selection bar (or **Download as zip** in the menu of one of
them): your browser downloads one zip file holding them, folders with everything in them. One file alone is
downloaded as it is.

- The zip is made while it downloads: it starts at once whatever its size, and the browser shows how much has come
  (not how much is left: the size isn't known in advance). Stopping the download in the browser stops it on the server.
- It is named after the folder downloaded, or the folder the items are in.
- Photos, videos, music and other files already compressed are put in the zip as they are (compressing them would only
  take time); text files are compressed.
- Hidden files are included. Folders you can't see are left out without a word; links (shortcuts to other folders),
  files that couldn't be read and folders you may not download are left out too, and listed in a file
  **SFE - left out.txt** inside the zip.
- Each person downloads at most two zips at once.
- A space can set the **largest zip** ([File management](../administration/file-management.md#settings)): a larger
  one is refused before it starts.

## Properties

**Properties** in the item menu, or **Alt+Enter** on the item with the focus (or the one selected), shows:

- its name, kind, the folder it is in, its size (exact, in bytes), when it was modified, created and last opened (as
  far as the server's disk keeps it), and whether it is hidden, read-only or a link to another place;
- what the index knows: a picture's or video's resolution, a video's or song's length, its tags, or that no scan has
  indexed it yet;
- for a file of the library, how many [previous versions](#previous-versions) it has, with **Show…**;
- **Access**: what you may do with it (open, download, save changes over a file, add files and folders to a folder,
  rename, move, move to the trash), with a ✓ or, for what you can't, the reason under it: the server's or the space's
  switch, a visitor or guest account, your rights, a folder rule, a link the server keeps read-only, the disk (mounted
  read-only, or the server not allowed to write there), a file the disk marks read-only. **Folder rule** names the
  folder whose rule decides here (ask an administrator to change it), **Disk** says whether the disk takes changes
  where the item is.

![The Access section of a file's properties](../images/properties-access.png)

![The properties of a picture, with its checksum](../images/properties-file.png)

**A folder** shows what it holds as the index knows it (as of the last scan) at once. **Count on disk** goes through
the folder on the server for the exact count now, with its progress; **Stop**, or closing the sheet, stops it. Folders
you can't see are never counted; links to other places are not followed (the count says how many were left out). A
count stops at two million files and folders ("more than").

![The properties of a folder, counted on the disk](../images/properties-folder.png)

**A file's checksum** (SHA-256, SHA-1 or MD5), to compare it with one published elsewhere: pick the algorithm, then
**Compute**. The server reads the whole file, with its progress; **Copy** puts the result on the clipboard. Only those
who may download the file can compute it, one file at a time.

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
- **The drive is (nearly) full**: copies and uploads keep the free space the space asks for.
- **The file is in use**: another program has it open, or a video is playing. Close it and try again.
- **The server's account may not change files there**: the server was installed with read-only access to the library
  ([Installation](../installation.md) says how to give it write access).
- **The server's account may not read it**: the file (or one inside the folder copied, which is named) cannot be
  read by the account SFE runs as, often a file put on a shared drive by another user. Give that account read access
  to it.
- **Folder access rules are set for a folder of that name** (rules left from a folder that was deleted or moved outside
  SFE): an administrator can remove them or point them elsewhere in
  [Folder access](../administration/spaces-and-accounts.md#folder-access).
- A folder can be renamed only by someone who may rename **everywhere inside it**: a sub-folder you can't see, or that
  is read-only for you, keeps you from renaming the folder holding it.
