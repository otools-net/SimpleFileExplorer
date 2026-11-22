import { item } from '../../testing/fakes';
import { PropertyJob } from './file.models';
import { attributesText, checksumText, countText, folderOf, itemKind, sizeText, totalsText } from './properties-texts';

function job(overrides: Partial<PropertyJob> = {}): PropertyJob {
    return {
        id: 'j',
        kind: 'count',
        path: 'Media',
        algorithm: null,
        done: true,
        files: 3,
        folders: 1,
        bytes: 2048,
        total: 0,
        capped: false,
        leftOut: 0,
        checksum: null,
        error: null,
        ...overrides,
    } as PropertyJob;
}

describe('properties texts', () => {
    it('writes sizes with their bytes counted', () => {
        expect(sizeText(1)).toBe('1 byte');
        expect(sizeText(512)).toBe('512 bytes');
        expect(sizeText(2_500_000)).toBe('2.4 MB (2,500,000 bytes)');
        expect(sizeText(null)).toBe('');
    });

    it('says what the index knows of a folder', () => {
        expect(totalsText(null)).toBe('In the index: …');
        expect(totalsText({ indexed: false, files: 0, folders: 0, bytes: 0 })).toBe(
            'Not in the index yet. Count it on the disk to see what it holds.',
        );
        expect(totalsText({ indexed: true, files: 1, folders: 0, bytes: 1024 })).toBe(
            'In the index: 1 file, 0 folders, 1 KB, as of the last scan.',
        );
    });

    it('says where a count on the disk stands', () => {
        expect(countText(job({ done: false }))).toBe('On the disk: 3 files, 1 folder, 2 KB so far…');
        expect(countText(job({ capped: true }))).toBe('On the disk: more than 3 files, 1 folder, 2 KB.');
        expect(countText(job({ error: 'Stopped.' }))).toBe(
            'On the disk: 3 files, 1 folder, 2 KB when the count was stopped.',
        );
        expect(countText(job({ leftOut: 2 }))).toBe(
            'On the disk: 3 files, 1 folder, 2 KB. 2 links or unreadable folders were left out.',
        );
        expect(countText(job({ error: 'Access denied.' }))).toBe('Access denied.');
    });

    it("says how far a checksum's reading went", () => {
        expect(checksumText(job({ kind: 'checksum', done: false, bytes: 512, total: 2048 }))).toBe(
            'Reading the file… 25% (512 B of 2 KB)',
        );
    });

    it('lists attributes, kinds and folders', () => {
        expect(attributesText({ hidden: true, readOnly: true, isLink: false })).toBe('hidden and read-only');
        expect(itemKind(item('a/b.jpg'))).toBe('JPG image');
        expect(itemKind({ ...item('a/b.jpg'), extension: null })).toBe('Image');
        expect(itemKind(item('a/b', { kind: 'directory' }))).toBe('Folder');
        expect(folderOf('Media/2024/beach.jpg', 'Root', false)).toBe('Root / Media / 2024');
        expect(folderOf('', 'Root', true)).toBe('This is the top of the connection.');
    });
});
