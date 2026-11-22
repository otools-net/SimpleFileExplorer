import { FileJobItem, FileJobView } from './file.models';
import {
    clashSide,
    clipboardText,
    compressTitle,
    couldNotStart,
    destination,
    extractTitle,
    jobStatus,
    keptToast,
    notDoneToast,
    stoppedToast,
    takenToast,
    transferredToast,
    transferTitle,
    whereOf,
} from './operations-texts';

function job(values: Partial<FileJobView>): FileJobView {
    return {
        id: 1,
        kind: 'copy',
        state: 'running',
        target: 'Holidays',
        total: 3,
        done: 1,
        bytesTotal: 0,
        bytesDone: 0,
        current: null,
        conflict: null,
        results: [],
        started: '2026-11-22T10:00:00Z',
        finished: null,
        ...values,
    };
}

const done = (path: string, newPath: string | null = null): FileJobItem => ({ path, newPath, status: 'done' });

describe('the texts of the operations panel', () => {
    it('names where an operation goes, between two places too', () => {
        expect(whereOf('Photos/Holidays', 'Media')).toBe('“Holidays”');
        expect(whereOf('', 'Media')).toBe('“Media”');
        expect(destination('“Holidays”', null)).toBe('to “Holidays”');
        expect(destination('“Holidays”', { from: 'Dropbox › Trips', to: 'Media' })).toBe(
            'from Dropbox › Trips to Media',
        );
    });

    it('titles moves and copies by their state', () => {
        expect(transferTitle(job({ kind: 'move', total: 1 }), 'to “Holidays”')).toBe('Moving 1 item to “Holidays”');
        expect(transferTitle(job({ state: 'cancelled' }), 'to “Holidays”')).toBe(
            'Stopped: 1 of 3 items copied to “Holidays”',
        );
        expect(transferTitle(job({ state: 'completed', results: [done('a'), done('b')] }), 'to “Holidays”')).toBe(
            '2 items copied to “Holidays”',
        );
    });

    it('titles compressions and extractions', () => {
        expect(compressTitle(job({ kind: 'compress' }), '“Media”')).toBe('Compressing into “Media”');
        expect(
            compressTitle(job({ kind: 'compress', state: 'completed', results: [done('a', 'x/trip.zip')] }), '“Media”'),
        ).toBe('“trip.zip” made in “Media”');
        expect(compressTitle(job({ kind: 'compress', state: 'completed' }), '“Media”')).toBe('No zip made');
        expect(extractTitle(job({ kind: 'extract', current: 'x/trip.zip' }), '“Media”')).toBe(
            'Extracting “trip.zip” to “Media”',
        );
        expect(extractTitle(job({ kind: 'extract', state: 'cancelled' }), '“Media”')).toBe(
            'Stopped: nothing extracted to “Media”',
        );
    });

    it('says how far an operation is', () => {
        expect(jobStatus(job({ state: 'waiting' }), null)).toBe('Waiting for your answer…');
        expect(jobStatus(job({ bytesTotal: 2048, bytesDone: 1024, current: 'a/b.jpg' }), null)).toBe(
            '1 of 3 · 1 KB of 2 KB · b.jpg',
        );
        expect(jobStatus(job({ kind: 'compress' }), null)).toBe('Waiting for its turn…');
        expect(jobStatus(job({ kind: 'extract', bytesTotal: 2048, bytesDone: 1024 }), 'about 3 min left')).toBe(
            '1 KB of 2 KB read · about 3 min left',
        );
    });

    it('describes both sides of a name clash', () => {
        expect(clashSide({ name: 'a', isFolder: true, size: null, modified: null })).toBe('folder');
        expect(clashSide({ name: 'a', isFolder: false, size: 2048, modified: '2026-01-01T12:00:00Z' })).toMatch(
            /^2 KB, modified .*2026/,
        );
    });

    it('says what is cut or copied, as a whole sentence', () => {
        const item = { name: 'beach.jpg' } as never;
        expect(clipboardText({ kind: 'move', items: [item], connection: null })).toBe(
            '“beach.jpg” cut. Open a folder to paste it into.',
        );
        expect(clipboardText({ kind: 'copy', items: [item, item], connection: null })).toBe(
            '2 items copied. Open a folder to paste them into.',
        );
        expect(keptToast('move', 1)).toBe("1 item cut. Paste it into a folder with Ctrl+V or the folder's menu.");
    });

    it('writes the toasts ending a move or copy', () => {
        expect(transferredToast('move', 2, '“Holidays”')).toBe('2 items moved to “Holidays”.');
        expect(stoppedToast('copy', 0, '“Holidays”')).toBe('Stopped. Nothing was copied to “Holidays”.');
        expect(stoppedToast('move', 1, '“Holidays”')).toBe('Stopped. 1 item was moved to “Holidays” before.');
        expect(notDoneToast('move', [{ path: 'a/clip.mp4' }])).toBe('“clip.mp4” could not be moved.');
        expect(notDoneToast('copy', [{ path: 'a', message: 'It is in use.' }, { path: 'b' }])).toBe(
            '2 items could not be copied. It is in use.',
        );
        expect(couldNotStart('retry', 'Gone.')).toBe('Could not try again: Gone.');
        expect(takenToast('move', ['a', 'b'], '“Saved”')).toBe(
            '2 names are already in “Saved”, so those items were not moved.',
        );
    });
});
