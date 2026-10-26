# Archive measurements

Measures the archive viewer on big archives in the real app: how long the list takes to read, the server's peak
memory, the catalog's size, and how long pages, Go to and Find take. The results go in
`agents/docs/features/archive-reading.md` ("Measured"). Node.js 22+, no npm packages; the app's build and data from a
docs-screenshots run (`<tmp>/sfe-docs`, or env `SFE_DOCS_WORK`).

```sh
# everything (~1.2 GB of archives written once into <tmp>/sfe-archives, kept for the next run)
node agents/tools/archives/measure.mjs

# a quick run: every archive 100 times smaller
node agents/tools/archives/measure.mjs --scale 0.01

# some of them
node agents/tools/archives/measure.mjs --only zip-5m,7z-1m
```

| File | |
|---|---|
| `measure.mjs` | Per archive: writes it if missing, starts the app (`:5097`, the docs build, an empty cache) on a library holding only the archives, signs in as the docs' admin, asks for the archive until its list is read (the build time), reads the process's peak working set (Windows) or `VmHWM` (Linux), the catalog's size, then times the root page (median of 10), a folder's first page of 1 000, every page of the one-folder archive, Go to 1 500 folders down the deep one (an address holds no more: Kestrel's 8 KB request line), Find in this folder, and Find in archive for its last name (all pages). Prints a JSON line per archive, then a Markdown table |
| `writers.mjs` | `writeZip` (stored, Zip64 past 65 534 entries; the directory in a second pass of the generator), `writeTar` (ustar), `write7z` (empty files only: a header and nothing else, as SharpCompress loads a 7z's whole list) |

- Scenarios: `zip-5m` (5 000 000 files, folders of 1 000), `zip-wide` (100 000 files in one folder), `zip-deep`
  (9 999 folders nested: `Files:Archives:MaxDepth` is 10 000 with the file), `tar-1m`, `7z-1m` (1 000 000 each).
- No RAR: nothing free writes one.
- The docs build is a Debug build in Development: the numbers are upper bounds of a release server's. The peak
  includes the app's own start (the "before" column).
