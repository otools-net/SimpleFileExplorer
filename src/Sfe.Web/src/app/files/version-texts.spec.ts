import { TrashItem } from './file.models';
import {
    notRestoredText,
    restoredCopyText,
    restoreOverQuestion,
    versionCount,
    versionDetails,
    versionHints,
    versionsIntro,
} from './version-texts';

function version(overrides: Partial<TrashItem> = {}): TrashItem {
    return {
        id: 4,
        path: 'Docs/notes.txt',
        name: 'notes.txt',
        isFolder: false,
        size: 2048,
        files: null,
        deletedAt: '2026-11-22T14:03:00Z',
        deletedBy: 'Sam',
        deletedById: 2,
        previousVersion: true,
        ...overrides,
    };
}

describe('version texts', () => {
    it('says what a version is, its size and who saved over the file', () => {
        expect(versionsIntro()).toContain('the content it had is kept in the trash');
        expect(versionDetails(version())).toBe('2 KB · Saved over by Sam.');
        expect(versionDetails(version({ deletedBy: null, size: null }))).toBe('');
    });

    it('asks before restoring over the file, and says what became of it', () => {
        const question = restoreOverQuestion('notes.txt', '22 Nov');
        expect(question).toMatch(/^Put the version of 22 Nov in place of “notes\.txt”\? /);
        expect(question).toMatch(/ The file as it is now is kept as a version in the trash\.$/);
        expect(restoredCopyText('notes (2).txt')).toBe('The version is restored as “notes (2).txt”.');
        expect(notRestoredText('A scan is running.')).toBe('Could not restore the version: A scan is running.');
        expect(notRestoredText(null)).toBe('Could not restore the version.');
    });

    it('counts the versions for Properties', () => {
        expect(versionCount(0, false)).toBe('None');
        expect(versionCount(1, false)).toBe('1 version');
        expect(versionCount(3, false)).toBe('3 versions');
        expect(versionCount(100, true)).toBe('More than 100 versions');
    });

    it('gives each button a sentence as its hint', () => {
        for (const hint of Object.values(versionHints())) expect(hint).toMatch(/^[A-Z].*\.$/);
    });
});
