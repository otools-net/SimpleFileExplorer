import { StorageUsage } from '../library.models';
import { storageHint, storageLevel, storageLine, usedShare } from './storage-texts';

const GB = 1024 ** 3;
const usage = (used: number, total: number | null): StorageUsage => ({
    reported: true,
    used,
    total,
    free: total === null ? null : total - used,
});
const notReported: StorageUsage = { reported: false, used: null, total: null, free: null };

describe('storage texts', () => {
    it('says what is used, of what, and what is free', () => {
        expect(storageLine(usage(120 * GB, 500 * GB))).toBe('120 GB used of 500 GB, 380 GB free');
        expect(storageLine(usage(3 * GB, null))).toBe('3 GB used');
        expect(storageLine(notReported)).toBe('Free space not reported');
    });

    it('turns orange from 85 % used and red from 95 %', () => {
        expect(storageLevel(usedShare(usage(84, 100)))).toBe('normal');
        expect(storageLevel(usedShare(usage(85, 100)))).toBe('nearly-full');
        expect(storageLevel(usedShare(usage(95, 100)))).toBe('full');
        expect(storageLevel(usedShare(usage(5, null)))).toBe('normal');
        expect(usedShare(notReported)).toBeNull();
    });

    it('names what is measured and how full it is', () => {
        expect(storageHint(usage(50, 100), null)).toBe('The disk holding the library is 50% full.');
        expect(storageHint(usage(96, 100), 'Box')).toBe(
            'The account of the connection “Box” is 96% full. It is almost full.',
        );
        expect(storageHint(usage(90, 100), null)).toContain('It is getting full.');
        expect(storageHint(usage(5, null), 'Box')).toBe('The space the connection “Box” uses. It reports no limit.');
        expect(storageHint(notReported, null)).toBe("The disk holding the library doesn't report its free space.");
    });
});
