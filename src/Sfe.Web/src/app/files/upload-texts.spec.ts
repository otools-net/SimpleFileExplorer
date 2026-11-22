import { UploadInfo } from './file.models';
import {
    describeUpload,
    tooLargeToast,
    uploadedToast,
    uploadFolderHint,
    uploadState,
    uploadStatus,
    uploadTitle,
} from './upload-texts';
import { UploadItem } from './uploads';

function item(values: Partial<UploadItem>): UploadItem {
    return {
        key: 1,
        file: null,
        name: 'clip.mp4',
        size: 200,
        modified: null,
        id: null,
        sent: 50,
        state: 'uploading',
        exists: false,
        choice: 'ask',
        message: null,
        tries: 0,
        ...values,
    };
}

describe('the texts of uploads', () => {
    it('titles a batch while it runs and once done', () => {
        expect(uploadTitle(true, 1, 0, 0, '“Media”')).toBe('Uploading 1 file to “Media”');
        expect(uploadTitle(false, 3, 0, 0, '“Media”')).toBe('0 files uploaded to “Media”');
        expect(uploadTitle(false, 3, 2, 1, '“Media”')).toBe('2 files uploaded to “Media”, 1 failed');
    });

    it('says how far a batch and each file are', () => {
        expect(uploadStatus({ done: 1, total: 2 }, { sent: 1024, total: 2048 }, true, null)).toBe(
            '1 of 2 · 1 KB of 2 KB · paused',
        );
        expect(uploadStatus({ done: 1, total: 2 }, { sent: 1024, total: 2048 }, false, 1024)).toBe(
            '1 of 2 · 1 KB of 2 KB · 1 KB/s',
        );
        expect(uploadState(item({}))).toBe('25%');
        expect(uploadState(item({ state: 'paused' }))).toBe('paused at 25%');
        expect(uploadState(item({ state: 'interrupted' }))).toBe('25%, interrupted');
        expect(uploadState(item({ state: 'failed' }))).toBe('failed');
        expect(uploadState(item({ state: 'skipped' }))).toBe('skipped');
    });

    it('writes the toasts of uploads', () => {
        expect(uploadedToast(1, '“Media”')).toBe('1 file uploaded to “Media”.');
        expect(tooLargeToast(['a/big.iso'], 100)).toBe(
            '“big.iso” is larger than the 100 MB a file may have here, so it was left out.',
        );
        expect(tooLargeToast(['a', 'b'], 100)).toBe(
            '2 files are larger than the 100 MB a file may have here, so they were left out.',
        );
    });

    it("describes an unfinished upload in the administration's list", () => {
        const upload = { size: 10, received: 5, ownerName: 'alice', lastActivity: '2026-10-17T12:00:00Z' };
        expect(describeUpload(upload as UploadInfo)).toMatch(
            /^5 B of 10 B \(50%\) · Sent by alice, last received .+\.$/,
        );
        expect(uploadFolderHint('')).toBe('Open the top of the library.');
        expect(uploadFolderHint('Media/In')).toBe('Open “Media/In”.');
    });
});
