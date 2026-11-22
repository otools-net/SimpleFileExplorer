import { icu, textLocale } from '../../core/i18n/icu';
import { FolderExclusions } from '../../core/models';

/*
 * How a folder kept out of scans, trickplay or thumbnails is described: a short chip in the listing, a sentence in its
 * hint.
 */

/**
 * The short words of a folder's chip ('not scanned · no previews', 'skipped'); null when it is excluded from nothing.
 */
export function exclusionChip(excluded: FolderExclusions | null | undefined): string | null {
    if (!excluded) return null;
    const { scan, trickplay, thumbnails } = excluded;
    if (scan && trickplay && thumbnails) return $localize`:chip of a folder kept out of scans and previews:skipped`;
    const parts = [
        scan ? $localize`:chip of a folder kept out of scans:not scanned` : null,
        trickplay && thumbnails
            ? $localize`:chip of a folder without trickplay or thumbnails:no previews`
            : thumbnails
              ? $localize`:chip of a folder without thumbnails:no thumbnails`
              : trickplay
                ? $localize`:chip of a folder without trickplay:no trickplay`
                : null,
    ].filter((part) => part !== null);
    return parts.length ? parts.join(' · ') : null;
}

/**
 * The sentences of a folder's hint: what it is kept out of and, when that comes from folders above it, which ones.
 * `path` is the folder's own path.
 */
export function exclusionTitle(excluded: FolderExclusions | null | undefined, path: string): string | null {
    if (!excluded || !exclusionChip(excluded)) return null;
    const kept = [
        excluded.scan ? $localize`scans (what the index has of it stays as it was)` : null,
        excluded.trickplay ? $localize`trickplay` : null,
        excluded.thumbnails ? $localize`thumbnails` : null,
    ].filter((part) => part !== null);
    const list = new Intl.ListFormat(textLocale(), { type: 'conjunction' });
    const sentence = $localize`Kept out of ${list.format(kept)}:list: by an administrator.`;
    const parents = [
        ...new Set([excluded.scan, excluded.trickplay, excluded.thumbnails].filter((from) => from && from !== path)),
    ];
    if (!parents.length) return sentence;
    const folders = list.format(parents.map((from) => `“${from}”`));
    const where = icu(
        $localize`{count, plural, one {Set on the folder {folders}.} other {Set on the folders {folders}.}}`,
        { count: parents.length, folders },
    );
    return `${sentence} ${where}`;
}
