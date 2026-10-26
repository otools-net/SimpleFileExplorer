import { ChangeDetectionStrategy, Component, computed, ElementRef, inject } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { joinParts, plural } from '../core/format';
import { FileDownloads } from '../files/downloads';
import { FileActions } from '../files/file-actions';
import { FileTransfers } from '../files/file-transfers';
import { ItemMenu } from '../files/item-menu';
import { LibraryItem } from '../core/models';
import { MenuEntry } from '../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../menus/overflow-row';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from './dialogs.service';
import { QuickLinksService } from './quick-links/quick-links.service';
import { ListingStore } from './listing.store';

/**
 * The bar at the bottom while files or folders are selected: their count, select all, adding them to the quick links,
 * renaming the one selected, moving, copying, downloading and deleting them, the tags of the files selected.
 * One line: on a narrow screen the count shortens ("15 selected"), then the buttons leave from the right (Download
 * first, Quick links last) into its "⋯" menu; Clear, the count, Select all and Tags stay. The host is a full-width
 * strip at the bottom, the bar centred in it: the room the bar has is measured there (`overflowIn`).
 */
@Component({
    selector: 'sfe-selection-bar',
    imports: [IconComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'selection-dock', role: 'region', 'aria-label': 'Selected items', '[hidden]': '!count()' },
    template: `
        <div class="selection-bar" sfeOverflowRow="Selected items" [overflowIn]="host">
        <span class="visually-hidden" aria-live="polite">{{ countText() }}</span>
        <button type="button" class="icon-button ghost small" [sfeOverflowItem]="null" overflowStay aria-label="Clear the selection" title="Clear the selection (Esc)" (click)="store.clearSelection()">
            <svg sfeIcon="close"></svg>
        </button>
        <!-- The long wording leaves first, alone; the short one shows in its place (selection.css). -->
        <span class="selection-count" aria-hidden="true" [sfeOverflowItem]="null" [overflowPriority]="-1">{{ countText() }}</span>
        <span class="selection-count selection-count-short" aria-hidden="true" [sfeOverflowItem]="null" overflowStay>{{ shortCountText() }}</span>
        <button type="button" class="button small" [sfeOverflowItem]="null" overflowStay [disabled]="count() >= loaded()" [title]="allTitle()" (click)="store.selectAll()">Select all</button>
        @if (quickLinks.enabled()) {
            <button type="button" class="button small selection-pin" [sfeOverflowItem]="more.quickLinks" title="Add the selected items to the quick links (or drag them onto the sidebar)" (click)="pinSelection()">
                <svg sfeIcon="pin"></svg>
                <span>Quick links</span>
            </button>
        }
        @if (renamable(); as item) {
            <button type="button" class="button small selection-rename" [sfeOverflowItem]="more.rename" [title]="'Give ' + item.name + ' another name (F2)'" (click)="rename(item)">
                <svg sfeIcon="edit"></svg>
                <span>Rename</span>
            </button>
        } @else if (renamableTogether()) {
            <button type="button" class="button small selection-rename" [sfeOverflowItem]="more.renameTogether" title="Rename the selected items together: find and replace, or numbered names (F2)" (click)="menu.renameSelection()">
                <svg sfeIcon="edit"></svg>
                <span>Rename</span>
            </button>
        }
        @if (deletable()) {
            <button type="button" class="button small selection-delete" [sfeOverflowItem]="more.delete" [title]="deleteTitle()" (click)="deleteSelection()">
                <svg sfeIcon="trash"></svg>
                <span>Delete</span>
            </button>
        }
        @if (movable()) {
            <button type="button" class="button small selection-move" [sfeOverflowItem]="more.move" title="Move the selected items to another folder (Ctrl+X, then Ctrl+V there; or drag them onto a folder)" (click)="transfers.pick('move', store.selectedItems())">
                <svg sfeIcon="move"></svg>
                <span>Move</span>
            </button>
        }
        @if (copyable()) {
            <button type="button" class="button small selection-copy" [sfeOverflowItem]="more.copy" title="Copy the selected items into another folder (Ctrl+C, then Ctrl+V there; or drag them onto a folder with Ctrl)" (click)="transfers.pick('copy', store.selectedItems())">
                <svg sfeIcon="copy"></svg>
                <span>Copy</span>
            </button>
        }
        @if (extractable(); as extract) {
            <button type="button" class="button small selection-extract" [sfeOverflowItem]="more.extract" [title]="extract.title" (click)="extractSelection()">
                <svg sfeIcon="extract"></svg>
                <span>{{ extract.label }}</span>
            </button>
        }
        @if (compressible()) {
            <button type="button" class="button small selection-compress" [sfeOverflowItem]="more.compress" title="Put the selected items in a zip in this folder" (click)="compressSelection()">
                <svg sfeIcon="compress"></svg>
                <span>Compress</span>
            </button>
        }
        @if (downloadable()) {
            <button type="button" class="button small selection-download" [sfeOverflowItem]="more.download" [title]="downloadTitle()" (click)="downloadSelection()">
                <svg sfeIcon="download"></svg>
                <span>Download</span>
            </button>
        }
        @if (store.can('tags.assign') && files()) {
            <button type="button" class="button small primary" [sfeOverflowItem]="null" overflowStay [title]="tagsTitle()" (click)="tagSelection()">
                <svg sfeIcon="tag"></svg>
                <span>Tags</span>
            </button>
        }
        <button class="icon-button small selection-more" sfeOverflowMore></button>
        </div>
    `,
})
export class SelectionBarComponent {
    protected readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    protected readonly config = inject(ConfigService);
    protected readonly quickLinks = inject(QuickLinksService);

