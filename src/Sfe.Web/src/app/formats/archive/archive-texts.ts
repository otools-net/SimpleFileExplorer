import { ArchiveItem } from '../../core/archive.models';
import { formatSize } from '../../core/format';
import { icu } from '../../core/i18n/icu';
import { quoted } from '../../files/file-dialog-texts';
import { ArchiveListing } from '../format.models';

/** A search of the archive by name, as its texts need it: `folder` null for the whole archive. */
export interface FoundSoFar {
    query: string;
    folder: number | null;
    rows: readonly unknown[];
    next: string | null;
    searching: boolean;
}

/**
 * The archive viewer's texts as whole messages marked for translation: counts are ICU plurals, the place searched (the
 * archive or the folder shown) an ICU `select` (agents/docs/development/translations.md).
 */
export const archiveTexts = {
    topOf: (name: string) => $localize`Go to the top of ${quoted(name)}:archive:.`,
    skipped: (count: number) =>
        icu(
            $localize`{count, plural, one {# folder in between is not shown.} \
other {# folders in between are not shown.}}`,
            { count },
        ),
    crumbHint: (name: string, current: boolean) =>
        current ? $localize`This is the folder shown.` : $localize`Open the folder ${quoted(name)}:folder:.`,
    find: (here: boolean) => (here ? $localize`Find in this folder` : $localize`Find in archive`),
    findLabel: (here: boolean) =>
        here ? $localize`Find in this folder by name` : $localize`Find in the archive by name`,
    findHint: (here: boolean) =>
        here
            ? $localize`List the folders and files right inside this folder whose name holds what you type \
(Esc goes back to the folder).`
            : $localize`List the folders and files of the whole archive whose name holds what you type \
(Esc goes back to the folder).`,
    extractHint: (top: boolean) =>
        top
            ? $localize`Extract the whole archive into a folder of the library, picked next.`
            : $localize`Extract this folder of the archive into a folder of the library, picked next.`,
    extractOneHint: (name: string) =>
        $localize`Extract ${quoted(name)}:entry: into a folder of the library, picked next.`,
    reading: (read: number) =>
        icu(
            $localize`{read, plural, =0 {Reading the list of entries…} \
other {Reading the list of entries… {read, number} so far.}}`,
            { read },
        ),
    leftOut: (count: number) =>
        icu(
            $localize`{count, plural, \
one {# entry is not listed: its name leads out of the archive, or it is nested too deep.} \
other {# entries are not listed: their names lead out of the archive, or they are nested too deep.}}`,
            { count },
        ),
    foundNone: (query: string, here: boolean) =>
        here
            ? $localize`No name in this folder holds “${query}:query:”.`
            : $localize`No name in this archive holds “${query}:query:”.`,
    openFolder: (name: string) => $localize`Open the folder ${quoted(name)}:folder:.`,
    openFile: (name: string, inPlace: boolean) =>
        inPlace
            ? $localize`Show ${quoted(name)}:file: here, in place of the list.`
            : $localize`Open ${quoted(name)}:file: in a new tab.`,
    givePassword: (name: string) => $localize`Give the archive's password to open ${quoted(name)}:file:.`,
    whereHint: (where: string) => $localize`Open the folder it is in, ${quoted(where)}:folder:.`,
    previous: () => $localize`Previous file`,
    previousHint: () => $localize`Show the file before this one in the folder.`,
    next: () => $localize`Next file`,
    nextHint: () => $localize`Show the file after this one in the folder.`,
    newTab: () => $localize`Open in new tab`,
    newTabHint: () => $localize`Open this file in a new tab.`,
    download: () => $localize`Download`,
    downloadHint: () => $localize`Download this file from the archive.`,
    extract: () => $localize`Extract…`,
    open: () => $localize`Open`,
    openFolderHint: () => $localize`Show this folder of the archive.`,
    showHereHint: () => $localize`Show this file here, in place of the list.`,
    lockedList: () =>
        $localize`This archive's list is protected by a password: what it holds can't be read without it.`,
    lockedFiles: () => $localize`Some files of this archive are protected by a password.`,
    nothingLocked: () => $localize`Nothing in this archive is protected by a password.`,
    tooManyTries: () => $localize`Too many tries: wait a minute before the next one.`,
    wrongPassword: () => $localize`That password doesn't open this archive.`,
    viewerNotLoaded: () => $localize`Could not load the viewer of this file. Reload the page to try again.`,
    searchFailed: (reason: string) => $localize`Could not search the archive: ${reason}:reason:`,
    cannotExplore: () => $localize`This file can't be explored.`,
    noSuchFolder: () => $localize`There is no such folder in the archive.`,
};

