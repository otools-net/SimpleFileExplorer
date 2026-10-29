# Changelog

What each version of SFE added or fixed. What an upgrade changes for an existing install (database, settings, first
scan) is in [docs/upgrading.md](docs/upgrading.md).

## Unreleased

### Fixed

- **Phones: the bar at the bottom no longer jumps** when a menu or a dialog opens from the bottom of the screen (an
  item's menu while items are selected, Rename, Tags…): holding the page behind it made Chrome show its address bar
  again, which moved everything at the bottom. The page is now held without it, and a swipe on the dimmed area no
  longer scrolls the page.
- **Phones: tiles keep their place as pictures load**: a tile takes the shape of its picture from the index at once,
  instead of when its preview arrives, and the names under the pictures are laid out with them, so the rows no longer
  move under the finger and a tap on ⋮ no longer opens the file below it instead.
- **Phones: the document viewers' toolbars keep their height** as their buttons move into "⋯" (JSON, CSV, subtitles,
  archives): the list under them no longer shifts by a few pixels as it opens.
- **The trash and missing files keep their names in a narrow column** (a phone held sideways, a tablet beside the
  sidebar): the buttons took the whole line, the name vanished and the page could be scrolled sideways.
- **Phones: settings' check boxes and lock switches, and the files of Duplicates & moves are easier to hit** with a
  finger.
- **Phones: the big Play button of a video always plays it**: on a small screen the controls covered it, and a tap
  only showed them. The seek bar is easier to grab with a finger.
- **Phones held sideways: a Markdown file's outline is readable**: its headings were squeezed to half their height.
- **Phones: swiping to the previous file in the viewer stays in the viewer**: Chrome took a swipe to the right as
  its own "back" and left the page.
- **iPhones: a tap on a recent search or a tag suggestion takes it** (in the search box, the tag pickers): the tap was
  cancelled and nothing happened.
- **iPhones: the page no longer zooms in when a field gets the focus** (search, rename, new folder, the text viewer's
  status bar…): text fields are 16 px on touch screens. The folder picker's path, the search's "in folder" button and the Close button of dialogs
  are easier to hit with a finger.
- **Phones: a tap on the dimmed area around a menu only closes the menu**: the tap no longer also reaches what is
  under it (the sidebar button, a file).

## 1.84.0

### Added

- **Archives open in the viewer** like folders of their own: a zip, 7z, rar or tar file shows its folders and files,
  their sizes and dates, without being unpacked, whatever its size (millions of entries, folders nested thousands
  deep). Move around with the folders, **Up** and **Go to folder**, or **Find in archive** by name across all of
  it (or in the folder shown only); open a file from the archive in place (pictures, text, Markdown, JSON, CSV, subtitles, PDF, read only) or in a new
  tab, or download it. **Open as archive** shows what comic books, EPUB books, office documents and packages are
  made of. Names leading out of the archive and links are never
  offered. See
  [Archives](docs/user-guide/archives.md).
- **Archives with a password** open with it: the viewer asks for it (as it opens an archive whose list is protected,
  or when a protected file is clicked), then their files open for 15 minutes. Zips with AES or ZipCrypto, 7z and rar.
  The password stays in the server's memory only, for you only, and a protected list is never written to its disk.
