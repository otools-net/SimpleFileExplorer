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

## Browse, files, transfers (phases 3 to 5)

Added with those phases: listing with more than one page, a ranged read of a video, upload of a file over 150 MB
(parts), rename and move at Dropbox, delete into Dropbox's own deleted files, copy to and from the library.
