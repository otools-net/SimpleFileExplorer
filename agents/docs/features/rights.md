# Rights

Code: `src/Sfe.Core/Access/RightCatalog.cs` (+ `Rights` ids), `RightsEvaluator.cs`, `AccessService.cs`;
`src/Sfe.Api/Security/` (`RequireRight`, policy provider, `CurrentAccess`); client `core/rights.ts`,
`core/config.service.ts` (`can`/`canAny`). User docs: `docs/reference/` (rights).

## Adding a right (rules)

- Every route names its right(s): `.RequireRight(Rights.X)` (any of several), or `.AllowAnonymous()` for the
  session/setup routes (a route checking a token itself adds `RightMetadata` too). `AccessTests.EveryRouteNamesARight`
  fails otherwise.
- A new function gets a right in `RightCatalog` (scope, `GuestAllowed` = false for anything that changes something or
  tells about the server, default groups), the same id in the client's `core/rights.ts`, and the client hides what
  needs it (`config.can(...)`, `AdminSubpage.rights`).
- A right added after 1.34 gets `since: RightCatalog.Revision + 1` (`RightDefinition.Since`):
  `AccessService.GrantNewRightsAsync` (Program.cs, each start) gives it once to the built-in groups of existing spaces
  by `DefaultGroups`, tracked by `Tenants.RightsRevision` (null = 0); new spaces get `Revision`. Without it, existing
  installs never get the right.

## How rights are evaluated

- `RightsEvaluator.GetAsync(new AccessSubject(tenant, user))`: server administrators everything; others the union of
  their groups' rights in the library; a visitor without an account the Guests group's when the library has
  `GuestAccess` (and `Sfe:Security:AnonymousGuests`); minus the library's `TenantDisabledRights`, minus guest-forbidden
  rights for guests (anonymous or `UserKind.Guest`), minus `Sfe:Rights:Disabled` (applied at each read).
- A library membership is a group in it.
- `CurrentAccess` picks the library: the cookie's if the account still reaches it, else its first; for visitors the
  `sfe.guest` cookie's if open to guests, else the first library.
- Settings rights by prefix: [settings-new-key.md](settings-new-key.md). Space administrators: [accounts.md](accounts.md).
