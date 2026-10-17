import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { joinParts, plural } from '../core/format';
import { ItemMenu } from '../files/item-menu';
import { LibraryItem } from '../core/models';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from './dialogs.service';
import { QuickLinksService } from './quick-links/quick-links.service';
import { ListingStore } from './listing.store';

/**
 * The bar at the bottom while files or folders are selected: their count, select all, adding them to the quick links,
 * renaming the one selected, the tags of the files selected.
 */
@Component({
    selector: 'sfe-selection-bar',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'selection-bar', role: 'region', 'aria-label': 'Selected items', '[hidden]': '!count()' },
    template: `
        <button type="button" class="icon-button ghost small" aria-label="Clear the selection" title="Clear the selection (Esc)" (click)="store.clearSelection()">
            <svg sfeIcon="close"></svg>
        </button>
        <span class="selection-count" aria-live="polite">{{ countText() }}</span>
        <button type="button" class="button small" [disabled]="count() >= loaded()" [title]="allTitle()" (click)="store.selectAll()">Select all</button>
        @if (quickLinks.enabled()) {
            <button type="button" class="button small selection-pin" title="Add the selected items to the quick links (or drag them onto the sidebar)" (click)="pinSelection()">
                <svg sfeIcon="pin"></svg>
                <span>Quick links</span>
            </button>
        }
        @if (renamable(); as item) {
            <button type="button" class="button small selection-rename" [title]="'Give ' + item.name + ' another name (F2)'" (click)="rename(item)">
                <svg sfeIcon="edit"></svg>
                <span>Rename</span>
            </button>
        }
        @if (store.can('tags.assign') && files()) {
            <button type="button" class="button small primary" [title]="tagsTitle()" (click)="tagSelection()">
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

    private readonly menu = inject(ItemMenu);

    protected readonly count = computed(() => this.store.selection().size);
    private readonly selected = computed(() => this.store.selectedItems());
    protected readonly files = computed(() => this.selected().filter(item => item.kind !== 'directory').length);
    private readonly folders = computed(() => this.selected().length - this.files());
    protected readonly loaded = computed(() => this.store.items().length);
    protected readonly countText = computed(() => {
        const parts = joinParts([this.folders() ? plural(this.folders(), 'folder') : null, this.files() ? plural(this.files(), 'file') : null], ' and ');
        return `${parts || plural(this.count(), 'item')} selected`;
    });
    protected readonly allTitle = computed(() => this.store.hasMore()
        ? `Select the ${plural(this.loaded(), 'item')} loaded so far (Ctrl+A). Scroll down to load more.`
        : 'Select every file and folder listed (Ctrl+A)');
    protected readonly tagsTitle = computed(() => this.folders()
        ? 'Add or remove tags on the selected files (the folders selected are left out)'
        : 'Add or remove tags on the selected files');
    /** The only item selected, when it may be renamed. */
    protected readonly renamable = computed(() => {
        const selected = this.selected();
        return selected.length === 1 && this.menu.canRename(selected[0]) ? selected[0] : null;
    });

    protected pinSelection(): void {
        void this.quickLinks.add(this.store.selectedItems().map(item => item.path));
    }

    protected rename(item: LibraryItem): void {
        this.dialogs.name()?.rename(item);
    }

    protected tagSelection(): void {
        const items = this.store.selectedFiles();
        if (items.length === 1)
            this.dialogs.fileTags()?.open(items[0]);
        else if (items.length)
            this.dialogs.filesTags()?.open(items);
    }
}
