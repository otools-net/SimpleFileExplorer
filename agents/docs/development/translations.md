# Translations: texts marked for translation

How the client's texts are made translatable. Only English ships (owner's choice, release 2.0): the texts are marked
and extracted so that a language can be added later without touching every file. Code: `angular.json` (`i18n`,
`polyfills`, `extract-i18n`), `src/Sfe.Web/src/app/core/i18n/icu.ts`, `core/format.ts`, `src/locale/messages.xlf`,
`src/testing/template-texts.ts`, `src/app/core/i18n/hard-coded-texts.spec.ts`, `agents/tools/i18n/`. The words
themselves follow [ui-texts](ui-texts.md).

## The choice (2026-11-21)

**Angular's own i18n** (`@angular/localize`: `i18n` attributes in templates, `$localize` in code) plus
**`intl-messageformat`** (FormatJS) for ICU messages in code. Why, against Transloco:

- Nothing to load or look up while English is the only language: an unmarked `$localize` returns its text; the cost is
  the `$localize` polyfill (~0.5 KB gzip, plus the en-GB locale data the build adds: polyfills 1.9 KB in all).
  `intl-messageformat` is ~10 KB gzip, in the bundle once code uses `icu()`.
- The texts stay in the templates (no keys to invent, no JSON to keep in step by hand); `ng extract-i18n` finds them,
  with descriptions and meanings for translators; templates get ICU plurals and selects natively.
- A language later: either one build per language (`localize` in `angular.json`, served under `/fr/`…), or the
  current build with translations loaded before the app starts (`loadTranslations()` in `main.ts`, from a JSON made
  from the `.xlf`): no re-render, a reload switches. Decide then; a language setting (user level, browser default)
  comes with it, not before.
- `sourceLocale` is `en-GB` (UK English, [ui-texts](ui-texts.md)): the build registers its locale data and Angular's
  pipes (`| number`) use it; numbers are written as in en-US.

## Writing a text

- **Templates:** `i18n` on the element holding the text, `i18n-title`, `i18n-placeholder`, `i18n-aria-label`… on
  attributes people read. A description helps a short or ambiguous text:
  `<button i18n="empties the trash">Empty</button>` (`meaning|description@@id`, each part optional:
  `i18n-title="@@trash.empty"` only sets the id).
- **Code:** `` $localize`Text` ``; description first: `` $localize`:size unit, bytes:B` ``. Values go in as
  `` ${value}:name: `` (a named placeholder: `` $localize`Deleted “${name}:name:”.` ``).
- **Long texts in code:** one `$localize` per text, never `$localize` pieces joined with `+` (two messages for
  translators). Break it over lines with a `\` at the end of each line and the next line starting at column 0: the
  text has neither the break nor an indent (`extract.mjs` and the run-time text agree), and lines stay within 120
  characters (`settings/tabs/*.ts` are written so).
- **Counts and choices in code:** one ICU message for the whole sentence, never a count glued to a word —
  `` icu($localize`{count, plural, one {# file} other {# files}} deleted.`, { count }) `` (`core/i18n/icu.ts`).
  Plural rules follow the text's language (`$localize.locale`, en-GB until a translation is loaded); `#` and
  `{n, number}` follow the browser's region, as dates and sizes in `core/format.ts` do. `plural()` in `format.ts` is
  deprecated: its calls go with each area's texts (2.3–2.8), the last area removes it.
- **Not to translate** (a brand, a file name, a code shown as is): put `translate="no"` on the element; the test
  skips it.
- **Sizes:** `formatSize()` takes its units from `$localize` (B, KB… are translatable).
- After marking texts: `node agents/tools/i18n/extract.mjs` writes `src/Sfe.Web/src/locale/messages.xlf` (the English
  source file, XLIFF 2.0), committed with them; `--check` says whether it is up to date (ignoring line locations).

## The test of hard-coded texts

`hard-coded-texts.spec.ts` parses every template of `src/app` (`.html` files and inline `template:`s) with Angular's
own parser (`parseTemplate`, `src/testing/template-texts.ts`) and counts the texts not marked: text nodes and
interpolations holding two letters in a row, and the attributes `title`, `placeholder`, `alt`, `aria-label`,
`aria-description`, `label`. Counts per file are in `src/testing/hard-coded-texts.json` (1,523 in 99 files on
2026-11-21):

- more than the baseline (or any in a new file) fails, listing the texts with their lines;
- fewer fails too, so the baseline follows the work: write it again with
  `SFE_TEXTS_BASELINE=write npm test -- --include src/app/core/i18n` (Git Bash).

Not counted: strings in code (`$localize` is checked by review and by 2.3–2.8 going through each area), texts bound
in expressions (`[title]="'Close'"`), `svg`, `style`, `script`.

## Gotchas

- An attribute with an interpolation and `i18n-*` (`aria-label="Level of {{ name }}" i18n-aria-label`) is bound as
  a **property** (`ariaLabel`), which jsdom doesn't reflect to the attribute (specs find nothing by
  `[aria-label=…]`): bind `[attr.aria-label]` to a `$localize` text from code instead. `title` is fine (a reflected
  property).
- A text chosen by a condition (`cond ? 'Create' : 'Save'`) becomes `@if` around two `<ng-container i18n>`s inside
  the element; on an element projected by a selector (`sheetFooter`), keep the element itself outside the `@if` and
  take a changing `title` from code (`$localize` fields).
- ICU plurals in templates: `{n, plural, one {{{ n }} group} other {{{ n }} groups}}` (Angular has no `#`); a
  line break is safe between cases, not inside one.
- A text split into elements (`<span translate="no">ffmpeg</span> <span i18n>(optional)</span>`) loses the space
  between them: Angular drops whitespace-only text between two elements. Keep the space inside one of them.
- Error messages shown with a prefix (`Could not load the spaces: …`): keep the reason alone in the signal and write
  the sentence in the template (`<p i18n>Could not load the spaces: {{ error }}</p>`).

- `$localize` at module level (the settings tabs, `EXTERNAL_PLAYERS`) runs when the module is imported: a translation
  loaded at run time must be loaded before the app's modules are (`main.ts` importing the app dynamically after
  `loadTranslations()`); a build per language has no such issue.
- Plain texts inside an element marked `i18n` carry no `i18n` of their own in the parsed template (only texts with
  an interpolation do): `template-texts.ts` counts what is inside a marked element as marked.

- Specs reading files use `node:fs` through `src/testing/node.d.ts` (a few declarations: `@types/node` isn't
  installed, it would change timer types in every spec); `process.cwd()` is `src/Sfe.Web` under `npm test`.
- In the compiler's typings `TmplAstText` has no `i18n`, but texts inside an element marked `i18n` carry it at run
  time: `template-texts.ts` reads it through a cast.
- `@angular/localize` must be the same version as `@angular/core` (`npm install @angular/localize@<core version>`;
  a range picks the latest and fails on the peer dependency).
- `ng extract-i18n` builds the client in memory (~20 s); it doesn't touch `wwwroot`.
- The unit-test builder takes the build's `polyfills`: `$localize` exists in specs without importing it.
