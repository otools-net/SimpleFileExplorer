import { inject, Injectable } from '@angular/core';
import { isViewable } from '../../formats/formats';
import { LibraryItem, TagSummary } from '../../core/models';
import { tagQuery } from '../../tags/tags';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';

/** What clicking the parts of a listed file does. */
@Injectable({ providedIn: 'root' })
export class ItemActions {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);

    /**
     * While items are selected, a plain click (or shift-click) on a file or folder adds it to the selection (or takes it
     * out) instead of opening it. True when it did.
     */
    selectInstead(event: MouseEvent, item: LibraryItem): boolean {
        if (!this.store.selection().size || event.ctrlKey || event.metaKey || event.altKey || event.button !== 0)
            return false;
        event.preventDefault();
        this.store.select(item, !this.store.isSelected(item.path), event.shiftKey);
        return true;
    }

    /**
     * A click on a file's link. While items are selected it selects (`selectInstead`); a plain click on a file with a viewer opens
     * the viewer; other clicks open the link (new tab...).
     */
    openFile(event: MouseEvent, item: LibraryItem): void {
        if (this.selectInstead(event, item))
            return;
        if (isViewable(item) && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
            const viewer = this.dialogs.viewer();
            if (viewer) {
                event.preventDefault();
                viewer.open(item);
                return;     // the viewer reports the view itself
            }
        }
        this.store.reportView(item);
    }

    openTags(item: LibraryItem): void {
        this.dialogs.fileTags()?.open(item);
    }

    searchTag(tag: TagSummary): void {
        this.store.navigate({ q: tagQuery(tag) });
    }
}
