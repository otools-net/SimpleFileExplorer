# The administration page

**Administration** in the [⋮ menu](../user-guide/signing-in.md#the--menu) opens the administration page: the settings,
the tag list, duplicates, accounts, background tasks and your own account.

![The administration page](../images/admin-interface-browsing.png)

The sidebar groups the sections; the section open lists its pages under it. The arrow at the top left goes back to the
folder you came from, scrolled where you left it.

Breadcrumbs above the page's title say where you are (**Administration › Library › Tags › All tags**) and follow you
from page to page: click the section to open its first page, or **Administration** to go back to the first section.

| Group | Section | Pages |
|---|---|---|
| Preferences | [Interface](interface.md) | Appearance, Browsing, Search |
| | [Playback](playback.md) | video player, external player, data server |
| Library | [Scanning](scanning.md) | What to scan, File types, Indexing |
| | [Tags](tags.md) | All tags, Settings |
| | [Duplicates & moves](duplicates-and-moves.md) | Duplicates, Similar videos, Moved files, Settings |
| Media | [Thumbnails](thumbnails.md) | previews in listings and the viewer |
| | [Trickplay](trickplay.md) | Activity, Settings |
| | [Picture editor](picture-editor.md) | where edited pictures go |
| Access | [Spaces & accounts](spaces-and-accounts.md) | Spaces, Accounts |
| Server | [Background tasks](background-tasks.md) | Tasks, Schedules, History, Settings |
| | [System](system.md) | version, API reference, shutdown/dismount |
| Account | [My account](my-account.md) | Preferences, Password |

Each page shows only to the accounts with its [right](../reference/rights.md): an account without any right to change
something sees only **My account**, a visitor without an account no administration page at all.

## Settings pages

- Each setting has a description under it.
- Changes on several pages are kept together until **Save** in the bar at the bottom. A dot marks the sections and
  pages with unsaved changes, **Discard** drops them, and leaving the page asks first.
- Saved values apply right away, for everyone using the space, and take precedence over `appsettings.json` and
  environment variables. **Reset to defaults** goes back to those.
- Errors are shown next to their fields, and their sections and pages are marked in red.
- The page shows the most used settings; tick **Show all settings** in the bottom bar for the others (each browser
  remembers the choice).
- Server administrators choose with the tabs at the top of a settings page whether they edit **This space** or the
  **Whole server** (the defaults of every space), and can **lock** a setting for the levels below: see
  [Settings by level](../reference/settings-levels.md). Changes made in one tab are kept while the other shows (a dot
  marks the tab); **Save** saves both tabs, the whole server's first, and **Discard** forgets both. When the values of
  a tab are refused, that tab opens with its errors; the other tab's saved changes stay saved.
- Only the settings you changed are saved: a space keeps following the server for the others, even if someone changed
  the server's settings while your page was open.
- A setting you can't change says why: set by the configuration, locked by the server or the space, a server setting,
  or not your section.

Some settings are never on this page, because they decide which programs run or where data lives: the library root,
the thumbnail cache folder, the database, the ffmpeg path, shutdown/dismount, and everything about the setup and
sign-in. Set them in the [configuration](../configuration.md).

## On a phone

<img src="../images/phone-admin.png" alt="The administration page on a phone" width="300">
<img src="../images/phone-admin-section.png" alt="A section on a phone, with its breadcrumbs and pages as chips" width="300">

On phones the page opens on the list of sections, each one then a page of its own with a back arrow to the list and its
pages as chips at the top. The breadcrumbs leave the group out there, and **Administration** in them returns to the list
of sections.
