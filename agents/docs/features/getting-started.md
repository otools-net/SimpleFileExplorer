# Getting-started checklist

Code: client `src/app/admin/getting-started/` (`getting-started-items.ts`, `getting-started-lines.ts`,
`getting-started.component.*`), `styles/getting-started.css`, the overview and phone list of `admin-page.component.html`,
`SetupPageComponent.finish`; server `Sfe.Core/Access/GettingStarted.cs` (`GettingStartedItems`), route
`GET api/access/getting-started` (`GettingStartedEndpoints`). User docs: `docs/administration/README.md#getting-started`.

- **Split:** the server says which items the session sees and which are done (`{ items: [{ id, done }] }`, in
  order); the client (`GETTING_STARTED`) says what each is called, its icon, its page (`home`) and its wizard. An id
  the client doesn't know is left out, so the server may add one first.
- **Who sees an item:** the rights of its page (`GettingStarted.Items`): `library` = `server.settings`, `groups` and
  `folders` = `tenant.members`, `accounts` = `tenant.members` or `server.users`, `connection` = `connections.library`
  or `connections.server`. The route requires any of them (403 otherwise). `accounts` has `onlyIn: 'external'`
  (Local mode: administrators only), left out by the client.
- **Done** (one `EXISTS` each, nothing counted: [scale](../development/scale.md)): `library` the space's folder exists;
  `groups` a group with `BuiltIn == null`; `folders` any folder rule of the space; `accounts` a member of one of the
  space's groups other than the asking account; `connection` one the space can see (its own, a server one offered to
  it, or the account's own). Read once when the overview opens, not polled.
- **Wizards:** an item names its wizard by its registry id (`folders` → `share-folder`, `connection` →
  `add-connection`, 5.3b / 5.3c). While `findWizard` doesn't know it, or `canOpenWizard` refuses, the item opens its
  page; registering the wizard switches it with no change here.
- **Hidden** by its ×: `localStorage` `sfe.gettingStarted.hidden.<tenant>.<user>` (`preferences.ts`), per browser,
  account and space (default taken: like the notifications, no server setting). While items are left, a
  "Getting started: n of m done" button stays in its place; all done and hidden: nothing.
- Where it shows: the overview's top (computers) and above the list of sections on phones (`sectionList()`, not while
  searching). Its page links carry the admin page's history state and `replaceUrl` like the cards (phones push).
- The setup ends on `/admin` (recovery and the restart step still open the library), so a new administrator sees it.
- Screenshot `admin-getting-started.png` (scene `admin`, clipped to `.getting-started`).
