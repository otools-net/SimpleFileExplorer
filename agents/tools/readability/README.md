# Readability measurements

Files: `measure.mjs` (the counts), `baseline.mjs` and `baseline.json` (the ratchet), `rewrap-comments.mjs` (long
comments), `references.mjs` (outputs a refactoring must leave unchanged). The plan they serve:
`agents/docs/development/readability-refactoring-plan.md`.

Counts what is over the size limits of `agents/docs/development/readability.md` ("Sizes") and lists it worst first:
the worklist of the refactoring, and how far it is. Reads the files git tracks, changes nothing. Node.js 22+ and the
client's packages (`npm ci` in `src/Sfe.Web`: TypeScript files are read with the compiler's parser).

```sh
# the summary table, then the 15 worst of each kind per area
node agents/tools/readability/measure.mjs

# one area (server, "server tests", client, "client tests", "e2e tests", styles), more rows
node agents/tools/readability/measure.mjs --area client --top 40

# everything as JSON (every file, function, class and template measured)
node agents/tools/readability/measure.mjs --json > <tmp>/readability.json
```

| Measure | How |
|---|---|
| lines over 120 characters, files over 400 lines (tests: 800) | every area |
| functions over 60 lines, with more than 5 parameters, nested deeper than 3 | not in tests (a `describe` is an outline) |
| classes with more than 8 dependencies | C#: parameters of the primary constructor; TypeScript: `inject()` fields |
| inline templates over 40 lines | `template:` of a component |
| files declaring several top-level types | C# |

- The limits are the `LIMITS` constant at the top of `measure.mjs`: change them there and in `readability.md` together.
- **C# is measured by its layout** (CSharpier's: Allman braces, 4 spaces, file-scoped namespaces), not parsed: a
  member is a `{` alone on its line at 4 or 8 spaces after a header with parentheses (past the `)` CSharpier puts on
  its own line). Counts are close, not exact: a multi-line lambda assigned to a field and an expression-bodied
  member (`=> x switch { … }`) are not functions; "nested" counts the blocks open at once (statements, lambdas, local
  functions), not the braces of initializers. Run `dotnet csharpier format .` first if the layout is off. Migrations (`Data/*Migrations/`) are left out.
- TypeScript is exact: nesting counts `if`/loops/`switch`/`try` inside one another, `else if` staying at its `if`'s
  level, inner functions measured on their own.
