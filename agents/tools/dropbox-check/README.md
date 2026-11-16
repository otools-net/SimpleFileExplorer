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

## Index of a large Dropbox

Confirms the estimates of [connections-index-plan](../../docs/development/connections-index-plan.md) ("First sync of
1 M files") on a real account; the suites can't (no account, and Dropbox publishes no page times or rate limits).
Run it once the connection index (plan 6, phase 1) is built, and again after phase 5 for the preview steps. Takes an
afternoon; most of it is waiting.

### Before you start

1. A Dropbox account holding **many files**: 100 000 at least, ideally 1 million, in deep and wide folders (one
   folder of 50 000 files or more, and a tree 10 levels deep). Photos and a few videos for the preview steps.
2. A Dropbox app **only for this check**, at the App Console: **Scoped access**, **Full Dropbox** (an App folder
   would only see what was copied into it), permissions `account_info.read`, `files.metadata.read`,
   `files.content.read` and nothing that writes: the check can't change a real account's files. The steps marked
   *(writes)* below use a second, App folder app (the one of "Setup" above) instead.
3. A scratch install of the version to release ([running-the-app](../../docs/environment/running-the-app.md)), on
   SQLite (the default). Repeat steps 3 to 6 on SQL Server if the release notes will promise it.
4. A file to note results in: date, SFE version, database, Dropbox entries (files and folders), and each number
   asked for below.
5. Make the index's logs visible: in the scratch install's `appsettings.json`, under `Logging:LogLevel`, set
   `"Otools.Net.Sfe.Core.Connections": "Debug"`; restart. The index's lines come from
   `Otools.Net.Sfe.Core.Connections.Indexing.ConnectionIndexer`: one per page (entries, ms reading, ms writing), one
   when a sync resumes after a restart, one on a cursor reset (with the cursor's age).

### Steps

1. **Connect.** Set the app key of the Full Dropbox app (Administration › Connections › Settings), then My account ›
   Connections › Add, and allow at Dropbox. Note the time.
2. **First sync.** Open the connection once: the "Connection index" task starts (Administration › Tasks shows it
   running, with a line like "Listing “X”: N files, M folders"; the connection's **Manage** panel shows the index's
   status). Don't use the connection meanwhile. Note:
   - [ ] the total time, and the entries indexed (the task's run history or log gives both);
   - [ ] seconds per page: the sync lists **folder by folder, in pages of 500** (one call per folder, plus one per
     500 entries), not one recursive listing. Note the log's ms per page on first, middle and last pages, and the
     number of folders: with many small folders, calls ≈ folders; the plan's estimate (~525 calls of 2000) assumed
     a recursive listing, so expect more calls and note the total time against it;
   - [ ] any 429 (rate limited) in the log, with its `Retry-After`, and whether the sync went on by itself;
   - [ ] the server's memory every few minutes (Task Manager, or `Get-Process Sfe.Api` in PowerShell, column
     `WS`): it must stay flat, not grow with the entries;
   - [ ] the database file's size after the sync (`.sfe/sfe.db` in the library, or the SQL Server database):
     about 0.5 KB a row on SQLite, 1 KB on SQL Server;
   - [ ] any error in the log naming `list_folder` on a very large folder (50 000 entries or more).
3. **Restart halfway.** Sync again from scratch (remove the connection, add it back), and stop the server
   (Ctrl+C) when the task's counts are about half of step 2's. Start it again and open the connection (or press
   **Sync now** in its Manage panel, under My account › Connections or Administration › Connections):
   - [ ] the task goes on from where it stopped (its progress doesn't start at 0, the log says it resumed);
   - [ ] at the end, the entry count equals step 2's (no rows twice, none missing).
4. **Changes** *(writes: use the App folder app)*. Index the App folder app's connection with a few hundred files.
   In SFE, tag three files (**Tags…** on each): the one you will rename, one in the folder you will move, one you
   will delete; and tag a whole folder of about a hundred files (its tag button). Then on Dropbox's web page: add a
   file, rename the first tagged file, move the folder holding the second, delete the third. Press **Sync now** in
   the connection's Manage panel:
   - [ ] the status's counts follow (one file more, one less), and the log names the changes;
   - [ ] the sync took seconds (by cursor), not a full pass;
   - [ ] the renamed and the moved files show their tag in their new place (rows follow Dropbox's ids), and a search
     `t:<tag>` in the connection finds them there; the deleted one is gone from it;
   - [ ] the folder's tag dialog counts its hundred files, and removing the tag there takes a moment, not minutes.
5. **A long pause.** Before stopping the install of step 2, tag a few files of the large connection and note them.
   Leave it off for **two weeks or more**, start it and press **Sync now**:
   - [ ] the log says whether Dropbox reset the cursor, and how old it was;
   - [ ] after a reset, a full pass ran (the counts end as before), and the noted files still there keep their tags
     (`t:<tag>` in the connection finds them all).
6. **Search** (once phase 3 is built): a name, a type and a size on the large connection; note the time to the
   first page and to the count (the plan's table, measured locally on 3 M rows, says what to expect).
7. **Previews** (built in phase 5: [connection-previews](../../docs/features/connection-previews.md)). Before:
   ffmpeg configured on the scratch install (Installation page), Playback › Video streams on (Fallback), and
   `Otools.Net.Sfe.Core.Connections` at `Debug` in the logs. Then open a folder of photos and one of videos:
   - [ ] photos get Dropbox's thumbnails at once; with the log at Debug, a folder of 100 photos makes about 4
     `get_thumbnail_batch` calls (25 each), not 100; reopening the folder asks Dropbox nothing (thumbnails from
     `{cache}/connections/{id}/`);
   - [ ] note whether **videos** get a thumbnail from Dropbox (not documented): with none, a video's tile shows a
     thumbnail only after it was played converted once (SFE makes it from the cached file);
   - [ ] **`media_info`**: open one photo alone (a folder with one photo, so it goes through `get_thumbnail`, not the
     batch): its width and height show in the listing afterwards, and `w:` finds it; note whether Dropbox answered
     `media_info` or `pending` (a photo uploaded minutes ago may be pending);
   - [ ] play a video the browser can't play (HEVC, MKV): the first play waits for the download into the cache, then
     converts; afterwards its duration shows and `d:` finds it; a second play starts at once;
   - [ ] a **photo over 20 MB** gets a thumbnail made by SFE (Dropbox makes none): the server log shows one download
     of it, the cache folder holds its `.bin`;
   - [ ] Markdown, JSON, a `.txt` open read only in the viewer; a file over 10 MB says it is too large;
   - [ ] Administration › Settings › Connections: set the cache to 100 MB and play a few videos: the cache folder
     stays under it; remove the connection: its folder under `connections/` is gone.

### Afterwards

- Write the numbers in the release's notes, and in the plan's phase 0 table (or the feature note once the plan is
  deleted) in place of the estimates; tick the plan's "A person confirms the sync numbers" item.
- Anything far from the estimates (pages over 3 s, memory growing, a resume starting over, tags lost) is a bug to
  fix before the release.
- Remove both apps at Dropbox (Settings › Connected apps) and the scratch install.