- **Extract archives** (zip, 7z, rar, tar, gz…) into the library, in the background: **Extract to “name”** and
  **Extract…** in an archive's menu and the selection bar (several archives at once, each beside it), and
  **Extract…** in the archive viewer for a folder or a file of it; a wrong password is asked again from the panel.
  Names leading out of the folder, links and zip bombs are left out and listed; a cancelled or failed extraction leaves
  nothing half written. New right `files.extract`, given to no group. See [Archives](docs/user-guide/archives.md#extracting).
- **Compress into a zip** saved in the library: **Compress…** in the selection bar and the item menu, with the
  compression asked (Store, Fastest, Normal, Maximum; pictures, videos and archives stored as they are) and an optional
  password (AES-256, the names stay visible). Made in the background, one at a time on the server; the panel tells
  the ratio achieved. New right `files.compress`, given to no group. See
  [Managing files](docs/user-guide/managing-files.md#compress-into-a-zip).
- **The log keeps what the server refused or failed at**, with the message the person saw, who asked and the address
  asked: a refused request is a warning, a server failure an error ([Logs](docs/administration/logs.md)).

### Fixed

- **Uses less battery on phones**, above all with SFE left open in the background:
  - a tab hidden for more than 30 seconds closes its live connection to the server (which woke the phone every
    25 seconds) and opens it again when shown, reading the folder and the trash count again;
  - a running scan, and the administration pages that refresh by themselves (Tasks, Video streams, Trickplay, Keep
    alive), are no longer polled while the tab is hidden, and refresh at once when it shows;
  - the scan's moving progress bar no longer makes the browser lay the whole page out on every frame, and the scan
    panel's clock only ticks while a scan runs.
- On a phone, the **menu of a file or folder** (its ⋮ button) no longer closes as soon as it opens after scrolling
  down the folder: the browser's address bar coming back, or the end of the scroll, closed it.
- **Messages no longer cover the moves and copies panel**: they show above it while it is open.
- In **fullscreen on a phone**, a wide video no longer turns back upright when you move along the time bar: the screen
  is held sideways until fullscreen ends.

## 1.83.0

### Added

- A **Trash** link at the bottom of the library's sidebar opens Administration › File management › Trash in one click,
  for those who may delete. Its icon shows whether the trash is empty, and a small number how many files and folders
  it holds (99+ above), kept up to date as things are deleted, restored or purged.
- **Fullscreen on a phone or tablet** turns the screen sideways for a wide video, as YouTube does, and back when it
  ends (browsers that allow it, such as Chrome on Android; iPhones already turn by themselves).
- **Flash movies and games** (`.swf`) play in the viewer and on a page of their own, through Ruffle, a Flash Player
  emulator that comes with SFE (nothing fetched from the internet): play / pause, play again, volume, full screen, and
  what the movie is (ActionScript version, size, frame rate). A movie may load the other files of its folder (levels,
  music), never anything else, and never acts as the person watching it. Administration › Playback › **Play Flash
  movies** (on by default) turns it off for a library.

### Changed

- The **tag sidebar's sections** fold under their heading: **Last searched** and **I feel lucky** are folded until you
  open them, and each browser remembers which ones you folded or opened.

### Fixed

- The **viewer** covers the whole screen: the page's scrollbar no longer shows as a strip at its right.

## 1.82.0

### Changed

- The **video player's controls** stay on one line on a phone or in a small window: the video's length leaves the time
  first, then the buttons that don't fit go, from the right, into a **More actions** (⋯) button at the end of the line
  (on/off ones with a check mark, Speed with its rates in place); Play, Mute and Fullscreen always stay. They used to
  wrap onto a second line.
- The **selection bar** stays on one line and whole on a phone: the count gets shorter first (`5 selected`), then the
  buttons that don't fit go, from the right (Download first), into its **More actions** (⋯) button; Clear, the count,
  Select all and Tags always stay. Its buttons on the right used to be cut off.
- The **bars of the document viewers** (text, Markdown, JSON, CSV, subtitles, PDF) stay on one line: the buttons that
  don't fit go, from the right (Aa first), into their **More actions** (⋯) button; the editing buttons and the fields
  stay (Save a copy and Done as icons on a phone). On a phone the PDF's find is a magnifier opening the field under
  the bar, and its zoom list goes into the menu.
- On a phone, the **folder's buttons beside the path** (quick link, tags, upload, new file, new folder, copy path) give
  way to one **⋯** opening the folder's menu, the same as a right click on a computer (with Paste, Select all, Refresh,
  Properties…): the path keeps its line, and the files start higher. They used to fill one or two lines of their own.
- On a phone, the **sub-pages of an administration section** (chips at the top) are one line that scrolls sideways, the
  page shown scrolled into view, instead of wrapping onto two lines.
- In the **List** layout, a narrow list (a phone, or a small window with the sidebar open) gives each file two lines:
  its whole name, then its type, size and resolution in small text. Names used to break mid-word over three lines.
- In the **text viewer**, with long lines not wrapped, a fade at the right edge shows that a line goes on past it,
  until the text is scrolled to its end.
- SFE's **work files** (the trash, unfinished uploads) go to the server's data folder by default, no longer to a
  hidden `.sfe` folder in the library. The spaces of an install updated from an earlier version keep theirs in the
  library; only new installs and spaces created later start on the server.

### Fixed

- In the **text viewer** on a phone, the reading settings button (Aa) was off the screen when the text had long lines
  (the bar grew with them).
- In **Duplicates & moves › Moved files**, a missing file's Choose file… and Forget buttons were drawn over its "Was
  in …" line on a phone or a narrow window: the line now ends with "…", and on a phone the buttons have a line of
  their own under the file.
- **Security:** `System.Security.Cryptography.Pkcs` (brought by the SQL Server driver) updated to 10.0.13 for the .NET
  denial of service vulnerabilities CVE-2026-84008 and CVE-2026-70162.

## 1.81.0

### Added

- A **logo** with the version in the console window at start; `--no-logo` on the command line leaves it out (never
  shown for a service or when the output goes to a file or a log).
- `--help` (or `-h`, `-?`) on the command line lists SFE's options, grouped: start-up, Windows service, main settings,
  database, setup and sign-in.
- **Default tags of folders**: in a folder's tags dialog, tags that every file put into the folder or its sub-folders
  gets (scanned, created, uploaded, copied or moved there), optionally added to the files already there too; taking
  one off asks whether the files keep it. Folders with default tags show a tag-and-sparkle icon listing them when clicked (new
  right `tags.folderDefaults`, given to Administrators and Users).
- **Open** and **Open in new tab** in the menu of files and folders (and **Open containing folder** in search
  results): no need for the browser's own menu any more.
- **Right-click menus all over the listing page**: the page itself (the folder listed: new folder or file, upload,
  paste, select all, refresh, its tags, quick link, Open in new tab, Properties; a search: select all, tag the
  results, search again, Open in new tab, copy link), the folders of the path at the top and the up / previous / next
  buttons, a search result's folder, quick links (also move up, move down, remove) and recent searches. Text boxes and
  selected text keep the browser's menu, as does Shift with the right click.
- The **menu key** (or Shift+F10) opens the menu of what has the focus, like a right click.
- In the **viewer**, a right click on the picture or video opens the viewer's menu (the ⋮ one), which now
  also has **Copy**, **Copy to…** and **Properties**. On a **video**, the menu starts with the player's controls:
  play / pause, speed, loop, mute, save this frame, full screen, picture in picture, and **Copy link at this time**.
- The video's own window has a right-click menu too: the player's controls, copy link at this time, download.
- A right click on a **tag chip** (on a file, in the sidebar, in the viewer) opens its menu: search it (also in a new
  tab), add it to the search or leave it out, remove it from the file, edit the tag.
- The files listed in **Duplicates**, **Similar videos** and the candidates of **Missing files** have a right-click
  menu: open in a new tab, open the folder (also in a new tab), download, copy full path.
- The **trash**'s items have a right-click menu: restore, delete for good, open the folder they were deleted from
  (also in a new tab).
- **Moves, copies and uploads** in progress (the panel at the bottom right, and Administration › Uploads) have a
  right-click menu: their buttons (cancel, pause, resume, close) and the folder they go into, also in a new tab.
- A right click on the app's other links (the ⋮ menu's, the administration's) opens a small menu: **Open**, **Open in
  new tab**, **Copy link address**; elsewhere on the administration page: **Back**, **Reload**, **Open in new tab**,
  **Copy page link**.
- In windows over the page (properties and other sheets, the picture editor, the viewer's bar) a right click opens no
  menu, except a link's; documents shown in the viewer keep their own right click.

### Changed

- On phones, the video player's speed list opens as a sheet across the bottom of the screen, like the item menu, with
  large buttons.

- When the server stops answering (stopped, or the network is down), the ⋮ button at the top right shows a yellow
  **!** and its menu says **Server not answering**, until the connection comes back; the folder shown is then read
  again.

### Fixed

- After going from page to page by full loads (typing addresses, Back and Forward), a page could take a long time to
  load: the pages the browser keeps for Back held their connections to the server. They now give them up.
- Two SFE servers on the same computer (on different ports) opened in one browser no longer sign each other out: each
  port gets a sign-in cookie of its own. Sessions signed in before stay signed in. A visitor's chosen space (without an
  account) is asked again once.

## 1.80.0

### Added

- **Excluded folders**: **Scans and previews…** in a folder's menu keeps it, with its sub-folders, out of scans,
  trickplay or thumbnails, each on its own, for the whole space (new right `index.exclude`, given to Administrators).
  Excluded folders show a small note in the listing and next to the folder's counts. New settings
  `Trickplay:ExcludedFolders` and `Thumbnails:ExcludedFolders`.
- **Administration › Scanning › Excluded folders**: every excluded folder in one table, to add, change and remove them
  (folders gone from the disk too).
- **Keep alive** for libraries on a network share: `Sfe:KeepAlive:Mode` (`Off` by default, `Auto`, `Always`) makes
  SFE look at each space's library folder every `IntervalSeconds` (240), so the file server never drops the idle
  connection. **Administration › Keep alive** shows each folder's last ping (and **Ping now**) and holds the settings,
  per space (new right `settings.keepAlive`, given to Administrators). **More folders** keeps folders reached through
  links to other shares alive too, and the library folder can be pinged by writing and deleting a small file in
  SFE's work folder instead of reading it (`Method: Touch`). While keep alive is on, the **⋮** button shows a green
  dot, or a yellow **!** when the network folder doesn't answer, updated live. See `docs/administration/keep-alive.md`.

### Changed

- Scans keep what the index has of an excluded folder (files, tags) instead of removing it: **Remove from index…**
  forgets a folder. Scanning an excluded folder, or one inside it, is refused.

### Removed

- The **server commands** (**Shut down server**, **Dismount drives** in Administration › System › Maintenance), their
  routes `POST api/system/shutdown` and `api/system/dismount`, the rights `server.shutdown` and `server.dismount`, and
  the settings `Sfe:System:EnableShutdown`, `ShutdownCommand` and `DismountCommand` (ignored if still set).

## 1.79.0

### Added

- A **Docker image** (`Dockerfile`, example `docker-compose.yml`): an External server with ffmpeg, its data in
  `/data`, its library in `/library`, for `linux/amd64` and `linux/arm64`. The database, the caches (thumbnails,
  trickplay) and the logs can each get a folder of their own: `SFE_DATABASE_DIR`, `SFE_CACHE_DIR`, `SFE_LOGS_DIR`.
  See `docs/docker.md`.

### Fixed

- On a server whose configuration makes it External, the console no longer says the setup needs no code on its own
  computer.

## 1.78.0

### Changed

- **External mode applies**: an External server takes the first space's library folder and ffmpeg from its
  configuration only (`Sfe__Root`, `Sfe__Thumbnails__FfmpegPath` in its environment, or `appsettings.json`). The setup
  page and Administration › System › Installation show them, without a way to change them. The listening address stays
  on the page; new spaces are still created inside `Sfe:Tenants:AllowedRoots`.
- Fast sign-in only works on a Local server.
- A new install whose configuration makes it External always asks for the setup code, also on the server's own
  computer.

## 1.77.0

### Changed

- **Local mode now applies**: a Local server answers only the computer it runs on (`http://localhost`), lets only
  administrators sign in (server administrators and members of a space's Administrators group), and has no visitors
  without an account. Other accounts' sessions end.
- **An install upgraded without a mode chosen is Local.** If people open it from other devices or with accounts that
  are not administrators, set it to **External**: from another computer, the page SFE now shows there lets the server
  administrator sign in and switch it with the setup code; or set `Sfe__Mode=External` in the server's environment.

### Added

- A page for visitors of a Local server from another computer: where to open SFE instead, and the switch to External
  for its administrator.
- `GET api/config` gives the server's `mode`.

## 1.76.0

### Added

- **How SFE is used**, chosen at the setup and in Administration › System › Installation: **Local** (SFE on your own
  computer, for its administrators) or **External** (opened by other people from their devices). Local is only
  chosen from the server's own computer. An External server goes back to Local only in its configuration. The
  environment can set it too (`Sfe__Mode`). For now the mode is only recorded: each mode's limits come in the next
  versions.

### Changed

- An install upgraded from an earlier version has no mode chosen yet and counts as **Local**. If other devices
  open it, choose **External** in Administration › System › Installation.

## 1.75.0

### Added

- The **picture editor saves over the original** with **Save** (Ctrl+S), for accounts that may replace the picture
  (the same rule as for text files: delete it and create files in its folder, and `files.edit`): the version before
  goes to the trash, the picture keeps its tags, and the index has its new size at once. **Save a copy** (Ctrl+Shift+S
  then) still makes a new file. A picture changed elsewhere since the editor opened it is only saved over after
  asking. JPEG, PNG and WebP pictures only (the formats the editor writes); others are saved as copies.
- `GET api/editor/images/access` (the picture's version and whether the caller may save over it) and
  `PUT api/editor/images` (the edited picture over the original, `If-Match` with that version).

### Changed

- A picture saved over shows again at once in the listing and the viewer of the tab that saved it.

## 1.74.0

### Added

- **EPUB books** open in a reader: **one page** at a time, **two pages** side by side as an open book (one when the
  window is narrow), or **scrolled** as one long page. ← → (or a swipe) turn the pages; the **contents** list the
  chapters; the bar tells the chapter and how far through the book; a book opens where it was last read in this
  browser. The **reading settings** (font, size, line spacing, light, sepia or dark) apply to its pages.
- `GET api/files/epub/{book}/{entry}`: the files inside a book, served by the app so that its styles, pictures and
  fonts load under the app's own policy; nothing of a book runs.

## 1.73.0

### Added

- **PDF documents** open in the viewer (pdf.js, bundled: nothing is fetched from elsewhere): pages drawn as you scroll,
  text to select and **find** (Ctrl+F), links, the document's **outline**, **zoom** (automatic, page width, whole
  page, 50–300 %), previous / next page (← →), **dark pages** for reading at night. A document opens at the page it
  was last read at in this browser; a document with a password asks for it. Scripts in documents never run.

## 1.72.0

### Added

- **Subtitles** (`.srt`, `.vtt`) open as a list of their **cues**: when each starts and ends, what is said (italic,
  bold, who speaks), cues that overlap marked. **Filter** the cues; double-click one to see it in the source. **Play**
  opens the video next to the subtitles (`Film.en.srt` → `Film.mkv`) at the cue selected, in a new tab.
- **Shift times** (while editing subtitles): moves every cue by the seconds typed, earlier or later, for subtitles out
  of sync with their video; only the times change, Ctrl+Z undoes it.
- `GET api/files/text/video`: the video a subtitle file belongs to.

## 1.71.0

### Added

- **CSV and TSV files** (`.csv`, `.tsv`, `.psv`) open as a **table**: the first row naming the columns (or not),
  the separator guessed (comma, semicolon, tab, pipe) or chosen, numbers to the right. Click a column's name to
  **sort** by it (numbers as numbers), **filter** the rows on any cell, move between cells with the arrows and copy one
  (Ctrl+C). Tables of any length scroll smoothly: only the rows on screen are drawn. **Table** / **Source** /
  **Split** show its text beside it; **Edit** changes the source, the table following as you type.

## 1.70.0

### Added

- **JSON files** (`.json`, `.jsonc`, `.geojson`, `.webmanifest`, `.har`) open as a **tree** of their members: open
  and close them with the mouse or the arrows, **filter** on keys and values, **copy the path** (`$.days[1].town`) or
  the value of the one selected. Keys stay in the order written and numbers as written (long ids are not rounded);
  comments and trailing commas (`appsettings.json`, `tsconfig.json`) are allowed. **Tree** / **Source** / **Split**
  show its text beside it; a file that isn't valid JSON says where. **Edit** changes the source, the tree following as
  you type; **Format** lays it out again, indented, comments kept.

## 1.69.1

### Fixed

- A video whose trickplay was made again while its old sheets were being shown (or scanned by an antivirus) could
  be left without trickplay until the file changed; replacing it now waits a moment and tries again.

## 1.69.0

### Added

- **Large text files** (over 10 MB) open in parts, at whole lines: **Start**, **Earlier**, **Later**, **End**; a
  `.log` opens at its end.
- **Follow**: a text file being written (a log) shows what is added to its end every two seconds, as `tail -f` does.
- **Reopen with encoding**: the encoding in the text's status bar reads the file again as UTF-8, Windows-1252 or
  UTF-16, when its letters show wrong. While editing, the status bar chooses the encoding and the line endings a save
  writes.
- A file **changed elsewhere** while it is open (another person, another tab, another program) says so: **Reload** or
  keep the text shown.
- Text files open **where they were last read** in this browser.
- **More text files** (`Sfe:FileTypes:Text`, Scanning › File types): extensions of the library opened as text in the
  viewer, besides those it knows.

## 1.68.0

### Added

- **Reading settings per account**: **Save as my default** in the reading settings (**Aa**) makes them yours, in
  every browser you sign in from; a browser's own changes still go over them, and **Reset** goes back to them.
- Administrators set the **library's defaults** for reading text files (colours, and for notes and for code: font,
  size, line height, width, margins, wrapping, line numbers) in **Interface › Appearance**; accounts change theirs in
  **My account › Preferences**.

## 1.67.0

### Added

- **Text files can be changed in the viewer**: **Edit** (or E), then **Save** (Ctrl+S) over the file, in the encoding
  and with the line endings it had. Saving over a file needs the rights that replacing it would (delete it, create
  files in its folder); otherwise **Save a copy** next to it, or **Download** the text as it is.
- The first save over a file keeps **its previous version in the trash** (shown there as "previous version", to
  restore next to it), unless the space deletes for good; the file keeps its tags, and the index follows its new
  content.
- A file changed elsewhere since it was opened is not saved over without asking: **Reload**, **Overwrite** or **Save
  a copy**. Text that a Windows-1252 file can't hold is offered to be saved as UTF-8.
- Unsaved changes are asked about before the file is left: another file, closing the viewer, another folder or
  search, closing the tab.
- Markdown is edited beside the document, which follows the text as it is typed.
- **New file** opens the new text file in the viewer, ready to be written.
- The item menu (right click) offers **Edit** on text files and **Crop…** on pictures.

### Changed

- The viewer's top bar keeps only **Tags**, **⋮** and **×**: zoom, crop, quick links, copy path, new window, external
  player, open in new tab and download are in its **⋮** menu.

## 1.66.0

### Added

- **Markdown files** (`.md`) open **rendered**: headings, lists, tables, quotes, links and pictures, with code blocks
  highlighted, in the reading settings of documents. **Source** shows the text as written, **Split** both side by
  side; an **outline** of the headings jumps to each.
- Links of a document to other documents of the library open them in the viewer; links to other files open the file,
  links to folders their listing, links to other sites a new tab.
- Pictures of other sites are not loaded until you ask (**Show**, at the bottom): reading a document doesn't tell them.
  A document's own HTML is never run (shown as text).

## 1.65.0

### Added

- **Reading settings** for text files (**Aa** in the text's bar): the font (three bundled fonts made for reading —
  Literata, Atkinson Hyperlegible, JetBrains Mono — the device's own, or any font installed on it), the size
  (also Ctrl+= / Ctrl+− / Ctrl+0 in the text), the line height, the longest line, the margins, wrapping, line numbers,
  and the colours (as the app, light, sepia or dark). Notes and documents (`.txt`, `.md`, `README`…) and code have
  settings of their own; this browser keeps them.
- Notes and documents open **wrapped, in a reading font, with margins**; code and logs keep a monospace font and line
  numbers.
- **A text file on a page of its own**: "Open in new tab" in the viewer, and a middle click or Ctrl+click on a text
  file in a listing, open it in its viewer in a new tab (`view?path=…`) rather than as raw text.

## 1.64.0

### Added

- **Text files open in the viewer**: `.txt`, `.log`, `.md`, `.json`, `.cs`, `.js`, scripts, configuration files and
  many more, with syntax highlighting, line numbers, wrapping of long lines, **Find** (Ctrl+F) and **Go to line**
  (Ctrl+Alt+G). The status bar tells the language, the encoding (UTF-8, UTF-16 or Windows-1252, as Notepad reads
  them), the line endings and the size. Read only for now; files over 10 MB, and files that aren't text, offer a
  download instead.
- **Open as text** in the item menu shows any other file as text.
- The viewer's previous and next steps through text files too, with the photos, videos and music of the folder; in a
  text file the arrow keys, the wheel and swipes move in the text (Esc leaves it, a second Esc closes).

### Fixed

- The source of the log page and of the server's log store was missing from the repository (a `logs/` rule of
  `.gitignore`).

## 1.63.0

### Changed

- **Removing a duplicate** moves the copy to the trash instead of deleting it for good: **Undo** in the message, or
  **Restore** on the trash page, puts it back (deleted for good only when the space deletes permanently).
- **Cards and rows** of files and folders keep two buttons, tags and **⋮**: **Copy full path** and the new **Add to
  quick links** / **Remove from quick links** are in the item menu.

### Fixed

- **Phones:** the item menu and the dialogs that open at the bottom of the screen cover the whole width, without a gap
  on the right where the scrollbar goes.

## 1.62.0

### Added

- **Undo** after a rename and after a move (the items go back where they were), in the message that says it is done.
- **Rename several at once**: find and replace in their names, or numbered names (`Trip ##`), with a preview of every
  new name and of those that can't be given; undone together.
- **New file**: an empty file in the folder listed (new right `files.createFiles`, in no group).
- **Changes made elsewhere show by themselves**: when another person or tab changes the folder you are viewing through
  SFE, it is listed again in place (a live feed from the server), keeping your position and selection.
- **Phones**: the item menu opens as a sheet at the bottom; pressing and holding an item selects it; items are not
  dragged with a finger (Move to… and Copy to… do it).

### Fixed

- **Phones:** the folder's buttons (tags, upload, new file, new folder) go below its name instead of squeezing it into
  a narrow column.

## 1.61.0

### Added

- **Properties** of a file or folder (item menu, **Alt+Enter**): name, kind, folder, exact size, dates (modified,
  created, last opened), hidden / read-only / link, resolution, length and tags from the index. A folder shows what it
  holds as the index knows it, and **Count on disk** counts it on the server now (progress, stop). A file's
  **checksum** (SHA-256, SHA-1 or MD5) for those who may download it.

### Changed

- A right click on a file or folder opens SFE's item menu for everyone who may browse (Shift: the browser's menu).

