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

# one area (server, "server tests", client, "client tests", "e2e tests", styles, templates), more rows
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
| files declaring several top-level types (an `…Contracts.cs` or `…Models.cs` within the file limit is allowed) | C# |

- The limits are the `LIMITS` constant at the top of `measure.mjs`: change them there and in `readability.md` together.
- **C# is measured by its layout** (CSharpier's: Allman braces, 4 spaces, file-scoped namespaces), not parsed: a
  member is a `{` alone on its line at 4 or 8 spaces after a header with parentheses (past the `)` CSharpier puts on
  its own line). Counts are close, not exact: a multi-line lambda assigned to a field and an expression-bodied
  member (`=> x switch { … }`) are not functions; "nested" counts the blocks open at once (statements, lambdas, local
  functions), not the braces of initializers. Run `dotnet csharpier format .` first if the layout is off.
  Migrations (`Data/*Migrations/`) are left out.
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
| `client-build.txt` | the client build's files: content hash and size (chunk hashes left out of names and content), and the total | `ng build --output-path` into the folder (not `wwwroot`), the build then deleted |

- The folder must be outside the repository (the script refuses otherwise). About 2 minutes for all three.
- Two runs on the same code give identical files (checked 2026-11-12), so any difference is the change's.
- `client-build.txt` lists the hashed chunks as `chunk.js` with a hash of their content (the names of other chunks
  left out of it): a change that compiles to the same code gives the same file; any difference is a real one. A
  template moved to its `.html` file, or broken by `break-template-lines.mjs`, gives the same build (checked
  2026-11-13). Equal sizes alone are not enough: a text break once kept a `
` in the compiled template.

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
  members, strings inside a template's `${…}`, and Angular's inline `template:`/`styles:`.
- Check: the client build (`references.mjs --only client`) keeps every chunk's size, since esbuild folds `'a' + 'b'`
  back (checked 2026-11-12: identical after 242 splits).

## One type per file (C#)

```sh
dotnet run agents/tools/readability/split-types/SplitTypes.cs -- --dry-run src/Sfe.Core/Files   # what would move
dotnet run agents/tools/readability/split-types/SplitTypes.cs -- src/Sfe.Core/Files             # moves them
dotnet format style Sfe.slnx --severity warn --include src/Sfe.Core/Files/                        # unused usings
dotnet csharpier format src/Sfe.Core/Files
```

- A .NET 10 file-based app (`#:package Microsoft.CodeAnalysis.CSharp`): each top-level type is cut with Roslyn, its
  doc comment and attributes with it, into a file of the same folder and namespace; the file's usings are copied and
  `dotnet format style` drops those a new file does not need.
- Where a type goes: the type named like the file stays; the other types of `XEndpoints.cs` go to `XContracts.cs`;
  those of a class listed in `modelsOf` (`EditableSettings`) to `XModels.cs`; a `…Models.cs` or `…Contracts.cs`
  within 400 lines is left; anything else to `<Type>.cs`. A file none of whose types is named after it is deleted
  once they have left. An existing destination is refused (the file is left).
- Files that are not one file-scoped namespace (`Program.cs`) are left.
- Check: build, and `references.mjs --only openapi,ef` identical (schema names come from types, not files).

## The client's models by area

```sh
node agents/tools/readability/split-models.mjs            # prints where each declaration of core/models.ts goes
node agents/tools/readability/split-models.mjs --write    # writes the files; core/models.ts re-exports them
node agents/tools/readability/split-models.mjs --imports  # importers take each name from its file
```

- A declaration used (imported from `core/models`) by one area only goes to that area's models file
  (`tags/tag.models.ts`…); one used by several areas, or by `core/` itself, stays in `core/`, in a file by subject
  (`core/tag.models.ts`, `core/task.models.ts`…, the `CORE_FILES` table); what a core type refers to stays in core.
  `core/api.service.ts` does not count as a use (it carries every type).
- Each file imports what it refers to with `import type` (`import` for the guards that are functions);
  `core/models.ts` ends with `export *` of every new file, so no importer changes in that commit.
- `--imports` rewrites every `import { … } from '…/core/models'` into one import per file the names live in now
  (aliases and `type` kept); then the `export *` lines of `core/models.ts` can go.
- Check: both `tsc --noEmit` (app, spec), `npm test`, and the client build (`references.mjs --only client`): only
  `main.js` moved by a few bytes (two guard functions changed modules).

## Inline templates into .html files

```sh
node agents/tools/readability/move-templates.mjs --dry-run src/Sfe.Web/src/app   # what would move (over 40 lines)
node agents/tools/readability/move-templates.mjs src/Sfe.Web/src/app/access       # moves them
```

- `template: \`…\`` of a `@Component` becomes `templateUrl: './<file>.html'` (`<class>.component.html` when the file
  holds several components); the `.html` holds the string's value (escapes resolved), its common indentation
  removed unless it has `<pre>`, `<textarea>` or `ngPreserveWhitespaces`. `${…}` templates are reported, not moved.
- Check: `npm test` and the client build (`references.mjs --only client`) identical file for file (checked
  2026-11-13 on the first 13).
