# C# code style

Files: `.editorconfig` (formatting, naming and style rules, for every language of the repository).

- Primary constructors, file-scoped namespaces, collection expressions, `[GeneratedRegex]`.
- XML doc comments explaining *why*; match the surrounding comment density.
- Core DI registrations are singletons in `ServiceCollectionExtensions` (`AddSfeCore`, `AddSfeAccess`).
- Formatting, naming and braces are in the root `.editorconfig`; check with
  `dotnet format Sfe.slnx --verify-no-changes --severity warn` (it does not touch the migrations, marked generated).
  Its rules show in the editor only: `dotnet build` gets no warning from it.
- Sizes (lines, functions, files), names, comments, and how existing code is refactored without changing behaviour:
  [readability.md](readability.md).
- Client style: [frontend.md](frontend.md).
