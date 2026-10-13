# Upgrading

Install the new version over the old one (or replace the portable package, keeping its `appsettings.json` and `data`
folder). The index, tags, thumbnails, trickplay and settings are kept: new database tables and columns are added on
the first start, on SQL Server and SQLite alike. The notes below say what you may notice.

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
