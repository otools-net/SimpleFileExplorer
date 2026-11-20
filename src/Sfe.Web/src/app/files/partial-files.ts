/**
 * Files and folders the server is still writing (a transfer from a connection, a compression, an extraction; copies
 * inside the library write the name itself) carry this ending until
 * they take their names (`FileJobs.PartialSuffix`): listed with a mark, never opened, left out of scans.
 */
export const PARTIAL_SUFFIX = '.sfe-partial';

/** The mark of a partial item, and its hint. */
export const BEING_WRITTEN = 'being written';
export const BEING_WRITTEN_TITLE =
    'Still being written by a copy from a connection, a compression or an extraction: it takes its name when done. ' +
    'Left by one that stopped, it can be deleted.';

/** What a partial item's menu keeps: nothing that opens it, changes it or copies it. */
const PARTIAL_ENTRIES: ReadonlySet<string> = new Set(['delete', 'copyPath', 'properties']);

/** Still being written (`PARTIAL_SUFFIX`, any case as the server compares it). */
export function isPartial(item: { name: string }): boolean {
    return item.name.toLowerCase().endsWith(PARTIAL_SUFFIX);
}

/** The entries of `item`'s menu that apply: all, or only `PARTIAL_ENTRIES` while it is being written. */
export function partialEntries<T extends { id: string }>(item: { name: string }, entries: T[]): T[] {
    return isPartial(item) ? entries.filter((entry) => PARTIAL_ENTRIES.has(entry.id)) : entries;
}