- C# formatting and style are not this tool's job: CSharpier and `dotnet format style`
  ([readability › Checking](../../docs/development/readability.md#checking)).

## Comments over the limit

```sh
node agents/tools/readability/rewrap-comments.mjs src tests        # rewrites them
node agents/tools/readability/rewrap-comments.mjs --check src      # exit 1 when some are left
```

- C# `///` and `//`, TypeScript `//` and the ` * ` lines of block comments, CSS block comments; a comment at the end of
  a code line is not touched.
- Only the paragraph holding a long line is reflowed, with its own prefix; a paragraph ends at a blank comment line,
  a tag line (`<param …>`, `</summary>`), a bullet, a JSDoc `@tag`. A one-line `<summary>…</summary>` too long takes
  the block form (tags on lines of their own). Words, `<tags …>` and `` `code` `` are never split, so a single
  `<see cref="…"/>` longer than the line stays long; `<code>` and fenced blocks are left alone.
- A second run changes nothing (checked on `src/`, 2026-11-12).

## The baseline (no file gets worse)

`baseline.json` holds what each file is over the limits by (its lines when over the file limit, its long lines, its
types when several, and how many of its functions, classes and templates are over a limit), one file per line. Code
in `baseline.mjs`.

```sh
# exit 1 when a file is worse than in baseline.json (a new file over a limit counts); says when some are better
node agents/tools/readability/measure.mjs --check
# rewrite it from the code, printing the totals before -> after
node agents/tools/readability/measure.mjs --write-baseline
```

- Run `--check` before each commit. A file worse than its baseline is brought back within it.
- A refactoring that **moves** debt (a type with long lines into its own file, a function into another class) fails
  the check for the new file: run `--write-baseline` and commit `baseline.json` with it; its diff shows where the debt
  went, and the totals printed must not grow.
- A commit that pays debt off runs `--write-baseline` too, so that the files it improved cannot slide back.
- Both measure every area (no `--area`); another path can follow the flag (`--check <file>`).

## References (what a refactoring must not change)

```sh
node agents/tools/readability/references.mjs <temp>/before            # on the commit before
node agents/tools/readability/references.mjs <temp>/after              # after the change
git diff --no-index <temp>/before <temp>/after                         # nothing, or only what was meant
node agents/tools/readability/references.mjs <temp>/x --only openapi,ef  # some of them (openapi, ef, client)
```

| File | What | How |
|---|---|---|
| `openapi.json` | the OpenAPI document (routes, parameters, schemas, descriptions) | `OpenApiExportTests` with `SFE_OPENAPI_OUT`, through `dotnet test` (client build skipped) |
| `ef-model.txt` | whether each EF context differs from its migrations' snapshot | `dotnet ef migrations has-pending-model-changes`: must say "No changes" |
| `client-build.txt` | the client build's files and sizes, hashes left out of the names, and the total | `ng build --output-path` into the folder (not `wwwroot`), the build then deleted |

- The folder must be outside the repository (the script refuses otherwise). About 2 minutes for all three.
- Two runs on the same code give identical files (checked 2026-11-12), so any difference is the change's.
- `client-build.txt` lists the hashed chunks as `chunk.js` sorted by size: a template moved to its `.html` file
  should give the same sizes; a difference of a few bytes in one chunk is worth a look, not a failure.

## Long strings (C#)

```sh
node agents/tools/readability/split-strings.mjs src/Sfe.Api src/Sfe.Core   # "a b" -> "a " + "b", then:
dotnet csharpier format src                                                 # lays out the + chains
node agents/tools/readability/rewrap-descriptions.mjs src/Sfe.Api          # OpenAPI descriptions in raw strings
```

- `split-strings.mjs` splits regular and interpolated literals at a space (after a punctuation mark when one is near
  the end), never inside a `{hole}` or an escape; both halves keep the prefix (`$`). It leaves verbatim (`@"`), raw
  and `u8` literals, a literal with a member access or index after it or a cast before it, patterns (`is`, `case`,
  switch arms), and interpolated strings near `SqlQuery`/`FromSql`/`ExecuteSql`: `$"a" + $"b"` is a `string`, not a
  `FormattableString` (EF's `SqlQuery` then does not compile; an API with both overloads would take the values into
  the SQL text). Build after it.
- `rewrap-descriptions.mjs` reflows the Markdown of raw strings given to `.WithDescription(` or assigned to a
  `…Description` name, only the paragraphs holding a long line; no line starts with `-`, `1.`, `#`, `>` or `|`. The
  OpenAPI document then differs only in where its descriptions break lines: compare it with whitespace collapsed.
- Raw strings that are SQL, test data or console text are not touched: by hand, or left with a comment saying why.

## Long strings (TypeScript)

```sh
node agents/tools/readability/split-strings-ts.mjs src/Sfe.Web/src tests/e2e   # splits, then Prettier, in turns
```

- Found with the compiler's syntax tree: `'…'`, `"…"` and the literal parts of templates (never inside `${…}`), split
  at a space between column 50 and the limit (after a punctuation mark when one is near), as `'a ' + 'b'`; Prettier
  lays out each file after a split, until no long line can take one.
- Left: imports, object keys, literal types, `as`/`satisfies`, tagged templates, a member access on the literal, enum
  members, and Angular's inline `template:`/`styles:`.
- Check: the client build (`references.mjs --only client`) keeps every chunk's size, since esbuild folds `'a' + 'b'`
  back (checked 2026-11-12: identical after 252 splits).
