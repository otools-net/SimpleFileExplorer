# Rights

Every function and every setting needs a right; what you don't have is hidden, and the server refuses it anyway. Rights
are given by the groups of a space ([Spaces & accounts](../administration/spaces-and-accounts.md#groups)). Rights marked
"no" are never given to guests (visitors without an account, and guest accounts).

| Right | Allows | Guests | Default groups |
|---|---|---|---|
| `library.browse` | browse, search, see the scan's progress | yes | all |
| `files.view` | open pictures, play videos and audio, thumbnails, trickplay | yes | all |
| `files.download` | download files | yes | Administrators, Users |
| `files.external` | open files in an external player (a link of its own) | yes | Administrators, Users |
| `files.copyPaths` | copy full paths (shows the server's folders) | no | Administrators |
| `files.edit` | edit pictures (crop) and save the result as a new file | no | Administrators, Users |
| `files.stream` | play videos converted by the server (ffmpeg) when the browser can't play them | yes | Administrators, Users |
| `tags.view` | see tags, the tag sidebar | yes | all |
| `tags.assign` | tag files, folders and searches | no | Administrators, Users |
| `tags.manage` | create, edit, delete tags; clear the tag history | no | Administrators |
| `index.scan`, `index.remove` | scan; remove folders from the index | no | Administrators |
| `tracking.duplicates`, `tracking.duplicatesRemove`, `tracking.similar`, `tracking.missing` | duplicates, deleting copies, similar videos, moved files | no | Administrators |
| `trickplay.manage` | trickplay activity | no | Administrators |
| `tasks.view`, `tasks.run`, `tasks.schedule` | background tasks, running them, schedules | no | Administrators |
| `settings.interface`, `.playback`, `.scanning`, `.tags`, `.tracking`, `.thumbnails`, `.trickplay`, `.tasks`, `.editor` | the space's settings, by section | no | Administrators |
| `tenant.members` | the space's accounts, groups and visitors | no | Administrators |
| `account.preferences`, `account.password` | own preferences and password | no | Administrators, Users |
| `account.quickLinks` | own [quick links](../user-guide/browsing.md#quick-links) in the sidebar | no | Administrators, Users |
| `system.apiDocs` | the API documentation (`/api/docs`) | no | Administrators |
| `server.settings`, `server.tenants`, `server.users`, `server.shutdown`, `server.dismount`, `server.backup`, `server.streams` | the server's settings, spaces, every account, system commands, backups, the list of video streams | no | server administrators only |

**Server administrators** have every right in every space, plus the server's own.

## Who decides

Three levels decide who gets what:

1. the **environment** can switch a right off for everyone, even administrators (`Sfe:Rights:Disabled`, e.g.
   `files.download` or `tracking.*`);
2. a server administrator can switch rights off in one **space** (Edit space › **Rights switched off in this space**);
3. **groups** give the rest.

Rights added by an upgrade are given once to the built-in groups of existing spaces (see [Upgrading](../upgrading.md)).
