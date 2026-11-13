# Connections

**Connections** link SFE to storage outside its libraries: Dropbox, for now. Whoever runs the server registers an app
at Dropbox once; then people with the rights connect their Dropbox, or one for a space or for the whole server.

This version makes, signs in again and manages connections. Browsing their files, copying and moving them to and from
the library come with a later version.

## Levels

| Level | Who uses it | Who adds it | Right |
|---|---|---|---|
| **Only me** | the account that added it, in every space it opens | that account, in **My account › Connections** | `connections.own` |
| **This space** | the space's groups chosen for it | the space's administrators | `connections.library` |
| **The server** | the spaces it is offered to, then their groups | the server's administrators | `connections.server` |

The three rights go to the **Administrators** group of every space, never to guests
([rights](../reference/rights.md)). A connection of one account stays private: the server's administrators see that it
exists and can remove it, never open or change it.

## Setting up Dropbox (server administrators)

![Administration › Connections › Dropbox](../images/admin-connections-setup.png)

1. At Dropbox's App Console, create an app with **Scoped access** (the whole Dropbox, or an app folder only), and give
   it the permissions `account_info.read`, `files.metadata.read`, `files.content.read` and `files.content.write`.
2. In the app's settings at Dropbox, add the **Return address** this page shows to its redirect addresses, exactly as
   shown (**Copy** copies it). It is the address people reach SFE by; behind a proxy or under a sub-folder, open this
   page through that address. When Dropbox can't send people back to it, they sign in by copying a code instead.
3. Type the app's **app key** in **Administration › Connections › Settings** (`connections.dropbox.appKey`). Dropbox is
   offered from then on.
4. The **app secret** is optional: sign-ins work without it. Typed here, it is kept by the server in a file only the
   server's account can read, never shown again, and not in [backups](system.md): type it again after restoring on
   another computer. `Sfe:Connections:Dropbox:AppSecret` in the [configuration](../configuration.md) wins over it.
5. **Test** checks the key and secret with Dropbox (without a secret, the first sign-in checks the key).

![Administration › Connections › Settings](../images/admin-connections-settings.png)

The **Settings** say whether connections are allowed at all, of which kinds and at which levels; `Sfe:Locked` in the
configuration can fix them.

## Adding a connection

![My account › Connections](../images/admin-account-connections.png)

**Add** opens the form: the kind, the name people will see, and the **folder** of the storage shown as the
connection's root (empty for all of it).

![Adding a connection](../images/connections-add.png)

- **Sign in at the provider and come back here**: the browser goes to Dropbox, you allow SFE in, and Dropbox sends
  you back to the connection's page.
- **Sign in at the provider and copy the code it shows**: Dropbox opens in a new tab and shows a code; paste it in the
  form and **Connect**.

A sign-in lasts 10 minutes and works once, for the account that started it.

## Managing a connection

**Manage** opens what you may do with it:

- **Rename**, **Read only** (nothing is changed in the storage through SFE, whatever the groups allow), **Off** (kept,
  with who uses it, but nobody opens it).
- **Sign in again** when it shows **Sign in again** in red: Dropbox no longer accepts its sign-in (access removed at
  Dropbox, or the server's keys lost with a restored backup). It stays the same connection, with the same people;
  signing in to another Dropbox account than before is said, as people then see other files.
- **Who uses it** (space and server connections): **Groups of this space** — doesn't see it, reads, or reads and
  changes; for a server connection, the **spaces it is offered to** (every space, or some), then each space's
  administrators choose their groups.
- **Remove**: the connection goes with who used it; the files stay in the storage.
