# Properties of a file or folder (file management, phase 7)

Code: `src/Sfe.Core/Files/Properties.cs` (`Properties`, `PropertyJobs`, `IPropertyAccess`), `Api/Endpoints/PropertyEndpoints.cs`;
client `app/files/properties.dialog.ts` (`PropertiesDialog`, slot `Dialogs.properties`), the `properties` entry and
`canSeeProperties` of `item-menu.ts`, Alt+Enter in `library-page.component.ts`. Tests: `PropertiesTests`,
`PropertyRouteTests`, `properties.spec.ts`. User docs: `docs/user-guide/managing-files.md#properties`.

## Decisions (2026-10-17)

- The item menu opens for everyone who browses (it always has Properties); Shift+right click keeps the browser's.
- A folder's totals come from the index at once; **Count on disk** walks it on demand.
- Checksums: SHA-256, SHA-1 or MD5, for whoever may download the file (`files.view` + `files.download`).

## How it works

- `GET api/files/properties?path=` (`library.browse` at the item): `Properties.Describe` (disk: size, created, modified,
  accessed, hidden = dot name or attribute, read-only, link without its target) + the item as listings show it with
  `AttachIndexDataAsync`; `canChecksum`. Dates before 1971 are "unknown" (1601/1970 placeholders).
- `GET …/totals?path=` (rate limit `media`): `IndexedTotalsAsync` counts files, folders and bytes in the tree with
  `InTree` + `WhereVisible(state.Visible())`, so files below folders hidden from the caller don't count (they would
  tell what is there). Proportional to the folder's rows: only asked when a folder's sheet opens.
- `POST …/count`, `POST …/checksum` → `PropertyJobs` (in memory, per library): one job per caller (`RequestLimits.Caller`)
  and kind, a new one stops the previous; `GET`/`DELETE …/jobs/{32 hex}` only for the caller; forgotten 10 minutes
  after the end. The count walks with a stack (no recursion), skips links (`leftOut`), protected real paths (work
  folder, server files), folders the caller doesn't see (access read when it started), and stops at 2 000 000 entries
  (`capped`). The checksum reads with `IncrementalHash`, progress in `bytes`/`total`.
- The client polls every 400 ms while a job runs; closing the sheet (or opening another item) cancels its jobs.

## Gotchas

- `ItemMenu.hasActions` is now true for every browsing session: tests that expected the browser's menu without file
  rights were changed; the ⋮ button shows on every item.
