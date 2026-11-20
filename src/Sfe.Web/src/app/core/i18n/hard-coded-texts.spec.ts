import { writeFileSync } from 'node:fs';
import baseline from '../../../testing/hard-coded-texts.json';
import { appTemplates, hardCodedTexts } from '../../../testing/template-texts';

/**
 * Texts in templates must be marked for translation (`i18n`, `i18n-title`…). The ones written before are counted per
 * file in `src/testing/hard-coded-texts.json`, which only goes down as each area is moved (release 2.0 steps
 * 2.3–2.8). Writing it again: `SFE_TEXTS_BASELINE=write npm test -- --include src/app/core/i18n`.
 * See agents/docs/development/translations.md.
 */
const BASELINE_FILE = 'src/testing/hard-coded-texts.json';
const allowed: Record<string, number> = baseline;

describe('texts in templates', () => {
    const found = new Map<string, ReturnType<typeof hardCodedTexts>>();
    for (const [path, source] of appTemplates()) {
        const texts = hardCodedTexts(source, path);
        if (texts.length) found.set(path, texts);
    }

    it('are marked for translation, except those counted in the baseline', () => {
        if (process.env['SFE_TEXTS_BASELINE'] === 'write') {
            const counts = [...found].sort(([a], [b]) => a.localeCompare(b)).map(([path, t]) => [path, t.length]);
            writeFileSync(BASELINE_FILE, `${JSON.stringify(Object.fromEntries(counts), null, 2)}\n`);
            return;
        }
        const over = [...found]
            .filter(([path, texts]) => texts.length > (allowed[path] ?? 0))
            .map(([path, texts]) => `src/${path} (${allowed[path] ?? 0} allowed):\n` + list(texts));
        expect(over, 'texts not marked for translation (add i18n, or translate="no" for names)').toEqual([]);
    });

    it('has a baseline that went down with the texts moved', () => {
        const stale = Object.entries(allowed)
            .filter(([path, count]) => (found.get(path)?.length ?? 0) < count)
            .map(([path, count]) => `src/${path}: ${found.get(path)?.length ?? 0} found, ${count} allowed`);
        expect(stale, `fewer texts than ${BASELINE_FILE} allows: write it again (SFE_TEXTS_BASELINE=write)`).toEqual(
            [],
        );
    });
});

function list(texts: ReturnType<typeof hardCodedTexts>): string {
    return texts.map(({ line, text }) => `  line ${line}: ${text}`).join('\n');
}
