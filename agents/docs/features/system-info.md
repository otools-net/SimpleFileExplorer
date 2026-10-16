# System section (About, Video streams)

Code: client `src/app/admin/` (System section); route `GET api/system/info`.

- `GET api/system/info` (`server.settings`): the library's `LibraryPaths.Root` and `Ffmpeg` (configured, found path,
  `GetVersionAsync` read once), shown in System › About.
- Playback › Video streams polls `api/streaming/sessions` ([video-streams.md](video-streams.md)).
- System › Installation: [installation-page.md](installation-page.md). Backups: [backups.md](backups.md).
- The version and commit shown: [app-version.md](../architecture/app-version.md).
