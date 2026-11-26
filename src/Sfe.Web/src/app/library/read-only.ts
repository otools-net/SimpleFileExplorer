import { LibraryItem, ReadOnlyReason } from '../core/models';
import { ListingPage } from './library.models';

/** The location bar's chip for a folder the session can't change (agents/docs/features/item-access.md). */
export const READ_ONLY_LABEL = $localize`:a folder the user can't change:Read only`;

const REASONS: Record<ReadOnlyReason, string> = {
    server: $localize`Read only: this server doesn't let anyone change files.`,
    space: $localize`Read only: this space is set so that nobody changes its files.`,
    visitor: $localize`Read only: visitors and guest accounts can open files here, not change them.`,
    rights: $localize`Read only: your rights in this space let you open files, not change them.`,
    rule: $localize`Read only: a folder rule lets you open the files here, not change them.`,
    link: $localize`Read only: this folder is behind a link leaving the library, which the server keeps read-only.`,
    diskReadOnly: $localize`Read only: the disk refuses changes here, it is mounted read-only.`,
    diskRefused: $localize`Read only: the server isn't allowed to write in this folder on the disk.`,
    fileReadOnly: $localize`Read only: the disk marks this file read-only.`,
};

/** Why the session can't change the files of the folder listed; null when it can, and for searches. */
export function readOnlyReason(page: ListingPage | null): ReadOnlyReason | null {
    return page && 'directoryCount' in page ? (page.readOnly ?? null) : null;
}

/** The hint of the chip: why, in one sentence. */
export function readOnlyText(reason: ReadOnlyReason): string {
    return REASONS[reason] ?? REASONS.rights;
}

/** What changes the folder listed on the disk: greyed in the menus when the disk refuses it. */
const PLACE_REASONS: readonly ReadOnlyReason[] = ['link', 'diskReadOnly', 'diskRefused'];

/**
 * Why the folder listed takes no change although the rights allow it: a link the server keeps read-only, or the disk.
 * Null when it takes them, when the rights already refuse (the menus say so), and for searches.
 */
export function placeRefusal(page: ListingPage | null): ReadOnlyReason | null {
    const reason = readOnlyReason(page);
    return reason && PLACE_REASONS.includes(reason) ? reason : null;
}

/**
 * Why `item` of the folder listed can't be changed when the folder can: the disk marks the file read-only, or a folder
 * rule lets the session only read it. Null when it is like its folder: what differs only is marked, the usual case
 * stays quiet.
 */
export function itemLock(page: ListingPage | null, item: LibraryItem): string | null {
    if (!page || !('directoryCount' in page) || page.readOnly) return null;
    const children = page.access?.children ?? {};
    const own =
        children[item.name] ??
        Object.entries(children).find(([name]) => name.toLowerCase() === item.name.toLowerCase())?.[1];
    if (own?.readOnly && !own.pathOnly) return $localize`A folder rule lets you open this, not change it.`;
    if (item.readOnly) return $localize`Marked read-only on the disk: it can't be saved over.`;
    return null;
}
