import { inject, Injectable } from '@angular/core';
import { ListingStore } from '../library/listing.store';
import { placeRefusal, readOnlyText } from '../library/read-only';
import { ArchiveCommands } from './archive-commands';
import { ItemCommand, ItemCommandId, ItemMenuEntry, ItemScope, ItemSubmenuId } from './item-scope';
import { OpeningCommands } from './opening-commands';
import { OrganisingCommands } from './organising-commands';
import { partialEntries } from './partial-files';
import { TransferCommands } from './transfer-commands';

/** Entries shown together under one entry ("Open as ▸"): flat when only one of them applies. */
interface Submenu {
    id: ItemSubmenuId;
    icon: string;
    label(): string;
    title(): string;
    ids: readonly ItemCommandId[];
}

const OPEN_AS: Submenu = {
    id: 'openAs',
    icon: 'file',
    label: () => $localize`Open as`,
    title: () => $localize`Open it in the viewer as something else.`,
    ids: ['openAsText', 'openAsArchive'],
};

const ARCHIVE: Submenu = {
    id: 'archive',
    icon: 'extract',
    label: () => $localize`Archive`,
    title: () => $localize`Put it in a zip file, or extract what an archive holds.`,
    ids: ['extractBeside', 'extractEach', 'extract', 'compress'],
};

/**
 * The commands writing in the folder listed (renaming, moving or deleting its items, an archive put beside them):
 * greyed out with the disk's reason when it takes no change there. Pasting into a sub-folder too, unless only the
 * folder itself is refused (the server's account may write in the sub-folder).
 */
const WRITES_HERE: ReadonlySet<ItemCommandId> = new Set<ItemCommandId>([
    'cut',
    'rename',
    'moveTo',
    'delete',
    'extractBeside',
    'extractEach',
    'compress',
    'paste',
]);

/**
 * The menu of listed items, group by group (a line between groups), in the order of a desktop file browser: opening,
 * editing, the clipboard, organising, archives, saving elsewhere, deleting, properties.
 */
const LAYOUT: readonly { group: string; ids: readonly (ItemCommandId | Submenu)[] }[] = [
    { group: 'open', ids: ['open', 'newTab', 'openFolder', OPEN_AS] },
    { group: 'edit', ids: ['editText', 'editPicture'] },
    { group: 'clipboard', ids: ['cut', 'copy', 'paste'] },
    { group: 'organise', ids: ['rename', 'moveTo', 'copyTo', 'tags', 'quickLink', 'exclusions', 'folderAccess'] },
    { group: 'archive', ids: [ARCHIVE] },
    { group: 'save', ids: ['download', 'copyPath'] },
    { group: 'delete', ids: ['delete'] },
    { group: 'properties', ids: ['versions', 'properties'] },
];

/**
 * The one registry of what can be done to listed items (agents/docs/features/context-menu.md): the item menu, the
 * selection bar and the folder menu take their entries from here. What doesn't apply (the kind of item, how many are
 * selected) is hidden; what the rights don't allow is greyed out with the reason in its hint.
 */
@Injectable({ providedIn: 'root' })
export class ItemCommands {
    private readonly store = inject(ListingStore);
    private readonly commands: Partial<Record<ItemCommandId, ItemCommand>> = {
        ...inject(OpeningCommands).commands,
        ...inject(TransferCommands).commands,
        ...inject(ArchiveCommands).commands,
        ...inject(OrganisingCommands).commands,
    };

    /** The entry of command `id` for `scope`; null where it doesn't apply. */
    entry(id: ItemCommandId, scope: ItemScope): ItemMenuEntry | null {
        const entry = this.commands[id]?.(scope) ?? null;
        return entry && !entry.disabled && WRITES_HERE.has(id) ? this.onDisk(id, entry) : entry;
    }

    /** The whole menu for `scope`, grouped; only what a partial item keeps while it is being written. */
    menu(scope: ItemScope): ItemMenuEntry[] {
        const entries: ItemMenuEntry[] = [];
        for (const { group, ids } of LAYOUT)
            for (const id of ids) {
                const entry = typeof id === 'string' ? this.entry(id, scope) : this.submenu(id, scope);
                if (entry) entries.push({ ...entry, group });
            }
        return partialEntries(scope.item, entries);
    }

    /** `entry` greyed out when the folder listed takes no change on the disk (item-access.md). */
    private onDisk(id: ItemCommandId, entry: ItemMenuEntry): ItemMenuEntry {
        const reason = placeRefusal(this.store.listing());
        if (!reason || (id === 'paste' && reason === 'diskRefused')) return entry;
        return { ...entry, disabled: true, title: readOnlyText(reason) };
    }

    private submenu(submenu: Submenu, scope: ItemScope): ItemMenuEntry | null {
        const entries = submenu.ids.flatMap((id) => this.entry(id, scope) ?? []);
        if (entries.length < 2) return entries[0] ?? null;
        return {
            id: submenu.id,
            label: submenu.label(),
            icon: submenu.icon,
            title: submenu.title(),
            submenu: () => entries,
            run: () => undefined,
        };
    }
}
