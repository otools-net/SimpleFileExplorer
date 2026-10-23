# File names (file management)

Code: `src/Sfe.Core/Files/FileNames.cs`; setting `Files:PortableNames` (`FileManagementOptions`). Tests: `FileNamesTests`.

- `FileNames.Check(name, portable)` gives the reason (a sentence for the person) or null. Everywhere: empty/blank,
  `.`, `..`, `/` (and `\` on Windows), control characters. Windows, or `portable`: `< > : " \ | ? *`, trailing dot or
  space, device names with or without extension (the base before the **first** dot, trailing spaces trimmed:
  `nul.tar.gz`, `con .txt`; also `COM¹²³`), 255 UTF-16 units. Linux, or `portable`: 255 UTF-8 bytes.
- `portable` comes from `Files:PortableNames` (per library, on by default); Windows servers always apply the Windows
  rules.
- Windows does **not** read `x \y` as `x\y`: a trailing space is dropped from the last segment only (a trailing dot
  from any segment). Checked by hand; don't test it as an alias.
- `KeepBoth(name, isFolder, exists)`: `beach (2).jpg`, `Photos (2)`, numbering before the last extension
  (`archive.tar (2).gz`); a folder's dot is not an extension; null after 1000 tries. Not used yet (phases 3–5).
- `FileOperations.Target` also refuses a name that `TryResolve` changes or refuses (`.sfe` at the root, anything the
  system reads as another name), as "reserved".
