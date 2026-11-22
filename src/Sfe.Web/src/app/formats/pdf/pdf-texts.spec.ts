import { pdfTexts, SCALE_VALUES } from './pdf-texts';

describe('pdfTexts', () => {
    it('names the zooms, fitted ones by name and the others as a percentage', () => {
        expect(SCALE_VALUES.map(pdfTexts.scale)).toEqual([
            'Automatic',
            'Page width',
            'Whole page',
            '50 %',
            '75 %',
            '100 %',
            '125 %',
            '150 %',
            '200 %',
            '300 %',
        ]);
        expect(pdfTexts.scale('4')).toBe('400 %');
        expect(pdfTexts.zoom('Page width')).toBe('Zoom: Page width');
    });

    it('gives each zoom a hint as a whole sentence', () => {
        expect(pdfTexts.scaleHint('page-width')).toBe('Make the pages as wide as the window.');
        expect(pdfTexts.scaleHint('1.5')).toBe('Show the pages at 150 % of their size.');
        for (const value of SCALE_VALUES) expect(pdfTexts.scaleHint(value)).toMatch(/^[A-Z].*\.$/);
    });

    it('counts the pages and the matches', () => {
        expect(pdfTexts.pageCount(1)).toBe('1 page');
        expect(pdfTexts.pageCount(12)).toBe('12 pages');
        expect(pdfTexts.matches(3, 7)).toBe('3 of 7');
        expect(pdfTexts.matches(0, 1)).toBe('0 of 1');
        expect(pdfTexts.matches(0, 0)).toBe('No matches');
    });

    it('says why a document is not shown', () => {
        expect(pdfTexts.couldNotShow('Bad XRef entry')).toBe('Could not show the document: Bad XRef entry.');
        expect(pdfTexts.couldNotShow('Bad XRef entry.')).toBe('Could not show the document: Bad XRef entry.');
        expect(pdfTexts.notPdf()).toBe("This file isn't a PDF that can be shown.");
    });
});
