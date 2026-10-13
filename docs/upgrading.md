# Upgrading

Install the new version over the old one (or replace the portable package, keeping its `appsettings.json` and `data`
folder). The index, tags, thumbnails, trickplay and settings are kept: new database tables and columns are added by
the update described below, on SQL Server and SQLite alike. The notes after it say what you may notice.

## The update page (1.37 and later)

When the new version starts on an install of an earlier version, it changes nothing yet: every page shows the **update
page** and the app can't be used, by anyone, until the update is done.

![The update page](images/update-page.png)

1. The page says which version the databases belong to and lists every change since then (from the
   [changelog](../CHANGELOG.md)).
2. It asks for the **setup code**, the same as for the [first start](first-start.md#the-setup-code). If the server has
   none any more, the page says where to set it for the way your server runs; set it and restart.
3. **Download a backup** (recommended) saves a zip file of the databases and configuration files, to go back to the
   earlier version if needed (see [Backups](administration/system.md#backup)).
4. **Start the update** updates the databases (accounts, then every space's index) and the settings, and follows it on
   the page. **Open SFE** then opens the library; sessions of before keep working.

If the update stops on an error, the page shows it: fix the cause (the server's log has the details) and start it
again. Before you confirm, nothing in the databases or the settings file has been changed, so you can still go back
to the earlier version by installing it again.

- Installs from before 1.37 recorded no version: the page says "an earlier version" and lists every change.
- A database that belongs to a **later** version than the one installed is never touched: install that version again,
  or restore a backup made with this one.
- While the database can't be reached at all (e.g. SQL Server starts after SFE), the page says so and SFE keeps trying.
- With `Sfe:Database:MigrateOnStartup` set to `false`, you update the schema yourself and SFE doesn't check versions.

## To 1.39

- Videos the browser can't play are now converted by the server while they play, when ffmpeg is found
  ([Playback](administration/playback.md#videos-converted-by-the-server)); turn it off with **Convert videos: Never**
  (`Sfe:Streaming:Mode` = `Off`). The groups Administrators and Users of existing spaces get the new right
  `files.stream` once, at the first start; guests don't.
- Nothing changes in the databases or the settings file.

## To 1.37

- The first start of 1.37 shows the update page above (installs before it recorded no version). The accounts'
  database gets a small `ServerProperties` table with the version (SQL Server: a migration; SQLite: a new table).

## To 1.36

- The index gets an indexed `Pixels` column (width × height, computed by the database) and an index on durations, for
  the new Duration and Resolution sorts of searches (SQL Server: a migration; SQLite: added on start-up). The values
  come from what scans already read: no scan is needed. On a large SQLite index, that first start takes a little
  longer while the indexes are built (the log says so).

## To 1.35

- The rights added by 1.35 (`files.edit`, `settings.editor`, for the [picture editor](user-guide/picture-editor.md))
  are given once, at the first start, to the built-in groups of every existing space as a new space would have them:
  Administrators and Users may edit pictures, Administrators change the editor's settings. Take them away afterwards
  if you wish: later starts don't give them back.
- The accounts' database gets one new column (SQL Server: a migration; SQLite: added on start-up).

## To 1.34 (accounts)

- After the upgrade, **set a setup code** and create the administrator ([First start](first-start.md)): until then the
  app only shows the setup page. Your index, tags, thumbnails, trickplay and settings are kept: they become the first
  space's.
- The accounts' tables are added to the existing database on the first start (SQL Server: a migration; SQLite: new
  tables in the same file).
- Excluded folders and trickplay folders move from the server's settings file to the first space's settings (the log
  says so); everything else in `settings.json` keeps applying as the server level.
- Media players opened with ▶ now get links that expire (see [External player](user-guide/video-player.md#external-player)).
- Browsers keep their own theme and search history as before.

## To 1.33 (durations)

Durations of videos and audio files are new in 1.33. The database gets its new column on start-up, and the next scan
of each folder reads the duration of the videos and audio files already indexed, once: expect that scan to take about
as long as the first scan of those files did. Until then, their duration is unknown (not shown, not found by `d:`).
