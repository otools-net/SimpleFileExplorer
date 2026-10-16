# Settings by level

A setting's value comes from up to four levels, each over the one before:

1. **Environment**: `appsettings.json`, environment variables, the command line ([Configuration](../configuration.md));
2. **Server**: the administration page's settings, "the whole server" (server administrators): every space's
   defaults, saved in `settings.json`;
3. **Space**: the same pages, "this space" (its administrators, section by section with their rights);
4. **User**: [My account › Preferences](../administration/my-account.md), each user's own.

![Choosing the level at the top of a settings page](../images/admin-interface-browsing.png)

## How low a setting goes

Each setting has a lowest level:

- what the server shares (scan and ffmpeg parallelism, time limits, page size caps, the data server, task log
  retention) stops at the **server**;
- what a space decides (file types, skipped and excluded folders, thumbnails, trickplay, duplicates, the external
  player...) at the **space**;
- personal choices (theme, page sizes, folder pictures, resolution and duration display, seek step, loop, hover
  previews of videos and animated images, search and sidebar sizes) go down to each **user**.

The folders of a space (excluded folders, trickplay folders, the folder for edited pictures) are only set in the space
itself.

## Locks

A level can **lock** a setting: the levels below keep its value.

- The environment locks with `Sfe:Locked` (e.g. `Sfe__Locked__0=player.*`).
- A settings page offers **Lock for spaces and users** (server) or **Lock for users** (space) next to each setting the
  levels below could change.

A setting you can't change says why (set by the configuration, locked by the server or the space, a server setting, or
not your section). Only values that differ from the level above are saved, so a level keeps following the one above for
the rest; **Reset to defaults** forgets a level's own values. The bottom bar says whether the page follows the level
above ("Following the server's settings").
