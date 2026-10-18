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

## Buttons and keys

| Button | Key | |
|---|---|---|
| Wrap long lines | | Long lines continue on the next line instead of going off the side; kept for the next files |
| Line numbers | | Shows or hides the numbers at the left; kept for the next files |
| Find (magnifier) | Ctrl+F | Finds a word in the text: **next**, **previous**, **all**, match case, regular expression, whole word |
| Go to line | Ctrl+Alt+G | Jumps to a line number (`42`), or a line and column (`42:7`) |
| | F3, Ctrl+G / Shift+F3, Shift+Ctrl+G | Next / previous match of the word found |
| | Ctrl+A, Ctrl+C | Selects all the text, copies the selection |

![Finding a word in a log](../images/text-viewer-find.png)

Click in the text to move around in it with the keys: the arrows, Page Up / Page Down, Home / End, Ctrl+Home / Ctrl+End;
the mouse wheel and swipes scroll it. **Esc** leaves the text (then ← / → go to other files again), a second **Esc**
closes the viewer.

## Other files: Open as text

Files the viewer doesn't show (a `.dat`, a file without an extension) can still be read as text: **Open as text** in
their [item menu](managing-files.md) (right click, or **⋮**). Files that aren't text (a picture saved under another name,
an archive) say so and offer **Download** instead.

Files larger than 10 MB are not shown: download them to open them in an editor of your computer.

Opening a text file needs the right to open files (`files.view`, see [Rights](../reference/rights.md)); Download
needs `files.download`.
