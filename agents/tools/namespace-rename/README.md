# namespace-rename

`rename.mjs` renamed the C# namespaces `Sfe.Core` / `Sfe.Api` / `Sfe.Tests` to `Otools.Net.Sfe.*`
([namespaces](../../docs/development/namespaces.md)). It splits each `.cs` file into code,
comments and strings, and rewrites only:

- `Sfe.Core|Api|Tests` names in code (`namespace`, `using`, qualified names, `typeof`, generic arguments);
- `cref="Sfe.…"` in comments;
- in `*ModelSnapshot.cs` only, string literals starting with `"Sfe.Core.` (the EF entity names of the model).

String literals elsewhere (data protection purposes, log categories, file names) and comment text are left alone;
grep `"Sfe\.` afterwards for what must stay. `bin`, `obj` and `node_modules` are skipped. It is idempotent.

```sh
node agents/tools/namespace-rename/rename.mjs src tests --dry-run   # count the files that would change
node agents/tools/namespace-rename/rename.mjs src tests             # rewrite them
```
