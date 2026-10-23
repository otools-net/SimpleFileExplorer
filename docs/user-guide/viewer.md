# Viewer

Clicking a file in a listing opens it full screen over the page: pictures as they are, videos and audio in SFE's own
[player](video-player.md), and [text files](text-files.md) with their lines numbered and their code highlighted. Other
files (a PDF, an archive) open in a new tab or download, as the browser chooses.

![A picture in the viewer](../images/viewer.png)

At the top left: the file's name, its position in the listing (`5 / 6`), resolution, size, date and duration, and its
tags. Hidden tags such as **Archived** are shown here too.

## Buttons and keys

The top right of the viewer holds three buttons: **Tags**, **⋮** (more actions) and **×** (close).

| Button | Key | |
|---|---|---|
| ‹ / › at the sides | ← / →, mouse wheel down / up, a swipe | Previous / next file of the listing that the viewer shows, skipping the others (the wheel works anywhere but over the video controls and in a text) |
| Tags | T | The file's [tags](tags.md) |
| ⋮ | | The menu below |
| × | Esc | Back to the listing, scrolled where you were |

The **⋮** menu offers what applies to the file shown (Esc closes it). A right click on the picture, the video or
the document opens the same menu (on selected text, and while editing a text, the browser's own menu stays). For a
video, the [player's controls](video-player.md#its-menu) come first.

| Entry | Key | |
|---|---|---|
| Actual size / Fit to the screen | Z | Pictures larger than the screen: [100%](#actual-size) or whole again |
| Crop… | E | Opens a picture in the [picture editor](picture-editor.md) (right `files.edit`) |
| Add to quick links / Remove from quick links | | Keeps the file in your [quick links](browsing.md#quick-links), in the sidebar |
| Copy full path | | When [copying paths](browsing.md#copying-full-paths) is on |
| Open in a new window | W | Videos: moves the video to a [window of its own](video-player.md#its-own-window) |
| Open in … | | Videos and audio: an [external player](video-player.md#external-player) such as VLC |
| Open in new tab | | The file itself (a text file: its [page of its own](text-files.md#a-page-of-its-own)), in a new browser tab |
| Download | | Saves the file (right `files.download`) |
| Copy, Copy to… | | Keeps the file to paste into a folder of the listing, or copies it into a folder you pick ([Managing files](managing-files.md#move-and-copy)) |
| Properties | | Its size, dates and what SFE knows of it ([Properties](managing-files.md#properties)) |

Renaming, moving and deleting are done from the listing, where the file's own menu has them.

Photos larger than **Large image size** ([Thumbnails](../administration/thumbnails.md)) are shown through their
thumbnail, in the viewer as in the listings.

## Actual size

A picture larger than the screen is shrunk to fit it, and the pointer turns into a magnifier over it. Click it (or press
<kbd>Z</kbd>, or **Actual size** in the **⋮** menu) to see it at its actual size, 100%, with the point clicked staying under the
pointer. The pointer then becomes a hand: drag the picture (with the mouse, or a finger on a touch screen) to look
around it, or scroll it with the mouse wheel (<kbd>Shift</kbd> + wheel: sideways). A click without dragging, **Fit to the screen**,
<kbd>Z</kbd> or <kbd>Esc</kbd> show it whole again. Moving to another file shows it whole too.

![A picture at its actual size, being dragged](../images/viewer-actual-size.png)

A large photo shown through its thumbnail can be seen at its actual size once its original has loaded.
