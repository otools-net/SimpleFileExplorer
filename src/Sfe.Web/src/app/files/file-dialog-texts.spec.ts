import { AccessGain } from './file.models';
import {
    counted,
    deletedToast,
    deleteQuestion,
    gainedFolders,
    gainText,
    leftOut,
    moreNames,
    nameOrCount,
    notDeleted,
    sizeToRead,
    transferPrompt,
    transferQuestion,
    trashUnavailable,
} from './file-dialog-texts';

const folder = { kind: 'directory' };
const file = { kind: 'file', size: 2048 };

describe('the texts of the file dialogs', () => {
    it('counts folders and files, each only when there are some', () => {
        expect(counted([folder])).toBe('1 folder');
        expect(counted([file, file])).toBe('2 files');
        expect(counted([folder, folder, file])).toBe('2 folders and 1 file');
        expect(nameOrCount(['a.jpg'])).toBe('“a.jpg”');
        expect(nameOrCount(['a', 'b', 'c'])).toBe('3 items');
        expect(moreNames(1)).toBe('and 1 more');
    });

    it('asks a delete as one sentence, by where the items go', () => {
        expect(deleteQuestion([file], null, false)).toBe('Move 1 file (2 KB) to the trash?');
        expect(deleteQuestion([folder], null, true)).toBe('Delete 1 folder and what it holds for good?');
        expect(deleteQuestion([folder, folder], null, false)).toBe('Move 2 folders and what they hold to the trash?');
        expect(deleteQuestion([folder, file], 'My Dropbox', false)).toBe(
            'Delete 1 folder and 1 file (2 KB, and what the folders hold) from “My Dropbox”?',
        );
        expect(deleteQuestion([file], '', false)).toBe('Delete 1 file (2 KB) from the connection?');
    });

    it('says what a delete did, and what it could not do', () => {
        expect(deletedToast(1, 'trash')).toBe('1 item moved to the trash.');
        expect(deletedToast(3, 'permanent')).toBe('3 items deleted for good.');
        expect(deletedToast(2, 'connection')).toBe('2 items deleted.');
        expect(notDeleted(2, 'it is in use.')).toBe('Could not delete 2 items: it is in use.');
        expect(trashUnavailable(1, true)).toBe(
            "1 item can't go to the trash: it holds links and is on another drive than the trash. " +
                'Delete it permanently instead?',
        );
        expect(trashUnavailable(2, false)).not.toContain('permanently');
    });

    it('words moves and copies, and who would gain access', () => {
        expect(transferPrompt('move', '“a.jpg”')).toBe('Move “a.jpg” into:');
        expect(transferQuestion('copy', '3 items', 'Trips')).toBe('Copy 3 items into “Trips”?');
        const gain: AccessGain = { folder: 'a/Plans', who: 'Ann', isGroup: false, before: 'read', after: 'write' };
        expect(gainText(gain)).toBe('Ann could change “Plans”, where they may only look now.');
        expect(gainText({ ...gain, isGroup: true, who: 'Visitors', before: 'hidden', after: 'read' })).toBe(
            "The group “Visitors” would see “Plans”, which they don't see now.",
        );
        expect(gainedFolders(['a/Plans'])).toBe('“Plans”');
        expect(gainedFolders(['a', 'b'])).toBe('2 folders');
    });

    it('says the size a zip reads and what batch rename leaves out', () => {
        expect(sizeToRead(2048, 1, 0)).toBe('2 KB in 1 file to read.');
        expect(sizeToRead(100, 2, 3)).toBe(
            "100 B in 2 files to read. 3 folders aren't in the index and aren't counted.",
        );
        expect(leftOut(0)).toBe('');
        expect(leftOut(2)).toBe('2 items left out.');
    });
});
