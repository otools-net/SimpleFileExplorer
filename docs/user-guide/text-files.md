# Text files

Text files open in the [viewer](viewer.md), like pictures and videos: notes (`.txt`, `.md`), logs (`.log`), settings
(`.json`, `.yaml`, `.ini`, `.xml`), subtitles (`.srt`, `.vtt`), scripts and code (`.ps1`, `.sh`, `.py`, `.cs`, `.js`,
`.html`, `.css`, `.sql`…), and files such as `README`, `LICENSE` or `Dockerfile`. They are shown as they are, read only:
nothing in them runs.

![A PowerShell script in the viewer, highlighted](../images/text-viewer.png)

- **Highlighting** follows the file's type: keywords, strings, numbers and comments in their own colours; in logs,
  dates, times and levels (errors in red, warnings in purple).
- The bar at the bottom tells the type, the **encoding** (UTF-8, UTF-16, or Windows-1252 for older files, as Notepad
  reads them), the **line endings** (Windows CRLF or Unix LF) and the size; at the right, the line and column of the
  cursor.
- **‹ / ›** at the sides go to the previous and next file of the folder that the viewer shows, pictures and videos
  included.
- Notes and documents (`.txt`, `.md`, `README`…) open as **text to read**: long lines wrapped, a reading font, wide
  margins. Code, logs and settings open as **code**: a monospace font, line numbers, long lines kept whole. Each has
  its own [reading settings](#reading-settings).

## Buttons and keys

| Button | Key | |
|---|---|---|
| Wrap long lines | | Long lines continue on the next line instead of going off the side |
| Line numbers | | Shows or hides the numbers at the left |
| Find (magnifier) | Ctrl+F | Finds a word in the text: **next**, **previous**, **all**, match case, regular expression, whole word |
| Go to line | Ctrl+Alt+G | Jumps to a line number (`42`), or a line and column (`42:7`) |
| | F3, Ctrl+G / Shift+F3, Shift+Ctrl+G | Next / previous match of the word found |
| Aa | | The [reading settings](#reading-settings) |
| | Ctrl+= / Ctrl+− / Ctrl+0 | Larger / smaller text, back to its usual size (with the cursor in the text) |
| | Ctrl+A, Ctrl+C | Selects all the text, copies the selection |

![Finding a word in a log](../images/text-viewer-find.png)

Click in the text to move around in it with the keys: the arrows, Page Up / Page Down, Home / End, Ctrl+Home / Ctrl+End;
the mouse wheel and swipes scroll it. **Esc** leaves the text (then ← / → go to other files again), a second **Esc**
closes the viewer.

## Markdown

Markdown files (`.md`) open **rendered**, as they are meant to be read: headings, lists, tables, quotes, links and
pictures, with code blocks highlighted. The **outline** button lists the headings at the left; click one to jump to it.

![A Markdown guide with its outline](../images/markdown-viewer.png)

- **Read** / **Source** / **Split** at the left of the bar: the document, its text as written (with line numbers and
  Find, as other text files), or both side by side (one above the other on narrow screens). The choice is kept for the
  next documents.
- **Links** to other documents of the library (`[packing list](Packing list.txt)`) open them in the viewer; links to
  other files open the file in a new tab, links to folders their listing, links to web sites a new tab.
- **Pictures** of the library show; pictures of other web sites are not loaded until you click **Show** in the bar
  at the bottom, so that reading a document doesn't tell those sites. HTML written in a document is shown as text,
  never run.
- In the document, Ctrl+F is the browser's own find; with the source shown, it finds in the source.

![A Markdown document beside its source](../images/markdown-split.png)

## Reading settings

**Aa** at the end of the text's bar opens the reading settings. They apply at once, and this browser keeps them: one
set for text to read, one for code.

![The reading settings over a diary, in sepia](../images/text-reading-settings.png)

| Setting | |
|---|---|
| Font | **Literata** (made for reading long texts), **Atkinson Hyperlegible** (letters that can't be mistaken for each other), **JetBrains Mono** (for code); or the serif, sans-serif or monospace font of this device; or **Another font of this device…**, by its name (`Fira Code`, `Calibri`) |
| Size | **A−** / **A+**, from 10 to 32 pixels (also Ctrl+− / Ctrl+= in the text, Ctrl+0 for the usual size) |
| Line height | The room between lines |
| Width | The longest line when lines wrap, in characters (about 70 is easiest to read), or the whole width |
| Margins | The room around the text |
| Wrap long lines, Line numbers | As the buttons of the bar |
| Colours | **App** (light or dark, with the app's [theme](browsing.md#light-and-dark)), **Light**, **Sepia** (warm paper) or **Dark** |
| Reset | Back to the first settings (for this kind of text), and the colours as the app |

The three bundled fonts are downloaded from the server the first time one of them is used, and only the letters a
page needs. On a phone the settings open as a sheet at the bottom of the screen:

![The reading settings on a phone](../images/text-reading-phone.png)

## A page of its own

**Open in new tab** (at the top right of the viewer) opens the text file in its viewer, in a tab of its own, with the
same buttons and settings; so does a middle click or Ctrl+click on a text file in a listing. The arrow at the top left
of that page opens its folder.

![A script on a page of its own](../images/text-view-page.png)

## Other files: Open as text

Files the viewer doesn't show (a `.dat`, a file without an extension) can still be read as text: **Open as text** in
their [item menu](managing-files.md) (right click, or **⋮**). Files that aren't text (a picture saved under another name,
an archive) say so and offer **Download** instead.

Files larger than 10 MB are not shown: download them to open them in an editor of your computer.

Opening a text file needs the right to open files (`files.view`, see [Rights](../reference/rights.md)); Download
needs `files.download`.
