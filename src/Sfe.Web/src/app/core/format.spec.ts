import { formatDuration, formatElapsed, formatSize, highlightParts, highlightPattern, plural } from './format';

describe('format', () => {
    it('formats sizes and durations', () => {
        expect(formatSize(512)).toBe('512 B');
        expect(formatSize(1536)).toBe(`${(1.5).toLocaleString()} KB`);
        expect(formatSize(null)).toBe('');
        expect(formatDuration(65)).toBe('1:05');
        expect(formatDuration(3723)).toBe('1:02:03');
        expect(formatElapsed(185)).toBe('3m 05s');
        expect(plural(1, 'file')).toBe('1 file');
        expect(plural(2, 'copy', 'copies')).toBe('2 copies');
    });

    it('highlights the plain words of a search only', () => {
        const pattern = highlightPattern('*sun -cat e:jpg "big sky"');
        expect(highlightParts('Big sunset', pattern)).toEqual([
            { text: 'Big ', mark: false },
            { text: 'sun', mark: true },
            { text: 'set', mark: false },
        ]);
        expect(highlightParts('a cat.jpg', pattern)).toEqual([{ text: 'a cat.jpg', mark: false }]);
        expect(highlightPattern('-x e:mp4')).toBeNull();
    });
});