## 1.60.0

### Added

- **Download several files or a folder as one zip:** **Download** in the selection bar, **Download as zip** in the item
  menu. The zip is written while it downloads (no waiting, no temporary file); media is stored as it is, text
  compressed; hidden files included, folders one can't see left out, links and unreadable files listed in a note inside
  the zip. Two zips at once per person; new setting **Largest zip to download** (no limit by default). Needs the right
  `files.download` only.

### Changed

- **Administration › File management:** the settings of copies, uploads and downloads are grouped apart from those of
  deleting.

## 1.59.0

### Added

- **Upload:** **Upload files** and **Upload a folder** next to the folder's name, or drop files and folders from the
  computer onto the page or onto a folder. Names already taken are asked about first (**Replace**, **Skip**, **Keep
  both**, the same for the rest). Files go up in pieces with progress and speed, **Pause**, **Resume**, **Retry** and
  **Cancel**; an upload interrupted by a reload or a dropped connection goes on where it stopped when the same file is
  picked again.
- **Administration › File management › Uploads** lists everyone's unfinished uploads, to cancel them. New settings
  **Largest file to upload** (no limit by default) and **Keep unfinished uploads for** (48 hours); new right
  `files.upload` (no group has it until an administrator ticks it).

## 1.58.0

### Added

