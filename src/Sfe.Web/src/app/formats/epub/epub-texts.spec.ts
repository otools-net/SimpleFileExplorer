import { epubTexts } from './epub-texts';

describe('epubTexts', () => {
    it('names the three layouts, each with a hint as a whole sentence', () => {
        const modes = epubTexts.modes();
        expect(modes.map((mode) => [mode.id, mode.label])).toEqual([
            ['pages', 'Page'],
            ['spread', 'Two pages'],
            ['scroll', 'Scroll'],
        ]);
        for (const mode of modes) expect(mode.title).toMatch(/^Show .*\.$/);
    });

    it('tells how far through the book, "…" while its pages are counted', () => {
        expect(epubTexts.progress(0.424)).toBe('42 %');
        expect(epubTexts.progress(null)).toBe('…');
        expect(epubTexts.progress(undefined)).toBe('…');
        expect(epubTexts.progressHint(true)).toBe('Counting the pages of the book…');
        expect(epubTexts.progressHint(false)).toBe('How far through the book you are.');
    });

    it('says why a book did not open, in whole sentences', () => {
        expect(epubTexts.couldNotOpen('No container')).toBe('Could not open the book: No container.');
        expect(epubTexts.couldNotOpen('No container.')).toBe('Could not open the book: No container.');
    });
});
