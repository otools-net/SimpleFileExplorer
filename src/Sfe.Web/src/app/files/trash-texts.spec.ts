import { describeTrashed, openFolderHint, purgedText, trashIntro, trashSizeText } from './trash-texts';

describe('trash texts', () => {
    it('says how long the trash keeps things', () => {
        expect(trashIntro(null)).not.toContain('Each');
        expect(trashIntro(1)).toContain('Each is deleted for good 1 day after it was deleted.');
        expect(trashIntro(0)).toContain('until they are restored or deleted for good.');
    });

    it('describes an entry: what it holds, when and by whom', () => {
        const entry = {
            id: 1,
            path: 'Old',
            name: 'Old',
            isFolder: true,
            files: 3,
            size: 2048,
            deletedAt: '2026-11-22T10:00:00Z',
            deletedBy: 'Sam',
            previousVersion: false,
        };
        expect(describeTrashed(entry as never)).toMatch(/^3 files · 2 KB · Deleted .+ by Sam\.$/);
        expect(describeTrashed({ ...entry, deletedBy: null, previousVersion: true } as never)).toMatch(
            /· Saved over [^·]+\.$/,
        );
    });

    it('says what the whole trash holds, and how many sizes it does not know', () => {
        expect(trashSizeText({ bytes: 2048, unknown: 0 })).toBe('The trash holds 2 KB.');
        expect(trashSizeText({ bytes: 2048, unknown: 1 })).toBe(
            "The trash holds 2 KB, not counting 1 item whose size isn't known.",
        );
    });

    it('names the folder an entry was in and counts what was purged', () => {
        expect(openFolderHint('Media')).toBe('Open “Media”, where it was.');
        expect(openFolderHint('')).toBe('Open the top of the library.');
        expect(purgedText(1)).toBe('1 item deleted for good.');
    });
});
