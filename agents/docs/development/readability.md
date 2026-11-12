# Readable code: rules and refactoring

Files: `.editorconfig` (formatting and C# style, checked by `dotnet format`), `agents/tools/readability/measure.mjs`
(the size limits below, measured). Language conventions that are not about readability stay in
[code-style](code-style.md) and [frontend](frontend.md).

The code was written with AI tools and works, but a person reading it meets long lines, long functions, files holding
many things and dense inline templates. These rules say what readable means here, so that the code can be brought to
it step by step **without changing what it does**. They are taken from guidelines the community already follows
([sources](#sources)), kept where this code already agrees with them, and sized from what was measured.

- **New and changed code** follows every rule now.
- **Existing code** is brought to them by the procedure in [Refactoring](#refactoring-existing-code); until then a
  file over a limit is a known debt, not a model to copy.
- A rule that makes one piece of code harder to read loses there: say why in a comment, and keep the exception small.

## The one test

Someone who knows C# or TypeScript, but not this file, reads a function once from top to bottom and can say what it
does and where they would change it. Everything below serves that.

## Sizes

Limits, not targets: most code should be well under them. "Now" is what `measure.mjs` counted on 2026-11-12, after the formatting
(server = `src/**/*.cs` without migrations, client = `src/Sfe.Web/src/**/*.ts` without specs).

| What | Limit | When over | Now over (server / client) |
|---|---|---|---|
| Line | 120 characters, comments too | break it ([Lines](#lines)) | 2749 / 2314 lines |
| Function or method | 60 lines | extract named steps | 50 / 7 |
| Nesting inside a function | 3 levels | guard clauses, extract the inner block | 16 / 7 |
| Parameters | 5 | one object for those that travel together | 119 / 2 |
| Dependencies of a class (constructor, `inject()`) | 8 | the class has several jobs: split it | 6 / 6 |
| File | 400 lines (tests: 800) | split by responsibility | 21 / 17 (tests: 4 / 2) |
| Top-level types in a C# file | 1 (see [C#](#c)) | one file per type | 106 files |
| Inline Angular template | 40 lines | move it to `name.component.html` | 56 |
| Paths through a function (cyclomatic complexity) | 25, the analyzer's default (CA1502) | split by case | editor hint |

Where the numbers come from: 400-line files and small functions are the Angular style guide's long-standing advice;
3 levels of nesting, 15 for cognitive complexity and 7 parameters are SonarSource's defaults; ESLint's own defaults are
stricter (50 lines per function, 3 parameters). 120 columns is what the comments of this code already wrap at, and
what fits a side-by-side diff. Test files get more room because a test reads as a list of independent cases.

## Rules for all code

### Functions

- **One job per function.** If describing it needs "and then", each part is a function with a name that says what it
  does. The caller then reads like a summary: `Validate`, `ReserveName`, `CopyBytes`, `Index`.
- **Leave early.** Check what stops the work first and return; the main path stays at the left margin. An `else` after
  a `return` is not needed.
- **Name a condition that needs thought.** More than two `&&`/`||`, or a mix of both, becomes a variable or a function
  whose name is the question asked:

  ```csharp
  // before
  if (LibraryPaths.NormalizeRelative(request.Target) is not { } targetPath || !paths.TryResolve(targetPath, out var targetFull)
      || !Directory.Exists(targetFull) || job.Access.IsProtected(targetFull))

  // after
  if (!TryResolveTarget(job, out var targetPath, out var targetFull))
  ```

- **One level of detail per function.** A function that decides *what* happens does not also open files and build
  SQL: it calls functions that do.
- **Same shape, same code.** Two blocks that differ by one name (the folder rules and the default tags kept with a
  deleted folder in `Trash`) are one function called twice.
- **`try` blocks hold one call.** The body of a `try` (and of a `catch` that does more than log) is a function, so the
  reader sees what can fail and what is done about it.
- **No flag parameters deciding between two behaviours** (`Delete(path, true)`): two functions, or an enum that reads
  at the call. Where a `bool` stays, the caller names it (`permanent: true`).
- **More than 5 parameters travel as an object** (a record, an options type) whose properties are documented once.

### Lines

- One statement per line; one declaration per line.
- A line over 120 characters is broken at its structure, not where it happens to reach the margin:
  - arguments: all on one line, or **one per line**, never a ragged mix;
  - a chain of calls (`Where`, `Select`, signals, promises): one call per line, the dot first;
  - `&&`, `||`, `?`, `:`, `??`: the line breaks **before** the operator;
  - a long string: a raw string (`"""`) or a template literal over several lines, or a named constant.
- A conditional expression (`a ? b : c`) is for choosing a value and fits one line or three; never one inside another:
  use `if`, a `switch` expression, or a function.
- Blank lines separate the steps of a function as paragraphs do; never two in a row, none after `{` or before `}`.

### Names

- Full words in the language of the feature: `targetFolder`, not `tgt` or `tf`. Abbreviations only when they are the
  usual word (`id`, `url`, `db`, `html`).
- A function is a verb phrase (`MoveToTrash`), a value a noun (`deletedItems`), a boolean a statement that is true or
  false (`isFolder`, `hasMore`, `canReplace`); never negative (`notFound` is fine as a status, `isNotEmpty` is not).
- One word per idea across the code: if it is `relative` and `full` for the two forms of a path in one class, it is in
  the next ([paths](paths.md)).
- The shorter the life, the shorter the name may be: `t => t.Id` is fine in a one-line lambda; a variable used over
  20 lines has a full name. Because C# code here uses `var`, the name must tell what the value is
  (`var entry`, not `var result`, for a `TrashEntry`).
- No numbers without a name: `64L << 20` is `FreeSpaceStep`, with its unit in the name or the comment.

### Comments

- Code says *what*; a comment says **why**: the reason, the trap, the browser or file system that forced it. A
  comment that repeats the line below is deleted; a comment that explains what a block does is a function name
  waiting to be extracted.
- Every public type and member, and every exported function, has a doc comment (`///`, `/** */`). Its first sentence
  is complete and plain, for someone who has not read the feature's note.
- A story longer than five lines (how a feature works across files, the history of a bug) lives in
  `agents/docs/features/`; the code links to it.
- A section banner (`/* ---------- The trash page ---------- */`) says the file holds several things: each section is
  a candidate for its own file.
- No code commented out, no `TODO` without what is missing: git keeps the old code.

## C#

Style the tools check is in `.editorconfig` (Allman braces, file-scoped namespaces, `_camelCase` private fields,
PascalCase constants, sorted usings, no unused members). What they cannot check:

- **One top-level type per file, named after it.** The exception is a family of small data types of one contract
  (requests and answers of one group of routes, the options of one section): together in `<Area>Contracts.cs` or
  `<Area>Models.cs`, under the file limit. A class never shares its file with the records it takes or returns
  (`FileEndpoints.cs` holds 13 types, `FileJobs.cs` 9).
- **Records over 5 members are written with properties**, one per line with its `<summary>`, `required` or `init`
  as needed; the positional form is for two or three values that are obvious at the call
  (`ClashSide(Name, IsFolder, …)` is at the limit, `FileJobView` with 14 is over it). Property names are what JSON
  shows: keep them.
- **A class over the file or dependency limit is split by what it knows**, into classes that can be read and tested
  alone (in `FileJobs`: the job list, the move, the copy, the extraction and the compression are five). `partial`
  files (`LibraryBrowser.Covers.cs`) are for one class that truly has one state; they make a file shorter, not a
  class simpler.
- **No optional services** (`FolderRuleService? rules = null` in a constructor): the class needs it or another class
  does that part. Tests build the object through their helpers ([testing](testing.md)).
- **Routes:** a `Map…Endpoints` method only lists routes, one sub-group per method once it passes 60 lines
  (`MapJobs(files)`); long descriptions are constants next to their handler. A handler that takes more than 5 values
  from the request and the container declares them in an `[AsParameters]` record ([api-routes](api-routes.md)).
- **Exceptions:** catch the types that can be handled (`IOException or UnauthorizedAccessException`);
  `catch (Exception)` only at the edge of a background job or of a best-effort step, always logged, and in a function
  whose name says so (`TryKeepRulesAsync`).
- **Async:** methods end in `Async`, take `CancellationToken cancellationToken` last, and pass it on; where it is
  deliberately not passed (work that must finish once started), a comment says so.
- **Expression bodies (`=>`) only for one line.** A member whose expression wraps gets a block body.
- **Braces** may be left out only when the condition and the statement are one line each (`csharp_prefer_braces =
  when_multiline`).
- **Pattern matching inside conditions** (`is not { } x || !TryY(x, out var y)`) declares variables where nobody
  looks for them: at most one declaration per condition, else a `Try…` method.

## TypeScript and Angular

On top of [frontend](frontend.md); from the Angular style guide and Google's TypeScript guide.

- **One concept per file, named after it** (`listing-position.ts`); no `utils.ts`/`helpers.ts`. `core/models.ts`
  (1463 lines) is split by area, each part next to the feature that owns it, re-exported from `models.ts` while
  callers move.
- **Templates:** inline up to 40 lines; larger ones go in `name.component.html` (`templateUrl`), where the editor
  formats and checks them. In any template:
  - an element whose tag passes the line limit has **one attribute per line**, in the order: structure
    (`#ref`, `class`, static attributes), inputs `[…]`, outputs `(…)`;
  - no logic beyond a property, a call without arguments worth naming, or one simple comparison: string building,
    `?:` with long branches and chained `?.`/`??` become a `computed()` in the class
    (`[style.transform]="zoomed() ? 'translate(' + … "` → `pictureTransform()`);
  - an event handler is one call (`(unsupported)="onUnsupported($event)"`), never two statements.
- **Order of a class:** injected services, inputs/outputs/queries, state signals, `computed` values, constructor and
  lifecycle hooks, methods the template calls, private methods. Related members stay together inside each group.
- **`protected`** for what only the template uses, **`private`** for the rest, **`readonly`** for everything Angular
  or the constructor sets once (signals, inputs, injected services).
- **Components show and forward; logic lives in plain functions and services.** What can be computed from values
  (parsing, layout arithmetic, building menu entries) is an exported function in its own file with its own spec, as
  `clampAxis` already is; the component calls it. A component over the file limit usually holds two or three of
  these.
- **Handlers are named for what they do** (`openTags()`, not `onTagsButtonClick()`); a handler that only branches on
  the key or the target delegates to such methods (`onKeydown` in `library-page.component.ts` is 74 lines of them).
- **Types:** no `any` (use `unknown` and narrow); exported functions state their return type; `!` only where a
  comment or the line above shows why the value is there: narrow once (`const urls = this.urls(); if (!urls)
  return;`) rather than `this.urls()!` on every line.
- **Imports:** a list that passes the line limit is one name per line. Named exports only.
- **Data tables** (`settings-schema.ts`, the icon sprite): one entry per line or per block; a long text is a constant
  or wrapped, never a 400-character line.
- **Braces:** as in C#.

## CSS

- One partial per area in `styles/` ([frontend](frontend.md)), under 400 lines (`reader.css` is 869: split by part of
  the reader).
- One selector per line in a list, one declaration per line, a blank line between rules.
- A value used in several rules is a custom property with a name saying what it is for.

## Tests

- A test's name is the behaviour it proves, readable as a sentence; its body is arrange, act, assert, separated by
  blank lines.
- Tests may repeat values to stay readable alone, but not machinery: setup shared by three tests is a helper
  ([testing](testing.md)).
- One test file per file or feature under test, with its name (`access.spec.ts` covering a whole folder is split
  along the components it tests). Limit 800 lines.

## Refactoring existing code

SFE is in production ([production-upgrades](production-upgrades.md)): a refactoring changes how the code reads and
nothing else.

- **What never changes in a refactoring:** routes and their names, JSON property names and enum values on the wire,
  setting keys, right names, database schema and migrations, file and cache layouts, log message templates that
  users search for, texts shown to users. C# namespaces of entities and options stay too (EF snapshots and
  configuration binding name them); moving a type to another file in the same namespace is always safe.
- **One kind of change per commit**, and never with a feature or a fix: "types of `FileJobs.cs` into their files",
  then "`FileJobs.RunAsync` in steps". A reviewer must be able to see that nothing else moved.
- **Tests stay green and unchanged.** `dotnet test` and `npm test` pass before and after; if a test has to change,
  the commit changed behaviour (the exception: a spec that reached into a private member that moved). Code without a
  test gets one that pins what it does **before** it is restructured.
- **Small steps the tools can do come first** (rename, extract method, move type to file, in the IDE), then the ones
  that need thought.
- **Touching a file for another reason?** Leave what you touched within the rules, in a commit of its own before
  the change.

Order, from mechanical to structural; each step is useful alone (the worklists, checks and traps of each step:
[readability-refactoring-plan](readability-refactoring-plan.md)):

1. **Format:** `dotnet format Sfe.slnx --severity warn` (done 2026-11-12, one commit for `src/` and one for
   `tests/`, both in `.git-blame-ignore-revs` so `git blame` skips them); `--verify-no-changes` passes since.
2. **Long lines**, file by file, worst first (`measure.mjs` lists them).
3. **One type per file** on the server; `models.ts` by area on the client.
4. **Templates** over 40 lines into `.html` files, then their long tags and expressions.
5. **Long and deep functions**, worst first, each after checking its tests.
6. **Classes with too many dependencies or lines**: the only step that changes the design; one class per change,
   with a short plan in the PR of what moves where.

## Checking

```sh
# C#: formatting and style of .editorconfig, changing nothing (exit 2 when something is off)
dotnet format Sfe.slnx --verify-no-changes --severity warn
# ... and fixing what can be fixed automatically
dotnet format Sfe.slnx --severity warn

# sizes: lines, files, functions, nesting, parameters, dependencies, templates; worst first
node agents/tools/readability/measure.mjs
node agents/tools/readability/measure.mjs --area client --top 40
```

- The editor shows the `.editorconfig` rules as you type (Visual Studio, Rider, VS Code with the C# extension);
  `dotnet build` is not affected (code style does not run in builds, the code quality rules added are hints).
- Nothing checks TypeScript style beyond the compiler's `strict` options. Proposed, once step 1 to 4 are done:
  `angular-eslint` with `typescript-eslint` (rules `max-lines-per-function`, `max-depth`, `max-params`, `complexity`,
  and `@angular-eslint/template/*` for templates), then `EnforceCodeStyleInBuild` on the server and both checks in
  CI, so the limits hold by themselves.

## Sources

- [.NET C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
  and the [.NET runtime coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md):
  layout, naming, `var`, braces, comments, catching specific exceptions.
- [Framework design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/): names of types
  and members.
- [Code analysis in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview): what
  `.editorconfig` severities do, code style in builds, code metrics rules (CA1501–CA1506).
- [Angular style guide](https://angular.dev/style-guide): one concept per file, file names, `protected`/`readonly`,
  members grouped, logic out of templates, components focused on presentation, handler names.
- [Google TypeScript style guide](https://google.github.io/styleguide/tsguide.html): descriptive names, named exports,
  no `any`, assertions explained.
- [SonarSource rules](https://rules.sonarsource.com/csharp/RSPEC-3776/) (cognitive complexity, nesting, parameter
  count) and [ESLint's](https://eslint.org/docs/latest/rules/) `max-*` rules: the thresholds.
- [Refactoring catalog](https://refactoring.com/catalog/) (Martin Fowler): extract function, replace nested
  conditional with guard clauses, introduce parameter object, the small-steps-under-tests procedure.
- [EditorConfig](https://editorconfig.org).
