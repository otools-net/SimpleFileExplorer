import { aspectHint, destinationLine, flipHint, overwriteHint, sizeLine } from './editor-texts';

describe('picture editor texts', () => {
    it('shows the size, and the size once edited', () => {
        expect(sizeLine({ width: 4000, height: 3000 }, null)).toBe('4000 × 3000');
        expect(sizeLine({ width: 4000, height: 3000 }, { width: 1600, height: 1200 })).toBe(
            '4000 × 3000 → 1600 × 1200',
        );
    });

    it('says where a copy goes, and what Save does when it may write over the original', () => {
        expect(destinationLine(null, false, false)).toBe(
            'Saved as a new file next to the original, with “(edited)” in its name.',
        );
        expect(destinationLine('Edited', false, false)).toBe(
            'Saved as a new file in “Edited”, in the same folders as the original.',
        );
        expect(destinationLine(null, true, false)).toBe(
            'Save writes over the original, and the version before goes to the trash. ' +
                'Save a copy makes a new file next to the original, with “(edited)” in its name.',
        );
        expect(destinationLine('Edited', true, true)).toBe(
            'Save writes over the original. ' +
                'Save a copy makes a new file in “Edited”, in the same folders as the original.',
        );
    });

    it('says whether a previous version is kept', () => {
        expect(overwriteHint(true)).toContain('This space keeps no previous version');
        expect(overwriteHint(false)).toContain('The version before goes to the trash');
    });

    it('hints at each aspect ratio', () => {
        const size = { width: 800, height: 600 };
        expect(aspectHint('free', 'Free', size)).toBe('Free: the frame takes any width and height.');
        expect(aspectHint('original', 'Original', size)).toBe(
            "Original: the frame keeps the picture's own shape, 800 × 600.",
        );
        expect(aspectHint('original', 'Original', { width: 0, height: 0 })).toBe(
            "Original: the frame keeps the picture's own shape.",
        );
        expect(aspectHint('1:1', 'Square', size)).toBe('Square (1:1): the frame keeps this shape.');
        expect(aspectHint('16:9', '16:9', size)).toBe('16:9: the frame keeps this shape.');
    });

    it('hints at turning the ratio', () => {
        expect(flipHint(null, false)).toContain('Pick a ratio first');
        expect(flipHint('9:16', false)).toBe('Use 9:16 instead: the same ratio, in portrait (X).');
        expect(flipHint('16:9', true)).toBe('Use 16:9 instead: the same ratio, in landscape (X).');
    });
});
