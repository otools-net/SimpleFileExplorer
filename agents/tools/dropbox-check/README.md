# Dropbox check with a real account

The suites never call Dropbox (`FakeProvider`, `RecordedDropbox`). Before a release with connections, someone with a
Dropbox account runs this list by hand on a scratch install ([running-the-app](../../docs/environment/running-the-app.md))
and notes the date and SFE version in the release's notes. Nothing here is automated: it needs a person to sign in at
Dropbox.

## Setup

1. At Dropbox's App Console: a new app, **Scoped access**, **App folder** (keeps the test away from real files),
   permissions `account_info.read`, `files.metadata.read`, `files.content.read`, `files.content.write` (submit them).
2. In SFE, Administration › Connections › Dropbox: copy the **Return address**; add it to the app's redirect URIs.
3. Administration › Connections › Settings: the app key. The Dropbox page says "offered".
4. **Test** without a secret: says it can only be checked by signing in. Save the app secret, **Test**: "accepts".
   A wrong secret: "refused". Remove the secret again (PKCE alone) for the next steps.

## Sign-in (phase 2)

- [ ] My account › Connections › Add, **come back here**: Dropbox asks to allow, the browser lands on My account
  with "Connected."; the row shows the Dropbox e-mail.
- [ ] Add, **copy the code**: Dropbox opens in a new tab and shows a code; pasted, **Connect** makes the connection.
- [ ] A code typed wrong: "The provider refused the sign-in"; the same state can't be used again ("start again").
- [ ] Refuse at Dropbox (Cancel): back on the page with "did not give access", nothing made.
- [ ] Wait 11 minutes on Dropbox's page, then allow: "This sign-in is over".
- [ ] **Sign in again** with the same Dropbox account: "Signed in again."; with another account: the message about
  another account; the connection keeps its id (address of the page) and its groups.
- [ ] At Dropbox, Settings › Connected apps: remove the app. (Phase 3 on: the next listing marks the connection
  "Sign in again".)
- [ ] The app secret set in `Sfe:Connections:Dropbox:AppSecret` and the page's removed: sign-in still works; the
  Dropbox page says "set in the server's configuration".
- [ ] Behind a reverse proxy or under a virtual directory: the return address shown is the public one, and the
  redirect flow lands back in SFE.
- [ ] The server's log: a security warning for the connection made and for an app secret set; no token, code or
  e-mail address in any line.

## Browse and download (phase 3)

- [ ] The sidebar lists the connection; it opens on Dropbox's root (or its root folder), folders first, thumbnails.
- [ ] A folder of more than 500 items (Dropbox pages of 500): scrolling loads every item once; sorting by size works.
- [ ] A video seeks (ranged reads through a temporary link); a photo opens; a PDF opens in the viewer.
- [ ] Download one file; several and a folder as one zip; a Paper document in a zip is listed in `SFE - left out.txt`.
- [ ] Connected apps at Dropbox: the app removed, the next listing says the connection must be signed in to again.

## Files

- [ ] New folder, rename (also a case-only rename: `a.txt` to `A.txt`), move and copy inside the connection by cut and
  paste: Dropbox's web page shows the same.
- [ ] Delete: the item is in Dropbox's deleted files; the dialog said the provider keeps them.
- [ ] Upload a file over 150 MB (several parts) and a folder; close the page mid-upload: nothing half-written stays
  at Dropbox. Upload a name already there: keep both, replace, skip.
- [ ] Properties of a file: size, date and Dropbox's content hash (compare with the hash Dropbox's API gives).

## Transfers with the library

- [ ] Copy a folder of photos and a video over 150 MB from the library to Dropbox and back; the task history shows one
  run per transfer, both places named.
- [ ] Move a file from the library to Dropbox: it lands in SFE's trash once Dropbox has it; move it back: it lands in
  Dropbox's deleted files.
- [ ] Cut the network mid-transfer: the item fails, **Retry** in the operations panel finishes it.

## Folder rules

- [ ] On a library-level connection, a rule hiding a folder from a group: a member of that group neither lists nor
  opens it (address typed by hand: not found).
- [ ] Rename the ruled folder in SFE: the rule follows. Rename it on Dropbox's web page: it stays hidden where the
  listing shows it; re-point the rule on the card.
