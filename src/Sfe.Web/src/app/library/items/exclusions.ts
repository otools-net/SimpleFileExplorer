import { FolderExclusions } from '../../core/models';

/* How a folder kept out of scans, trickplay or thumbnails is described: a short chip in the listing, a sentence in its hint. */

/** The short words of a folder's chip ('not scanned · no previews', 'skipped'); null when it is excluded from nothing. */
export function exclusionChip(excluded: FolderExclusions | null | undefined): string | null {
    if (!excluded)
        return null;
    const { scan, trickplay, thumbnails } = excluded;
    if (scan && trickplay && thumbnails)
        return 'skipped';
    const parts = [
        scan ? 'not scanned' : null,
        trickplay && thumbnails ? 'no previews' : thumbnails ? 'no thumbnails' : trickplay ? 'no trickplay' : null,
    ].filter(part => part !== null);
    return parts.length ? parts.join(' · ') : null;
}

/**
 * The sentence of a folder's hint: what it is kept out of and, when that comes from a folder above it, which one.
 * `path` is the folder's own path.
 */
export function exclusionTitle(excluded: FolderExclusions | null | undefined, path: string): string | null {
    if (!excluded || !exclusionChip(excluded))
        return null;
    const kept = [
        excluded.scan ? 'scans (what the index has of it stays as it was)' : null,
        excluded.trickplay ? 'trickplay' : null,
        excluded.thumbnails ? 'thumbnails' : null,
    ].filter(part => part !== null);
    const list = kept.length > 1 ? `${kept.slice(0, -1).join(', ')} and ${kept[kept.length - 1]}` : kept[0];
    const parents = [...new Set([excluded.scan, excluded.trickplay, excluded.thumbnails].filter(from => from && from !== path))];
    return `Kept out of ${list} by an administrator` + (parents.length ? `, through ${parents.map(from => `“${from}”`).join(' and ')}` : '') + '.';
}
