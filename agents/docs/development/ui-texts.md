# UI texts: glossary and writing style

The words and the style of every text a user reads: labels, buttons, hints (`title`), menus, dialogs, toasts, settings
(`settings-schema.ts`), empty states, and the server texts that reach users (right names in `RightCatalog`, problem
`detail`). Code: `src/Sfe.Web/src/app/` (templates, `toasts.*` calls, `settings-schema.ts`), `Sfe.Core/Access/RightCatalog`,
`shared/help.component.ts` (`sfe-help`). The user docs in `docs/` follow the same glossary.

Texts are rewritten to this note area by area as they are marked for translation ([translations](translations.md),
release 2.0 steps 2.3–2.8): a string is touched once, moved **and** rewritten in the same commit. New texts follow
it from now on.

## Language and spelling

- **UK English**: colour, centre, behaviour, cancelled, grey, organise, recognise, licence (the noun; "license" in
  file names such as `LICENSE` stays). Survey 2026-11-20: strings were mostly UK already; US leftover still seen:
  tag "Color" (`docs/administration/tags.md`, `docs/user-guide/tags.md`; the settings' "Colors" were fixed in 2.3).
  Identifiers in code stay as they are (`color`, `customized()`): only the text a user reads changes.
- **Punctuation:** the ellipsis character `…` (never `...`), curly quotes `“ ”` around words quoted in a text,
  `›` between the parts of a place in the app (Administration › Connections › Settings), `→` only in previews of a
  change (old → new).
- **Contractions** are fine and preferred in short texts (can't, don't, isn't): both forms are in use today (~170
  against ~140); keep "cannot" only where the stress matters ("This cannot be undone").
- **Sentence case** everywhere: buttons, titles, menu items, section names ("Open in new tab", "Folder rules"), except
  names of things (Dropbox, SFE, a space's name).
- **Numbers and sizes** go through `core/format.ts` (sizes in B / KB / MB / GB / TB, dates with the browser's
  locale) and counts through ICU plurals (`icu()`, [translations](translations.md); `plural()` is on its way out);
  never write "1 files" or "(s)".

## Tone

- Plain words, **full sentences**, second person ("You can…", "Your account…") or the imperative for actions. Say
  what happens, not what the thing is: "Long lines continue on the next line instead of going off the side." rather
  than "Line wrapping".
- **Terse noun-phrase hints are the main problem found** (no "its/it's" error was found): a hint that is only a noun
  phrase ("The room around the text.") becomes a sentence that says what changes ("Space left around the text, on
  every side.").
- One idea per sentence; no jargon a home user wouldn't know (no "tenant", "SSE", "job id", "snapshot", "SID") unless
  the screen is for administrators and there's no plain word.
- No blame and no exclamation marks. Don't say "please", "successfully", "simply", "just".
- Say who and where when it matters: "for everyone using this space", "on this browser only".

## Kinds of text

| Kind | Rule | Example |
|---|---|---|
| Button, menu item | A verb first, short; `…` when it opens a dialog that asks more before acting | "Compress…", "Delete", "Sign in again" |
| Hint (`title`) | A full sentence saying what the click does; with "(asks first)" when it confirms | "Delete the space Photos (asks first)" |
| Dialog title | What is being done, to what | "Rename “holiday.jpg”" |
| Confirmation | What will happen and whether it can be undone; buttons name the action, not "OK" / "Yes" | "Move to trash" / "Cancel" |
| Toast, success | What was done, past tense, the object named | "Space Photos deleted." |
| Toast, error | "Could not *verb* *object*: *reason*." (never "Failed to", "Unable to", "Error:") | "Could not change the password: …" |
| Empty state | What's missing and how to get some | "No quick links yet. Right-click a folder and choose Add to quick links." |
| Setting label | A noun phrase naming the value; the hint says its effect, its unit and range | "Text size" / "In pixels, from 10 to 32." |
| Problem `detail` (server) | Same as a toast error's reason: a sentence a user understands. Problem `type` words never change ([api-versioning](api-versioning.md)) | |

## Long explanations: `sfe-help`

A text longer than **about two lines** on a phone goes into `sfe-help` (`shared/help.component.ts`: a "?" opening it,
click / tap and keyboard accessible) instead of sitting under the field. The line shown keeps the essential
sentence; the help holds the why, the examples and the exceptions. Settings get it through the optional `help` field
of `SettingField` (step 2.3: rendered next to the label, found by the administration search; the hint
keeps to 200 characters, a spec checks it). When the in-app help exists (step 5.1), the help ends with a link to its
page.

## Glossary

Use the term on the left; the words on the right are not used in texts (code names may differ, they don't change).

| Term | Means | Not |
|---|---|---|
| **space** | One library with its own groups, accounts, tags, settings and connections; a server holds one or more. Used wherever access, settings or administration is meant ("this space's settings", connection level "space") | tenant |
| **library** | The files a space shows: its folder on the server and everything under it. "Open the library", "the library's folders" | root, share, drive |
| **folder** | A folder, in the library or in a connection | directory (except **sign-in directory**, below) |
| **item** | A file or a folder, when a text means either | entry (except archive entries, below), object, node |
| **connection** | An outside storage (Dropbox…) reached through SFE; its levels are shown as **Only me**, **This space**, **The server** (`connection.models.ts`; code names `account`, `library`, `server`) | remote, provider (provider = the kind: "Dropbox", for administrators only), cloud |
| **index** | SFE's database of a library's files (search, tags, duplicates) | catalogue, DB |
| **scan** | Reading the folders to bring the index up to date ("Scan this folder") | reindex, crawl, sync (for the library) |
| **sync** | Bringing a connection's index up to date; later, saved sync jobs (7.2) | scan (for connections) |
| **trash** | Where deleted and replaced items wait before being purged; "Move to trash", "Empty the trash" | recycle bin, bin, deleted items |
| **previous version** | What a file was before it was saved over; kept in the trash (step 4.2 lists them per file) | old version, backup, revision |
| **folder rules** | Per-folder access: Hidden / Read / Write for an account or a group. The card and page are **Folder access**, each line is a **rule** | permissions, ACL |
| **rights** | What a group may do in a space (from `RightCatalog`); a **right** is one of them | permissions, roles, privileges |
| **group** | A set of accounts sharing rights in one space; built-in **Administrators**, **Users**, **Guests** | role, team |
| **account** | A person's sign-in; **sign-in directory** = where its password is checked (local, Windows, LDAP) | user (in admin texts), login, profile |
| **visitor** | Someone using a space without an account (through the **Guests** group's rights) | guest (the person), anonymous |
| **administrator** | An account in a space's Administrators group (**space administrator** when the difference matters); **server administrator** has every right everywhere | admin (in texts; "Administration" for the page), superuser |
| **sign in / sign out** | The verbs; **sign-in** as a noun or adjective ("the sign-in page") | log in, login, log out |
| **quick links** | A user's links to folders, in the sidebar | bookmarks, favourites, shortcuts, pins |
| **tag** | A label put on files; **tag history** in the sidebar | label, keyword, category |
| **task** | Background work the server runs or schedules (scans, thumbnails, purge); **task history**, **schedule** | job (code name: `FileJobs`, `api/files/jobs`), process |
| **operation** | A move, copy, compression, extraction or transfer the user started; shown in the **operations** panel | job, transfer (for the user; "transfer" only in admin texts about connections) |
| **upload** | Files sent from the device to SFE; **download** the other way | import, export |
| **thumbnail** | The small picture on a tile; **preview** = seeing a file without opening it (hover, viewer) | icon, miniature |
| **trickplay** | Frames along a video for scrubbing and hover previews (administrators' word; "previews along the timeline" for users) | storyboard, sprites |
| **viewer** | Where a file opens full screen; **player** for video and audio; **editor** for pictures and text | lightbox, modal |
| **being written** | An item still being copied, uploaded or extracted (`.sfe-partial`, never shown) | partial, temporary, incomplete |
| **duplicates** / **similar videos** | Same files (by content) / videos that look alike | copies, clones |
| **mode** | **Local** (this computer) or **External** (others reach it) | |
| **entry** | A file or folder inside an archive | item (inside archives) |
| **settings** | Values a user or administrator changes; their **levels**: environment, server, space, user; a **locked** setting can't be changed below | options, preferences, config (the code's `reading-preferences` shows as "Reading settings") |
| **home folder** (5.4), **dashboard** (5.5), **notifications** (4.5) | Planned in release 2.0 | start folder, inbox, alerts |

## Gotchas

- Rights' names in `RightCatalog` reach users (the rights list, refusals) but their **keys** are API words: rewrite the
  name, never the key.
- Tests often find elements by their text (client specs and e2e `getByRole(..., { name })`): a text rewritten updates
  them in the same commit.
- Screenshots in `docs/images/` show texts: an area rewritten regenerates its screenshots
  ([docs-screenshots](../environment/docs-screenshots.md)).
