import { inject, Injectable } from '@angular/core';
import { LibraryItem } from '../core/models';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileActions } from './file-actions';
import { FileTransfers, pasteTitle } from './file-transfers';
import { ItemCommand, ItemCommandId, ItemMenuEntry, ItemScope, namesOf, refused } from './item-scope';

/**
 * The commands changing where listed items are or what they are called (Cut, Copy, Paste into, Rename…, Move to…,
 * Copy to…, Delete…), for `ItemCommands`. They act on the selection when the item is part of it; on those of it the
 * session may (the dialogs name them), greyed out when it may on none.
 */
@Injectable({ providedIn: 'root' })
export class TransferCommands {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly files = inject(FileActions);
    private readonly transfers = inject(FileTransfers);

    readonly commands: Partial<Record<ItemCommandId, ItemCommand>> = {
        cut: (scope) => this.cut(scope),
        copy: (scope) => this.copy(scope),
        paste: (scope) => this.paste(scope),
        rename: (scope) => this.rename(scope),
        moveTo: (scope) => this.moveTo(scope),
        copyTo: (scope) => this.copyTo(scope),
        delete: (scope) => this.delete(scope),
    };

    /** Several items that may be renamed, in a folder (not a search: names are compared with the folder's). */
    canRenameTogether(items: LibraryItem[]): boolean {
        return !this.store.searching() && items.filter((item) => this.files.canRename(item)).length > 1;
    }

    /** Opens the batch rename for those of `items` that may be renamed. False when fewer than two may. */
    renameTogether(items: LibraryItem[]): boolean {
        if (!this.canRenameTogether(items)) return false;
        this.dialogs.batchRename()?.open(items.filter((item) => this.files.canRename(item)));
        return true;
    }

    private movable(scope: ItemScope): boolean {
        return scope.targets.some((target) => this.transfers.canMove(target));
    }

    private copyable(scope: ItemScope): boolean {
        return scope.targets.some((target) => this.transfers.canCopy(target));
    }

    private cut(scope: ItemScope): ItemMenuEntry {
        const entry: ItemMenuEntry = {
            id: 'cut',
            label: $localize`Cut`,
            icon: 'cut',
            shortcut: 'Ctrl+X',
            title:
                scope.targets.length > 1
                    ? $localize`Keep the selected items, to move them into another folder with Paste (Ctrl+X).`
                    : $localize`Keep it, to move it into another folder with Paste (Ctrl+X).`,
            run: () => this.transfers.keep('move', scope.targets),
        };
        return refused(entry, this.movable(scope), 'move', namesOf(scope));
    }

    private copy(scope: ItemScope): ItemMenuEntry {
        const entry: ItemMenuEntry = {
            id: 'copy',
            label: $localize`Copy`,
            icon: 'copy',
            shortcut: 'Ctrl+C',
            title:
                scope.targets.length > 1
                    ? $localize`Keep the selected items, to copy them into another folder with Paste (Ctrl+C).`
                    : $localize`Keep it, to copy it into another folder with Paste (Ctrl+C).`,
            run: () => this.transfers.keep('copy', scope.targets),
        };
        return refused(entry, this.copyable(scope), 'copy', namesOf(scope));
    }

    /** Into a listed folder, while something is kept for a paste. */
    private paste({ item }: ItemScope): ItemMenuEntry | null {
        const clipboard = this.transfers.clipboard();
        if (!clipboard || item.kind !== 'directory') return null;
        const entry: ItemMenuEntry = {
            id: 'paste',
            label: $localize`Paste into “${item.name}:name:”`,
            icon: 'paste',
            title: pasteTitle(clipboard, item.name),
            run: () => this.transfers.paste(item.path),
        };
        return refused(entry, this.transfers.canInto(clipboard.kind, item), 'paste', [item.name]);
    }

    /** One item: its name; several: the batch rename, only in a folder (not in search results). */
    private rename(scope: ItemScope): ItemMenuEntry | null {
        const several = scope.targets.length > 1;
        if (several && this.store.searching()) return null;
        const entry: ItemMenuEntry = {
            id: 'rename',
            label: $localize`Rename…`,
            icon: 'edit',
            shortcut: 'F2',
            title: several
                ? $localize`Rename the selected items together: find and replace, or numbered names (F2).`
                : $localize`Give “${scope.item.name}:name:” another name (F2).`,
            run: () => (several ? this.renameTogether(scope.targets) : this.dialogs.name()?.rename(scope.item)),
        };
        const allowed = several ? this.canRenameTogether(scope.targets) : this.files.canRename(scope.item);
        return refused(entry, allowed, 'rename', namesOf(scope));
    }

    private moveTo(scope: ItemScope): ItemMenuEntry {
        const entry: ItemMenuEntry = {
            id: 'moveTo',
            label: $localize`Move to…`,
            icon: 'move',
            title:
                scope.targets.length > 1
                    ? $localize`Move the selected items to another folder, picked next (or drag them onto a folder).`
                    : $localize`Move “${scope.item.name}:name:” to another folder, picked next.`,
            run: () => this.transfers.pick('move', scope.targets),
        };
        return refused(entry, this.movable(scope), 'move', namesOf(scope));
    }

    private copyTo(scope: ItemScope): ItemMenuEntry {
        const entry: ItemMenuEntry = {
            id: 'copyTo',
            label: $localize`Copy to…`,
            icon: 'copy',
            title:
                scope.targets.length > 1
                    ? $localize`Copy the selected items into another folder, picked next \
(or drag them onto a folder with Ctrl).`
                    : $localize`Copy “${scope.item.name}:name:” into another folder, picked next.`,
            run: () => this.transfers.pick('copy', scope.targets),
        };
        return refused(entry, this.copyable(scope), 'copy', namesOf(scope));
    }

    private delete(scope: ItemScope): ItemMenuEntry {
        const permanent = this.files.alwaysPermanent;
        const several = scope.targets.length > 1;
        const name = scope.item.name;
        const entry: ItemMenuEntry = {
            id: 'delete',
            label: permanent ? $localize`Delete permanently…` : $localize`Delete…`,
            icon: 'trash',
            shortcut: 'Del',
            title: permanent
                ? several
                    ? $localize`Delete the selected items for good (Delete).`
                    : $localize`Delete “${name}:name:” for good (Delete).`
                : several
                  ? $localize`Move the selected items to the trash (Delete; Shift+Delete deletes them for good).`
                  : $localize`Move “${name}:name:” to the trash (Delete; Shift+Delete deletes it for good).`,
            run: () => this.files.delete(scope.targets),
        };
        const allowed = scope.targets.some((target) => this.files.canDelete(target));
        return refused(entry, allowed, 'delete', namesOf(scope));
    }
}
