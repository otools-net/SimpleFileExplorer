/**
 * Files and folders the server is still writing (a copy, a transfer from a connection, a compression, an extraction)
 * carry this ending until they take their names (`FileJobs.PartialSuffix`): listed under the name they will take,
 * with a mark and their progress (`WritingProgress`), never opened, left out of scans.
 */
export const PARTIAL_SUFFIX = '.sfe-partial';

/** The mark of a partial item, and its hint. */
export const BEING_WRITTEN = $localize`:mark of a file still being copied or extracted:being written`;
export const BEING_WRITTEN_TITLE = $localize`Still being written by a copy, a compression or an extraction: \
you can open it once it's done. Left by one that stopped (no progress shown), it can be deleted.`;

/** What a partial item's menu keeps: nothing that opens it, changes it or copies it. */
const PARTIAL_ENTRIES: ReadonlySet<string> = new Set(['delete', 'copyPath', 'properties']);

/** Still being written (`PARTIAL_SUFFIX`, any case as the server compares it). */
export function isPartial(item: { name: string }): boolean {
    return item.name.toLowerCase().endsWith(PARTIAL_SUFFIX);
}

/** The name shown: an item being written without its ending (`clip.mp4.sfe-partial` → `clip.mp4`). */
export function shownName(item: { name: string }): string {
    return isPartial(item) ? item.name.slice(0, -PARTIAL_SUFFIX.length) : item.name;
}

/** The entries of `item`'s menu that apply: all, or only `PARTIAL_ENTRIES` while it is being written. */
export function partialEntries<T extends { id: string }>(item: { name: string }, entries: T[]): T[] {
    return isPartial(item) ? entries.filter((entry) => PARTIAL_ENTRIES.has(entry.id)) : entries;
}
