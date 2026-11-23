import { ArchiveItem } from '../../core/archive.models';
import { ArchiveListing } from '../format.models';
import { archiveTexts as texts, entryHint, entrySize, entrySizeHint, folderCounts, foundStatus } from './archive-texts';

const entry = (kind: ArchiveItem['kind'], more: Partial<ArchiveItem> = {}): ArchiveItem => ({
    kind,
    id: 1,
    name: 'readme.txt',
    size: 2048,
    packed: 1000,
    modified: null,
    encrypted: false,
    ...more,
});

describe('archive texts', () => {
    it('counts the folders between the crumbs and the entries left out', () => {
        expect(texts.skipped(1)).toBe('1 folder in between is not shown.');
        expect(texts.skipped(4996)).toBe('4,996 folders in between are not shown.');
        expect(texts.leftOut(1)).toBe(
            '1 entry is not listed: its name leads out of the archive, or it is nested too deep.',
        );
        expect(texts.leftOut(3)).toMatch(/^3 entries are not listed: their names lead out of the archive, or they/);
    });

    it('says how much of the list is read', () => {
        expect(texts.reading(0)).toBe('Reading the list of entries…');
        expect(texts.reading(340000)).toBe('Reading the list of entries… 340,000 so far.');
    });

    it('quotes the names in its hints', () => {
        expect(texts.topOf('trip.zip')).toBe('Go to the top of “trip.zip”.');
        expect(texts.crumbHint('Day 1', false)).toBe('Open the folder “Day 1”.');
        expect(texts.crumbHint('Day 1', true)).toBe('This is the folder shown.');
        expect(texts.openFile('a.txt', true)).toBe('Show “a.txt” here, in place of the list.');
        expect(texts.openFile('a.exe', false)).toBe('Open “a.exe” in a new tab.');
        expect(texts.extractOneHint('Day 1')).toBe('Extract “Day 1” into a folder of the library, picked next.');
        expect(texts.foundNone('zebra', true)).toBe('No name in this folder holds “zebra”.');
        expect(texts.foundNone('zebra', false)).toBe('No name in this archive holds “zebra”.');
    });

    it('sizes a file in bytes and counts a folder in files', () => {
        expect(entrySize(entry('file'))).toBe('2 KB');
        expect(entrySizeHint(entry('file'))).toBe('2,048 bytes');
        expect(entrySizeHint(entry('file', { size: 1 }))).toBe('1 byte');
        expect(entrySize(entry('folder', { allFiles: 1 }))).toBe('1 file');
        expect(entrySize(entry('folder', { allFiles: 3 }))).toBe('3 files');
        expect(entrySizeHint(entry('folder'))).toBe('2 KB in all');
        expect(entrySize(entry('link'))).toBe('');
    });

    it('tells why an entry is only listed', () => {
        expect(entryHint(entry('file', { encrypted: true }))).toBe('This file is protected by a password.');
        expect(entryHint(entry('link'))).toContain('never created when the archive is extracted');
        expect(entryHint(entry('other'))).toContain('a device or a pipe');
        expect(entryHint(entry('file'))).toBe('readme.txt');
    });

    it("counts a folder's folders, files and size", () => {
        const listing = {
            format: 'zip',
            folder: { ...entry('folder'), folders: 1, files: 3, allFiles: 12, size: 6144 },
        } as unknown as ArchiveListing;
        expect(folderCounts(listing)).toBe('1 folder · 3 files here · 6 KB in 12 files below · ZIP');
    });

    it('keeps the reason in its error toasts', () => {
        expect(texts.searchFailed('The archive changed.')).toBe('Could not search the archive: The archive changed.');
        expect(texts.viewerNotLoaded()).toBe('Could not load the viewer of this file. Reload the page to try again.');
    });

    it('says where a search stands', () => {
        const found = { query: 'summ', folder: null, rows: [] as unknown[], next: null, searching: true };
        expect(foundStatus(found)).toBe('Searching the archive…');
        expect(foundStatus({ ...found, folder: 2 })).toBe('Searching this folder…');
        expect(foundStatus({ ...found, rows: [1, 2] })).toBe('Searching the archive… 2 names hold “summ” so far.');
        expect(foundStatus({ ...found, rows: [1], searching: false })).toBe('1 name holds “summ”.');
        expect(foundStatus({ ...found, rows: [1, 2], folder: 2, searching: false })).toBe(
            '2 names in this folder hold “summ”.',
        );
        expect(foundStatus({ ...found, rows: [1, 2], next: 'f2', searching: false })).toBe(
            '2 names hold “summ” so far. Scroll for more.',
        );
    });
});
