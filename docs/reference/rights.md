# Rights

Every function and every setting needs a right; what you don't have is hidden, and the server refuses it anyway. Rights
are given by the groups of a space ([Spaces & accounts](../administration/spaces-and-accounts.md#groups)). Rights marked
"no" are never given to guests (visitors without an account, and guest accounts).

Each right is of one of two kinds (column **Kind**):

- **Read**: it only looks (browse, play, download, see duplicates, read the logs…).
- **Write**: it changes something. **Write (files)** marks the rights that create, change or delete the library's
  files on disk; the other Write rights change the space (its index, tags, settings or accounts) or your own account,
  never the files. The group editor marks them **Changes files** and **Changes things**.

| Right | Allows | Kind | Guests | Default groups |
|---|---|---|---|---|
| `library.browse` | browse, search, see the scan's progress | Read | yes | all |
| `files.view` | open pictures, play videos and audio, thumbnails, trickplay | Read | yes | all |
| `files.download` | download files | Read | yes | Administrators, Users |
| `files.external` | open files in an external player (a link of its own) | Read | yes | Administrators, Users |
| `files.copyPaths` | copy full paths (shows the server's folders) | Read | no | Administrators |
| `files.edit` | edit pictures (crop) and save the result as a new file | Write (files) | no | Administrators, Users |
| `files.stream` | play videos converted by the server (ffmpeg) when the browser can't play them | Read | yes | Administrators, Users |
| `files.createFolders` | [create folders](../user-guide/managing-files.md#new-folder) | Write (files) | no | none |
| `files.rename` | [rename files and folders](../user-guide/managing-files.md#rename) | Write (files) | no | none |
| `tags.view` | see tags, the tag sidebar | Read | yes | all |
| `tags.assign` | tag files, folders and searches | Write | no | Administrators, Users |
| `tags.manage` | create, edit, delete tags; clear the tag history | Write | no | Administrators |
| `index.scan`, `index.remove` | scan; remove folders from the index | Write | no | Administrators |
| `tracking.duplicates`, `tracking.similar` | duplicates, similar videos | Read | no | Administrators |
| `tracking.duplicatesRemove` | delete a copy of a duplicate from the disk | Write (files) | no | Administrators |
| `tracking.missing` | moved files: list, relink, forget | Write | no | Administrators |
| `trickplay.manage` | trickplay activity | Write | no | Administrators |
| `tasks.view` | background tasks and their history | Read | no | Administrators |
| `tasks.run`, `tasks.schedule` | running background tasks, schedules | Write | no | Administrators |
| `logs.view` | the [log](../administration/logs.md) of the space (server administrators: of the whole server) | Read | no | Administrators |
| `settings.interface`, `.playback`, `.scanning`, `.tags`, `.tracking`, `.thumbnails`, `.trickplay`, `.tasks`, `.logs`, `.editor`, `.files` | the space's settings, by section | Write | no | Administrators |
| `tenant.members` | the space's accounts, groups and visitors | Write | no | Administrators |
| `account.preferences`, `account.password` | own preferences and password | Write | no | Administrators, Users |
| `account.quickLinks` | own [quick links](../user-guide/browsing.md#quick-links) in the sidebar | Write | no | Administrators, Users |
| `system.apiDocs` | the API documentation (`/api/docs`) | Read | no | Administrators |
| `server.settings`, `server.tenants`, `server.users`, `server.shutdown`, `server.dismount` | the server's settings, spaces, every account, system commands | Write | no | server administrators only |
| `server.backup`, `server.streams` | backups, the list of video streams | Read | no | server administrators only |

**Server administrators** have every right in every space, plus the server's own. The file management rights
(`files.createFolders`, `files.rename`) are in no group by default, even after an upgrade: tick them for the groups
that may change the library's files.

Renaming a folder also needs the right everywhere below it: someone kept out of a sub-folder, or with it read-only,
can't rename (nor later move or delete) the folder holding it.

## Who decides

Three levels decide who gets what:

1. the **environment** can switch a right off for everyone, even administrators (`Sfe:Rights:Disabled`, e.g.
   `files.download` or `tracking.*`; `folder:write` switches off every right that changes files, `kind:write` every
   right that changes anything: a read-only server);
2. a server administrator can switch rights off in one **space** (Edit space › **Rights switched off in this space**;
   **Files read-only** and **Read-only** tick the rights that change files, or every right that changes something);
3. **groups** give the rest;
4. **folder rules** then say where: a group, or one account, may be kept out of a folder (Hidden), only use its files
   (Read only: the rights marked *Changes files* don't apply there) or have its rights in full
   ([Folder access](../administration/spaces-and-accounts.md#folder-access)).

Rights added by an upgrade are given once to the built-in groups of existing spaces (see [Upgrading](../upgrading.md)).
