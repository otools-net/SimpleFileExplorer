# Readability measurements

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
- **C# is measured by its layout** (Allman braces, 4 spaces, file-scoped namespaces), not parsed: a member is a `{`
  alone on its line at 4 or 8 spaces after a header with parentheses. Counts are close, not exact (a multi-line
  lambda assigned to a field is missed; "nested" counts braces, so an object initializer inside a loop adds a level).
  Run `dotnet format` first if the layout is off. Migrations (`Data/*Migrations/`) are left out.
- TypeScript is exact: nesting counts `if`/loops/`switch`/`try` inside one another, `else if` staying at its `if`'s
  level, inner functions measured on their own.
- C# formatting and style are not this tool's job: `dotnet format Sfe.slnx --verify-no-changes --severity warn`.
