import { formatSize } from '../../core/format';
import { StorageUsage } from '../library.models';

/**
 * The sentences of the sidebar's storage bar, as whole messages marked for translation
 * (agents/docs/development/translations.md).
 */

/** From this share used the bar turns orange, and red from {@link FULL}. */
export const NEARLY_FULL = 0.85;
export const FULL = 0.95;

/** How full the bar shows the storage. */
export type StorageLevel = 'normal' | 'nearly-full' | 'full';

const percentFormat = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 0 });

/** The share used, from 0 to 1; null when the total isn't known. */
export function usedShare(usage: StorageUsage): number | null {
    if (!usage.reported || usage.used === null || !usage.total) return null;
    return Math.min(1, Math.max(0, usage.used / usage.total));
}

export function storageLevel(share: number | null): StorageLevel {
    if (share === null || share < NEARLY_FULL) return 'normal';
    return share < FULL ? 'nearly-full' : 'full';
}

/** "120 GB used of 500 GB, 380 GB free": the line under the bar. */
export function storageLine(usage: StorageUsage): string {
    if (!usage.reported || usage.used === null) return $localize`Free space not reported`;
    const used = formatSize(usage.used);
    if (usage.total === null || usage.free === null) return $localize`${used}:used: used`;
    const total = formatSize(usage.total);
    const free = formatSize(usage.free);
    return $localize`${used}:used: used of ${total}:total:, ${free}:free: free`;
}

/**
 * The bar's hint: what is measured and how full it is. `connection`: the connection's name, null for the disk
 * holding the library.
 */
export function storageHint(usage: StorageUsage, connection: string | null): string {
    const share = usedShare(usage);
    const where = connection === null ? diskSentence(usage, share) : accountSentence(usage, share, connection);
    switch (storageLevel(share)) {
        case 'full':
            return `${where} ${$localize`It is almost full.`}`;
        case 'nearly-full':
            return `${where} ${$localize`It is getting full.`}`;
        default:
            return where;
    }
}

function diskSentence(usage: StorageUsage, share: number | null): string {
    if (share !== null) {
        const percent = percentFormat.format(share);
        return $localize`The disk holding the library is ${percent}:percent: full.`;
    }
    return usage.reported
        ? $localize`The space used on the disk holding the library.`
        : $localize`The disk holding the library doesn't report its free space.`;
}

function accountSentence(usage: StorageUsage, share: number | null, connection: string): string {
    if (share !== null) {
        const percent = percentFormat.format(share);
        return $localize`The account of the connection “${connection}:connection:” is ${percent}:percent: full.`;
    }
    return usage.reported
        ? $localize`The space the connection “${connection}:connection:” uses. It reports no limit.`
        : $localize`The connection “${connection}:connection:” doesn't report its free space.`;
}