/** A row's hint: why it can't be opened, or its name. */
export function entryHint(entry: ArchiveItem): string {
    if (entry.encrypted) return $localize`This file is protected by a password.`;
    if (entry.kind === 'link')
        return $localize`This is a link: it is listed, but never created when the archive is extracted.`;
    if (entry.kind === 'other')
        return $localize`This is a device or a pipe: it is listed, but never created when the archive is extracted.`;
    return entry.name;
}

/** The Size column: a file's size, or how many files a folder holds in all. */
export function entrySize(entry: ArchiveItem): string {
    if (entry.kind === 'folder')
        return icu($localize`{files, plural, one {# file} other {# files}}`, { files: entry.allFiles ?? 0 });
    return entry.kind === 'file' ? formatSize(entry.size) : '';
}

/** The Size column's hint: a folder's size in all, a file's in bytes. */
export function entrySizeHint(entry: ArchiveItem): string {
    if (entry.kind === 'folder')
        return $localize`:a folder's size, with everything below it:${formatSize(entry.size)}:size: in all`;
    return icu($localize`{bytes, plural, one {# byte} other {# bytes}}`, { bytes: entry.size });
}

/** The bottom line of a folder: "2 folders · 3 files here · 6 KB in 12 files below · ZIP". */
export function folderCounts(listing: ArchiveListing): string {
    const folder = listing.folder;
    return icu(
        $localize`{folders, plural, one {# folder} other {# folders}} · \
{files, plural, one {# file here} other {# files here}} · \
{all, plural, one {{size} in # file below} other {{size} in # files below}} · {format}`,
        {
            folders: folder.folders ?? 0,
            files: folder.files ?? 0,
            all: folder.allFiles ?? 0,
            size: formatSize(folder.size),
            format: listing.format.toUpperCase(),
        },
    );
}

/** The status line of a search: searching, what was found so far, or all of it. */
export function foundStatus(found: FoundSoFar): string {
    const values = { count: found.rows.length, query: found.query, here: found.folder === null ? 'no' : 'yes' };
    if (found.searching && found.rows.length === 0)
        return icu($localize`{here, select, yes {Searching this folder…} other {Searching the archive…}}`, values);
    if (found.searching)
        return icu(
            $localize`{here, select, \
yes {Searching this folder… {count, plural, one {# name holds “{query}”} other {# names hold “{query}”}} so far.} \
other {Searching the archive… {count, plural, one {# name holds “{query}”} other {# names hold “{query}”}} so far.}}`,
            values,
        );
    if (found.next)
        return icu(
            $localize`{here, select, \
yes {{count, plural, one {# name in this folder holds “{query}”} other {# names in this folder hold “{query}”}} \
so far. Scroll for more.} \
other {{count, plural, one {# name holds “{query}”} other {# names hold “{query}”}} so far. Scroll for more.}}`,
            values,
        );
    return icu(
        $localize`{here, select, \
yes {{count, plural, one {# name in this folder holds “{query}”.} other {# names in this folder hold “{query}”.}}} \
other {{count, plural, one {# name holds “{query}”.} other {# names hold “{query}”.}}}}`,
        values,
    );
}
