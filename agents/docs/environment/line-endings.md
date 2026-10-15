# Line endings and shell editing

- The index is all LF; working copies are mostly LF, a few may be CRLF. Keep each file's existing endings.
- List with `git ls-files --eol`, check one with `file` (Git Bash `grep` doesn't see `\r`).
- `dotnet ef migrations add` writes CRLF (with BOM): normalise the new files and the snapshot to LF
  (`sed -i 's/\r$//'`).
- `sed`/`perl` multi-line edits silently miss on CRLF files, and inserted lines come out LF: prefer the Edit tool,
  check `git diff --stat`, normalise with `sed -i 's/\r*$/\r/'`.
- New `.sh` files need LF (`.gitattributes`); `.cmd` files are CRLF (`.gitattributes`).
- Git Bash's `perl` reading a CRLF file through stdin drops the CRs, `awk` too; a heredoc holding `'` inside
  `$(…)`/quotes breaks the whole command: write long files with the Write tool.
- Git Bash also rewrites arguments that look like paths (`printf '>nul'` came out `>/dev/null`) and heredocs swallow
  `\\`: write batch files and scripts with backslashes with the Write tool too.
- `warning: in the working copy of '…', LF will be replaced by CRLF` on every git command touching a changed LF file is
  harmless noise from the system gitconfig's `core.autocrlf=true` (commits stay LF): don't convert files to "fix" it;
  run git as `git -c core.safecrlf=false …` (e.g. `diff`, `status`, `add`) to keep the output clean.
