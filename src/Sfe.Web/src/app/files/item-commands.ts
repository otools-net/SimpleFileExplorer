import { inject, Injectable } from '@angular/core';
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
    { group: 'properties', ids: ['properties'] },
];

/**
 * The one registry of what can be done to listed items (agents/docs/features/context-menu.md): the item menu, the
 * selection bar and the folder menu take their entries from here. What doesn't apply (the kind of item, how many are
 * selected) is hidden; what the rights don't allow is greyed out with the reason in its hint.
 */
@Injectable({ providedIn: 'root' })
export class ItemCommands {
    private readonly commands: Partial<Record<ItemCommandId, ItemCommand>> = {
        ...inject(OpeningCommands).commands,
        ...inject(TransferCommands).commands,
        ...inject(ArchiveCommands).commands,
        ...inject(OrganisingCommands).commands,
    };

    /** The entry of command `id` for `scope`; null where it doesn't apply. */
    entry(id: ItemCommandId, scope: ItemScope): ItemMenuEntry | null {
        return this.commands[id]?.(scope) ?? null;
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
