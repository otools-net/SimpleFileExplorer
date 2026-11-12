# C# code style

Files: `.editorconfig` (formatting, naming and style rules, for every language of the repository).

- Primary constructors, file-scoped namespaces, collection expressions, `[GeneratedRegex]`.
- XML doc comments explaining *why*; match the surrounding comment density.
- Core DI registrations are singletons in `ServiceCollectionExtensions` (`AddSfeCore`, `AddSfeAccess`).
- Layout (line breaks, indentation) is CSharpier's: `dotnet csharpier format .` (`.csharpierrc.yaml`, 120 columns;
  migrations left out by `.csharpierignore`). Naming, braces and the other style rules are in the root
  `.editorconfig`, checked by `dotnet format style` and `dotnet format analyzers` with `--verify-no-changes --severity
  warn` (not plain `dotnet format`: its whitespace pass fights CSharpier's breaks, so IDE0055 is off). They show in
  the editor only: `dotnet build` gets no warning from them. Commands: [readability](readability.md#checking).
- Sizes (lines, functions, files), names, comments, and how existing code is refactored without changing behaviour:
  [readability.md](readability.md).
- Client style: [frontend.md](frontend.md).
