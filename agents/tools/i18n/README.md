# Texts for translation

File: `extract.mjs`. The rules it serves: `agents/docs/development/translations.md`.

Extracts the client's texts marked for translation (`i18n` attributes in templates, `$localize` in code) with
`ng extract-i18n` into the English source file `src/Sfe.Web/src/locale/messages.xlf` (XLIFF 2.0: each text with its
id, its description and meaning for translators, where it is used). It builds the client (about 20 s). Node.js 22+ and
the client's packages (`npm ci` in `src/Sfe.Web`).

```sh
# write the source file again (commit it with the texts that changed)
node agents/tools/i18n/extract.mjs

# check that the committed file is up to date (exit code 1 when not); writes only to a temporary folder
node agents/tools/i18n/extract.mjs --check
```

- `--check` ignores the locations (file and line: any edit above a text moves them); the written file keeps them.
- The ids are Angular's (a hash of the text and its meaning) unless the text sets one (`$localize`:@@id:Text``): a
  text rewritten gets a new id, so a translation of the old wording is dropped rather than shown for the new one.
- Texts in templates not marked yet are counted by a unit test, not by this tool
  (`src/app/core/i18n/hard-coded-texts.spec.ts`).
