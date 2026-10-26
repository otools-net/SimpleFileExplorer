# Third-party sample files

Files the tests need that SFE's tools can't make (no program on a dev machine writes them), taken as they are from
another project's tests, under that project's licence. `agents/tools/samples/update-samples.mjs` never touches this
folder. Read in place through `TestSamples.ThirdPartyOf("<project>/<file>")`.

## `sharpcompress/`

RAR archives (RAR can't be written without WinRAR) from [SharpCompress](https://github.com/adamhathcock/sharpcompress)
`tests/TestArchives/Archives/`, commit `8fa17607d8b041528ca4d67e8bee76c60b361faf` (taken 2026-10-26). MIT licence:
`sharpcompress/LICENSE.txt`.

| File | What |
|---|---|
| `Rar.rar` | RAR 4: `тест.txt` (15 498 bytes), `exe\test.exe`, `jpg\test.jpg` (a JPEG), folders |
| `Rar5.rar` | The same in RAR 5 |
| `Rar5.solid.rar` | RAR 5, solid |
| `Rar5.encrypted_filesOnly.rar` | RAR 5, data encrypted, names in clear; password `test` |
| `Rar5.encrypted_filesAndHeader.rar` | RAR 5, names encrypted too; password `test` |
| `Rar.encrypted_filesAndHeader.rar` | RAR 4, names encrypted; password `test` |

To take another: `curl -L -o tests/samples/third-party/sharpcompress/<file>
https://raw.githubusercontent.com/adamhathcock/sharpcompress/<commit>/tests/TestArchives/Archives/<file>`, and add
its row here (with the commit if it differs).
