import { inject, Injectable } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';

/**
 * What the session may do to listed items through file management, and the start of each action, shared by the item
 * menu, the selection bar and the keys. The server checks again: this only hides what would be refused.
 */
@Injectable({ providedIn: 'root' })
export class FileActions {
    private readonly store = inject(ListingStore);
    private readonly config = inject(ConfigService);
    private readonly dialogs = inject(Dialogs);

    /** The space deletes everything for good (`Files:DeletePermanently`). */
    get alwaysPermanent(): boolean {
        return Boolean(this.config.config()?.files?.deletePermanently);
    }

    /** May use `right` on `item` and in the folder listed (whose list of names changes). */
    private may(right: string, item: LibraryItem): boolean {
        return this.store.can(right) && this.store.can(right, item);
    }

    canRename(item: LibraryItem): boolean {
        return this.may(Rights.renameFiles, item);
    }

    /** May delete `item`: to the trash, or for good when the space always does. */
    canDelete(item: LibraryItem): boolean {
        return this.may(this.alwaysPermanent ? Rights.deletePermanently : Rights.deleteFiles, item);
    }

    canDeletePermanently(item: LibraryItem): boolean {
        return this.may(Rights.deletePermanently, item);
    }

    /**
     * Asks to delete `items` (those the session may delete), for good with `permanent` when it may (else to the
     * trash). False when none may be deleted.
     */
    delete(items: LibraryItem[], permanent = false): boolean {
        const forGood = this.alwaysPermanent || (permanent && items.every(item => this.canDeletePermanently(item)));
        const allowed = items.filter(item => forGood ? this.canDeletePermanently(item) : this.canDelete(item));
        if (!allowed.length)
            return false;
        this.dialogs.delete()?.open(allowed, forGood);
        return true;
    }
}
