# Changes from the .NET Framework version

SFE is a port of the ASP.NET MVC 5 / .NET Framework 4.8 `SimpleFileExplorer` to .NET 10: a JSON API plus a single-page
web client. The old app's database and thumbnail cache are not reused: scan the library again.

- **New UI:** grid and list layouts, sorting, infinite scroll that restores your position, a full-screen viewer (arrow
  keys and swipe), search highlighting with links to the folder, live scan progress, light and dark themes, usable on
  phones. Plus tags, trickplay, duplicates and moved files, accounts and spaces.
- **Cross-platform:** Linux and macOS as well as Windows (case-sensitive paths, `/` roots, `~`, systemd).
- **Fixes:** paths can't escape the root; rescans remove deleted folders (including top-level ones under a root scanned
  from the UI); bounded scan parallelism; an unreadable file no longer aborts a scan; `%`, `_` and `[` match literally;
  size filters keep all digits without a unit; an alternative made only of exclusions works; the unbounded thumbnail
  memory cache is gone; folder links looping back are not followed forever; an ffmpeg timeout no longer caches "no
  thumbnail"; the dismount timeout works.
- **Behaviour changes:**
  - Hidden and system files (`Thumbs.db`, `desktop.ini`) are skipped unless `ShowHiddenItems` is set.
  - New thumbnails are scaled to `Thumbnails:Width`; old full-size ones are still served. Browsers don't cache images,
    so a replaced photo shows up right away.
  - Scans only accept paths inside the root; shutdown and dismount are opt-in and use POST (both removed in 1.80).
  - Search results are ordered by full path (folder by folder, then by name) by default and can be sorted like a
    folder; either way they page quickly in a library of millions of files.
  - Tagging a file that isn't indexed yet indexes only that file, not the whole folder.