- **Move and copy:** **Move to…** and **Copy to…** (item menu, selection bar) pick a folder of the library; **Cut**,
  **Copy** and **Paste** (Ctrl+X, Ctrl+C, Ctrl+V, the item menu's **Paste into**); dragging items onto a folder or a
  folder of the location bar moves them, with Ctrl copies them. They run in the background: a panel shows the progress,
  asks what to do when a name is taken (**Replace**, **Skip**, **Keep both**, the same for the rest), offers
  **Cancel**, and says what was done.
- Moved files and folders keep their tags, folder access rules, quick links, thumbnails and trickplay; a move into a
  place where more people would see a folder says who, and can keep the folder's access as it was. Copies can take the
  tags of their originals, leave out what one may not see, and are indexed at once.
- New rights `files.move` and `files.copy` (no group has them until an administrator ticks them); the setting **Free
  space kept by copies** (1 GB by default).

## 1.57.0

### Added

- **Delete and the trash:** **Delete** (key, item menu, selection bar) moves files and folders to the space's trash,
  with an **Undo**; **Shift+Delete** deletes for good. **Administration › File management › Trash** lists what was
  deleted, to **Restore** it where it was (with its tags and folder access) or delete it for good; **Empty the trash**.
  New rights `files.delete` and `files.deletePermanently` (no group has them until an administrator ticks them).
- What stays in the trash longer than **Days in the trash** (30 by default) is deleted by the new **Trash purge**
  task; **Always delete permanently** makes a space skip the trash.

## 1.56.0

### Added

- **Selecting folders:** folders have a checkbox too, and **Select all** (Ctrl+A) takes the folders with the files.
  The selection bar counts both, adds both to the quick links, and offers **Rename** when one item is selected;
  **Tags** still acts on the selected files.
- **Keyboard:** once an item has the focus, the arrow keys move to the item beside, above or below it, Home and End to
  the first and last, Space selects, Shift+arrows select as they go.

## 1.55.0

### Added

- **New folder and Rename:** the listing can now create folders (the button next to the folder's name, or
  **Shift+N**) and rename files and folders (**F2**, or the item's menu), for the groups given the new rights
  `files.createFolders` and `files.rename` (no group has them until an administrator ticks them). A renamed folder keeps
  its tags, folder access, quick links, schedules, thumbnails and trickplay; renaming is refused while a scan runs over
  it.
- **Item menu:** right-click a file or folder (or use its **⋮** button) for Rename, Tags and Copy full path;
  Shift+right-click still opens the browser's own menu.
- **Administration › File management:** whether new names must also suit Windows (on by default), and whether SFE's
  work files go in a hidden `.sfe` folder of the library or in the server's data folder.

## 1.54.0

### Added

- **Folder access:** a space's page has a new **Folder access** part, where you choose what a group, or one account,
  may do in a folder and below it: **Hidden**, **Read only** (use the files, but not change them) or **Read & write**.
  **Who has access** tells, for every account and the visitors, whether they see a folder and may change its files;
  **Re-point…** moves the rules of a folder renamed outside SFE to its new place.
- The listing only offers what folder rules allow where you are (tagging, downloading, editing pictures).

## 1.53.0

### Added

- **Folder permissions are applied everywhere:** listings, search, files, thumbnails, players, tags and their counts,
  duplicates, similar videos, moved files, quick links, shared links and the picture editor all follow the rules on
  folders. A folder someone may not see answers as if it weren't there; one that only leads to a folder they may see
  lists only the way there. Rules can't be set from the web page yet, so nothing changes until then.

## 1.52.0

### Added

- **Groundwork for folder permissions:** the accounts database gets a table for rules that will say which groups and
  accounts may see or change each folder. No rules can be set yet, so nothing changes: every right still applies to
  the whole space.

## 1.51.0

### Added

- **Read and Write rights:** every right is now either Read (it only looks) or Write (it changes something). The group
  and space editors list the Read rights first and mark the others **Changes things**, or **Changes files** for the
  rights that create, change or delete the library's files.
- **Read-only spaces:** editing a space, **Files read-only** switches off every right that changes files and
  **Read-only** every right that changes anything. In the configuration, `folder:write` and `kind:write` in
  `Sfe:Rights:Disabled` do the same for the whole server.

## 1.50.0

### Added

- **Animated images play on hover:** a large GIF, PNG, WebP or AVIF shown through its still thumbnail plays the image
  itself while the mouse rests on it (on a touch screen, while you hold it), like the trickplay of videos. Only files
  with several frames are played. Each user can turn it off (**Thumbnails › Play animated images on hover**,
  `Sfe:Thumbnails:AnimatedHover`).

## 1.49.0

### Added

- **Search on the administration page:** a box above the sections finds settings and pages by the words of their
  names, descriptions and choices (or their configuration key). Opening a setting found shows its page, scrolled to
  it, even when it is only in the full view.
- **Overview of the administration page:** it opens on every section you may use as a card, by group, with links to
  its pages; **Administration** and the group in the breadcrumbs lead back to it.
- Each page of the administration says what it holds under its title.

### Changed

- **My account** is now the first section of the administration page.
- **Administration reorganised:** the Preferences group is now **Viewing**; **Playback** has **Player**, **Streaming**
  and **Video streams** (moved from System); **System › Maintenance** holds backups and the server commands, which
  left **About**. Old addresses of moved pages still open them.

## 1.48.0

### Added

- **No setup code on the server's own computer:** a new install opened in a browser on the computer SFE runs on skips
  the code; from any other device, or once an account exists, it is still asked (`Sfe:Setup:LocalWithoutCode`).
- **Fast sign-in** for SFE used on one computer: the browser the portable package opens at start signs the
  administrator in without a password, with a one-time link. Offered by the setup page, switched in **Administration ›
  System › Start-up**; only while the server has a single administrator (`Sfe:FastSignIn`).
- **Administration › System › Start-up:** whether the portable package opens the browser when it starts.
- `--no-browser` on the command line keeps the browser closed, whatever the settings say.

## 1.47.0

### Added

- Settings `Sfe:Security:TrustedProxies` (reverse proxies on other computers whose forwarded visitor address and
  scheme are believed) and `Sfe:Security:RequireHttps` (secure cookie and HSTS when the app is only used over https).
- `Sfe:Security:AccountLockoutFailures` (default 50): failures from all addresses together that lock a local account.
- **Recover the account** on the sign-in page: a server administrator locked out, or who forgot the password, asks
  for a one-time code that the server shows only where it runs (its console, and `recovery-code.txt` beside the setup
  code); with it, a new password unlocks the account and signs it in, ending its other sessions. No restart needed.
- **Security warning**, a new severity in **Logs**: wrong passwords (with the name and address), blocked addresses
  and locked accounts, wrong setup codes, account recoveries, the first administrator and installation changes are
  logged with it, and can be filtered apart from other warnings (`SEC` in the log files).

### Changed

- **Sign-in:** wrong passwords block the address they come from for that account (`MaxFailedSignIns`,
  `LockoutMinutes`), no longer the account itself: a stranger can't lock the administrator out. Unknown names are
  answered the same way.
- Behind a reverse proxy on the same computer, the visitor's own address counts for the sign-in limits, and https at
  the proxy marks the sign-in cookie secure.
- **Administration › System › Installation** needs a signed-in server administrator as well as the setup code.
- The setup code SFE makes itself is shown in its console and kept in its file, never written to the server's log;
  the update renews it.
- Searches, thumbnails, previews, video conversions and picture uploads are limited per account (or address): one
  visitor flooding them can no longer slow the server down for everyone.

### Fixed

- Files of a library that a browser could run as a page (`.html`, `.svg`, `.xml`…) are downloaded instead of opened
  in the app: such a file could act with the rights of whoever opened it. Every file is also served in a sandbox.
- Every page of the app carries a content security policy and refuses to be framed by other sites.
- The keys, setup code, databases and settings of SFE are never served as files of a library, even when a space's
  folder holds them or a folder link leads to them.
- ffmpeg can't be set to a program that isn't ffmpeg, or to one in a space's folder.
- The setup and update pages no longer tell visitors where the setup code file is once it is not needed.

## 1.46.0

### Added

- **Logs** (Administration › Server › Logs): every message, warning and error the server logs is kept and can be
  searched by text, severity, days and space; an entry opens on its error's details.
- New right **See the logs** (`logs.view`), given to the Administrators group of every space: a space's
  administrators see the entries of their space, server administrators those of the whole server.
- Settings (whole server) to choose how long log entries are kept (30 days by default) and to also save them to a
  file per day in the logs folder (`data/logs`, next to the database and the thumbnails; `Sfe:Logs:Directory`).

## 1.45.0

### Changed

- Administration: server administrators pick whether a settings page edits **This space** or the **Whole server**
  with tabs instead of radio buttons.
- Switching between **This space** and **Whole server** keeps the changes not saved yet in each (a dot marks the tab)
  instead of asking to forget them; **Save** saves both, **Discard** forgets both.
- Saving settings sends only the settings changed (new `PATCH api/settings` and `PATCH api/me/preferences`): a space or
  a user keeps following the server for everything else, even when the page was opened before the server changed.

## 1.44.0

### Added

- **Cinema mode** (C) in the viewer's video player: the video spans the whole width of the window, as YouTube's
  theater mode; the browser remembers the choice.

### Fixed

- A small video in the viewer no longer cuts off the player's buttons: the player is at least as wide as its controls,
  and on a phone the buttons that don't fit go to a second row.
- A video in cinema mode, or a picture in the viewer, no longer overflows below a title bar made taller by tags (which
  showed a scrollbar).
- Some folders showed a scrollbar with nothing to scroll: the page now always keeps the scrollbar's room.

## 1.43.0

### Added

- Setting SFE up needs no file edited: without a configured setup code, SFE makes one at its first start, shows it in
  its window (or log) and keeps it in `data/setup-code.txt`; type it with or without the dashes.
- The setup page now has three steps: the code, **your library** (pick its folder on the server's disks, choose this
  computer only or the whole network, and find ffmpeg), then the administrator.
- **Administration › System › Installation** changes the library folder, the network address and ffmpeg later, after
  entering the setup code. Values set by environment variables or the command line are shown locked.
- The Windows portable package opens the app in the browser when started, and installs itself as a Windows service with
  `install-service.cmd` (`Sfe.Api.exe --install-service`).

### Changed

- A configuration file that isn't valid JSON, or a port already in use, now stops SFE with a plain message (the file's
  line, what to do) instead of an error dump.
- A library folder that doesn't exist (an unplugged drive) is said as such in the listing, with a link to pick another.
- The repository's `appsettings.json` is neutral (library root `~`, SQLite); a machine's own values go in
  `appsettings.Local.json`.

## 1.42.0

### Added

- **Quick links** at the top of the sidebar: keep the folders and files you use most at hand, as in Windows Explorer.
  Drag folders, files or the selection onto it, or use the pin buttons (folder bar, folder cards, selection bar,
  viewer); drag the links to reorder them, or use **Edit** on phones. Each account has its own links in each library
  (right `account.quickLinks`, given to the Administrators and Users groups).

## 1.41.0

### Added

- The viewer shows a picture larger than the screen at its actual size (100%): click it, press `Z` or use the new
  button; drag it with the mouse or a finger to look around, scroll it with the wheel, and click again (or `Esc`) to
  see it whole.

## 1.40.0

### Changed

- Thumbnails and trickplay are stored in two levels of folders (`ab/cd/...`) instead of one folder per folder of the
  library, so the cache folder no longer holds hundreds of thousands of folders. The update moves the existing ones.

## 1.39.0

### Added

- Indexed folders show how many files and sub-folders the index has directly inside them, over their card or at the
  end of their line.
- Videos the browser can't play (AVI, WMV, HEVC...) are converted by the server with ffmpeg while they play, from where
  they were; seeking works across the whole video. **Administration › Playback** can convert every video, or none, and
  limits how many at once (right `files.stream`).
- **Administration › System › Video streams** lists the videos being converted, with who watches them (right
  `server.streams`).
- **Administration › System › About** shows the library's folder on the server and whether ffmpeg was found, where,
  and its version.

### Changed

- The search syntax help is now a dialog, grouped by theme and laid out for phones too; click an example to add it
  to the search, or press `?` to open it.
- Clicking your name in the ⋮ menu opens **Administration › My account**.
- The administration page shows breadcrumbs above each page's title, following the section and page open; on phones
  they lead back to the list of sections.

## 1.38.0

### Added

- Listings show files and folders that are not indexed yet faded, with a hint saying a scan will add them.

## 1.37.0

### Added

- Update page: after an upgrade, SFE stays closed until someone with the setup code confirms the update of its
  databases and settings. Nothing is changed before that, and the page lists every change since the installed version.
- Backups: a zip file of the databases and configuration files, from Administration › System › About or from the update
  page before updating (right `server.backup`).

### Fixed

- SQLite index: parallel work (a scan's folders, simultaneous requests) could share one database connection, which
  made a scan skip a folder now and then with "database is locked" or hang.

## 1.36.0

### Added

- Search by kind of file: `type:image`, `type:video`, `type:audio`, `type:other`, by the extension lists of
  Administration › Scanning › File types; several are either-or, and `-type:` excludes.
- Search results sort by **Duration** and **Resolution** (width × height). Files whose value is known come first, the
  others follow by path.
- The search box's syntax help lists `type:` and `d:`.

## 1.35.0

### Added

- Picture editor: the viewer's **Edit** button (or E) crops a picture, freely or to an aspect ratio, and saves the
  result as a new file, never changing the original.
- Rights `files.edit` and `settings.editor`, and a Picture editor settings page (output folder, quality).
- New rights now reach the built-in groups of existing spaces once, on the first start after an upgrade.

## 1.34.1

### Fixed

- A space's own administrator could add accounts from other spaces (or from none) to the space's groups. Only server
  administrators can do that now.

## 1.34.0

### Added

- Accounts, groups and rights, with a setup page that creates the first administrator, and a sign-in page.
- Guest access for visitors without an account.
- Several libraries (spaces) on one server, each with its own root, index and caches.
- Settings by level: server, library and personal preferences (My account), with locks.
- Links given to an external media player now expire.

## 1.33.0

### Added

- Durations of videos and audio files, read by scans: shown on tiles, rows and in the viewer, and searchable with `d:`
  (`d:>10m`, `d:1:30`).
