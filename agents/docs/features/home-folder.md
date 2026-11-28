# Home folder

Code: `src/Sfe.Core/Access/HomeFolderService.cs` (`HomeFolderChoice`, `GroupHomeFolder`), entity
`Data/Entities/Access/HomeFolder.cs`, `UserGroup.HomeFolder`, `GroupChanges` (the group's default checked), route
`api/me/home-folder` in `src/Sfe.Api/Endpoints/HomeFolderEndpoints.cs`; client `src/app/library/home-folder/`
(`HomeFolderService`, `section[sfeHomeLink]`), the item command `homeFolder` (`files/organising-commands.ts`), the
group editor's **Home folder** field. User docs: `docs/user-guide/browsing.md#home-folder`,
`docs/administration/spaces-and-accounts.md#home-folders`.

- An account's home in a space: its own choice (`HomeFolders` table, key `(UserId, TenantId)`, a root-relative path)
  over its groups' default (`Groups.HomeFolder`, nullable). **Not a user setting**: `UserSettings` rows have no space,
  and a home is a path of one space (decision of 2026-11-28, recorded in the 2.0 plan).
- A group's default may hold `{user}` (any case): `HomeFolderService.Expand` puts the account's sign-in name there,
  with `/` and `\` written `_` (one folder); a name making no path (`..`) gives no home. The default is checked as a
  path (`CleanDefault`: `NormalizeRelative`, 260 characters) but not on the disk: it names folders not made yet.
- Several groups with a default: hand-made groups first, then the built-in ones in enum order, then by name. Server
  administrators outside the space's groups only have their own choice.
- `GroupInput.HomeFolder`: **null keeps** the current default (older clients and scripts don't send it), empty or
  blank clears it; the root is never a group default (empty = none). An account may choose the root (`PUT` with `""`).
- Never created: `exists` in the view is false for a folder missing, not a folder, or hidden by folder rules
  (`state.At(...).IsVisible`); `PUT` only takes a folder that exists and the account sees (404 otherwise).
- Right `account.homeFolder` (`since: 15`, Members, never guests): every route; visitors get 403 like quick links.
  The Guests group's editor hides the field.
- Renames and moves: `PathMoves` calls `MovePathsAsync` for folders: choices and defaults at or below the old path
  follow it; for a default only the part before `{user}` can match (`Homes` → `People` moves `Homes/{user}`; renaming
  `Homes/tom` changes nothing).
- Client: `HomeFolderService.load()` from the sidebar's Home (`ngOnInit`); Home hidden in a connection (`choosable`)
  and without a home; reuses the quick links' styles (`.quick-link`, `.missing`, `.current`). Menu ids: `homeFolder`
  in the item menu and the current folder's menu (`FolderMenu.shared`) — **Set as home folder**, or **Stop using as
  home folder** on the chosen one, hidden on the one a group gives; `clearHomeFolder` in Home's own menu.
- The dashboard (2.0 step 5.5) shows it too.
