import { inject, Injectable } from '@angular/core';
import { MEDIA_KINDS } from '../../core/format';
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
     * A click on a file's link. While files are selected, a plain click (or shift-click) adds to the selection
     * instead of opening; a plain click on media opens the viewer; other clicks open the link (new tab...).
     */
    openFile(event: MouseEvent, item: LibraryItem): void {
        if (this.store.selection().size && !event.ctrlKey && !event.metaKey && !event.altKey && event.button === 0) {
            event.preventDefault();
            this.store.select(item, !this.store.isSelected(item.path), event.shiftKey);
            return;
        }
        if (MEDIA_KINDS.has(item.kind) && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
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