    protected readonly menu = inject(ItemMenu);
    private readonly fileActions = inject(FileActions);
    protected readonly transfers = inject(FileTransfers);
    private readonly downloads = inject(FileDownloads);

    protected readonly count = computed(() => this.store.selection().size);
    private readonly selected = computed(() => this.store.selectedItems());
    protected readonly files = computed(() => this.selected().filter(item => item.kind !== 'directory').length);
    private readonly folders = computed(() => this.selected().length - this.files());
    protected readonly loaded = computed(() => this.store.items().length);
    protected readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    /** The count when the bar is narrow: "15 selected". */
    protected readonly shortCountText = computed(() => `${this.count()} selected`);
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

    /** Several selected that may be renamed (in a folder, not a search: names are compared with the folder's). */
    protected readonly renamableTogether = computed(() =>
        !this.store.searching() && this.selected().filter(item => this.menu.canRename(item)).length > 1);

    protected readonly movable = computed(() => this.selected().some(item => this.transfers.canMove(item)));
    protected readonly copyable = computed(() => this.selected().some(item => this.transfers.canCopy(item)));
    /** Some of the selection may go in a zip (only those do), and the dialog is there. */
    protected readonly compressible = computed(() => Boolean(this.dialogs.compress()) && this.selected().some(item => this.transfers.canCompress(item)));

    /** Extract: one archive selected, its dialog; several (all archives), each beside it; null otherwise. */
    protected readonly extractable = computed(() => {
        const selected = this.selected();
        if (selected.length === 1 && this.transfers.canExtract(selected[0]) && this.dialogs.extract())
            return { label: 'Extract…', title: `Extract ${selected[0].name} into a folder picked next, with its password if it has one` };
        if (this.transfers.canExtractEach(selected))
            return { label: 'Extract', title: `Extract the ${selected.length} archives here, each into a new folder named after it` };
        return null;
    });

    /** Some of the selection may be deleted (only those are, the confirmation names them). */
    protected readonly deletable = computed(() => this.selected().some(item => this.fileActions.canDelete(item)));
    protected readonly deleteTitle = computed(() => this.fileActions.alwaysPermanent
        ? 'Delete the selected items for good (Delete)'
        : 'Move the selected items to the trash (Delete; Shift+Delete: for good)');

    /** Some of the selection may be downloaded (only those are). */
    protected readonly downloadable = computed(() => this.selected().some(item => this.downloads.canDownload(item)));
    protected readonly downloadTitle = computed(() => this.downloads.isZip(this.selected())
        ? 'Save the selected items to this device, in one zip file (folders with everything in them)'
        : 'Save the selected file to this device');

    /** What the buttons that may leave the bar are in its "⋯" menu: the same actions, titles and keys. */
    protected readonly more = {
        quickLinks: (): MenuEntry => ({ id: 'quickLinks', label: 'Add to quick links', icon: 'pin', title: 'Add the selected items to the quick links (or drag them onto the sidebar)', run: () => this.pinSelection() }),
        rename: (): MenuEntry => ({ id: 'rename', label: 'Rename…', icon: 'edit', shortcut: 'F2', title: `Give ${this.renamable()?.name} another name (F2)`, run: () => this.renameOne() }),
        renameTogether: (): MenuEntry => ({ id: 'rename', label: 'Rename…', icon: 'edit', shortcut: 'F2', title: 'Rename the selected items together: find and replace, or numbered names (F2)', run: () => this.menu.renameSelection() }),
        delete: (): MenuEntry => ({ id: 'delete', label: this.fileActions.alwaysPermanent ? 'Delete permanently…' : 'Delete…', icon: 'trash', shortcut: 'Del', title: this.deleteTitle(), run: () => this.deleteSelection() }),
        move: (): MenuEntry => ({ id: 'move', label: 'Move to…', icon: 'move', title: 'Move the selected items to another folder (Ctrl+X, then Ctrl+V there; or drag them onto a folder)', run: () => this.transfers.pick('move', this.store.selectedItems()) }),
        copy: (): MenuEntry => ({ id: 'copy', label: 'Copy to…', icon: 'copy', title: 'Copy the selected items into another folder (Ctrl+C, then Ctrl+V there; or drag them onto a folder with Ctrl)', run: () => this.transfers.pick('copy', this.store.selectedItems()) }),
        download: (): MenuEntry => ({ id: 'download', label: 'Download', icon: 'download', title: this.downloadTitle(), run: () => this.downloadSelection() }),
        extract: (): MenuEntry => ({ id: 'extract', label: this.extractable()?.label ?? 'Extract', icon: 'extract', title: this.extractable()?.title ?? '', run: () => this.extractSelection() }),
        compress: (): MenuEntry => ({ id: 'compress', label: 'Compress…', icon: 'compress', title: 'Put the selected items in a zip in this folder', run: () => this.compressSelection() }),
    };

    protected extractSelection(): void {
        const selected = this.selected();
        if (selected.length === 1)
            this.dialogs.extract()?.open({ path: selected[0].path, name: selected[0].name });
        else if (this.transfers.canExtractEach(selected)) {
            void this.transfers.extractEach(selected);
            this.store.clearSelection();
        }
    }

    protected compressSelection(): void {
        this.dialogs.compress()?.open(this.store.selectedItems().filter(item => this.transfers.canCompress(item)));
    }

    private renameOne(): void {
        const item = this.renamable();
        if (item)
            this.rename(item);
    }

    protected downloadSelection(): void {
        void this.downloads.download(this.store.selectedItems());
    }

    protected deleteSelection(): void {
        this.fileActions.delete(this.store.selectedItems());
    }

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
