# Accounts, groups, sign-in directories

Code: `src/Sfe.Core/Access/` (`AccessService`, `SignInService`, `IUserDirectory`, `LocalUserDirectory`,
`PasswordHashing`, `AccessChanges`), `src/Sfe.Api/Endpoints/AccessEndpoints.cs`; client `src/app/access/` (People:
Accounts, Groups, GuestAccess; Libraries), `src/app/account/` (LoginPage, AccountSection). Sessions:
[sessions-and-pipeline.md](../architecture/sessions-and-pipeline.md).

- **Directories:** `SignInService` asks the `IUserDirectory`s in `Sfe:Security:Directories` order (empty = local);
  `DirectoryStatus.Unknown` passes to the next. A non-local account gets a `Users` row on first sign-in (Source +
  ExternalId), its groups with an `ExternalName` follow the directory's groups at each sign-in.
- Only `LocalUserDirectory` exists (lockout after `MaxFailedSignIns`, PBKDF2-SHA512 210k with rehash of older hashes).
- **Space administrators** (`tenant.members` without `server.users`) only touch accounts already in a group of their
  space: `AccessEndpoints` 404s the others, keeps a shared account's other fields, and passes `membersOfTenantOnly` to
  the group methods. An account in their space alone (`OwnedBy`) is fully theirs (password too), so any route putting
  outside accounts into a space's group must stay server-wide.
- Rights of an account: [rights.md](rights.md).
