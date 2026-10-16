import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from './dialogs.service';
import { QuickLinksService } from './quick-links/quick-links.service';
import { ListingStore } from './listing.store';

/** The bar at the bottom while files are selected: their count, select all, their tags, adding them to the quick links. */
@Component({
    selector: 'sfe-selection-bar',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'selection-bar', role: 'region', 'aria-label': 'Selected files', '[hidden]': '!count()' },
    template: `
        <button type="button" class="icon-button ghost small" aria-label="Clear the selection" title="Clear the selection (Esc)" (click)="store.clearSelection()">
            <svg sfeIcon="close"></svg>
        </button>
        <span class="selection-count" aria-live="polite">{{ countText() }}</span>
        <button type="button" class="button small" [disabled]="count() >= loaded()" [title]="allTitle()" (click)="store.selectAll()">Select all</button>
        @if (quickLinks.enabled()) {
            <button type="button" class="button small selection-pin" title="Add the selected files to the quick links (or drag them onto the sidebar)" (click)="pinSelection()">
                <svg sfeIcon="pin"></svg>
                <span>Quick links</span>
            </button>
        }
        @if (store.can('tags.assign')) {
            <button type="button" class="button small primary" title="Add or remove tags on the selected files" (click)="tagSelection()">
                <svg sfeIcon="tag"></svg>
                <span>Tags</span>
            </button>
        }
    `,
})
export class SelectionBarComponent {
    protected readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    protected readonly config = inject(ConfigService);
    protected readonly quickLinks = inject(QuickLinksService);

    protected readonly count = computed(() => this.store.selection().size);
    protected readonly loaded = computed(() => this.store.files().length);
    protected readonly countText = computed(() => `${plural(this.count(), 'file')} selected`);
    protected readonly allTitle = computed(() => this.store.hasMore()
        ? `Select the ${plural(this.loaded(), 'file')} loaded so far (Ctrl+A). Scroll down to load more.`
        : 'Select every file listed (Ctrl+A)');

    protected pinSelection(): void {
        void this.quickLinks.add(this.store.selectedFiles().map(item => item.path));
    }

    protected tagSelection(): void {
        const items = this.store.selectedFiles();
        if (items.length === 1)
            this.dialogs.fileTags()?.open(items[0]);
        else if (items.length)
            this.dialogs.filesTags()?.open(items);
    }
}
