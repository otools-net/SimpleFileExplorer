import { loadTranslations, clearTranslations } from '@angular/localize';
import { icu, textLocale } from './icu';

describe('icu', () => {
    afterEach(() => {
        clearTranslations();
        $localize.locale = undefined;
    });

    it('picks the plural form and formats the count', () => {
        const files = (count: number) =>
            icu($localize`{count, plural, one {# file} other {# files}} deleted.`, { count });
        expect(files(1)).toBe('1 file deleted.');
        expect(files(0)).toBe('0 files deleted.');
        expect(files(1234)).toBe(`${(1234).toLocaleString()} files deleted.`);
    });

    it('takes plain values and choices', () => {
        const text = $localize`{kind, select, folder {Folder} other {File}} “{name}” renamed.`;
        expect(icu(text, { kind: 'folder', name: 'a' })).toBe('Folder “a” renamed.');
        expect(icu(text, { kind: 'file', name: 'b' })).toBe('File “b” renamed.');
    });

    it('is UK English until a translation is loaded', () => {
        expect(textLocale()).toBe('en-GB');
    });

    it('uses a loaded translation and the plural rules of its language', () => {
        const files = (count: number) =>
            icu($localize`:@@test.files:{count, plural, one {# file} other {# files}}`, { count });
        expect(files(2)).toBe('2 files');

        loadTranslations({
            'test.files': '{count, plural, one {# plik} few {# pliki} many {# plików} other {# pliku}}',
        });
        $localize.locale = 'pl';
        expect([files(1), files(3), files(5)]).toEqual(['1 plik', '3 pliki', '5 plików']);
    });
});
