import { ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { plural } from '../../core/format';
import { CopyPathService } from '../../core/copy-path.service';
import { LibraryItem } from '../../core/models';
import { Rights } from '../../core/rights';
import { FileTransfers } from '../../files/file-transfers';
import { ItemMenu, ItemMenuButtonComponent } from '../../files/item-menu';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { PinButtonComponent } from '../quick-links/pin-button.component';
import { FolderCoverComponent } from './folder-cover.component';
import { ItemActions } from './item-actions.service';
import { CopyPathButtonComponent, NOT_INDEXED, SelectBoxComponent } from './item-parts';

/**
 * A listed folder: a card with its first pictures (`cards`), or a line with its name. Buttons can't sit inside
 * the link: the pin, tags and copy buttons go next to it, in the box this component is.
 */
@Component({
    selector: 'sfe-folder-item',
    imports: [IconComponent, RouterLink, FolderCoverComponent, CopyPathButtonComponent, PinButtonComponent, ItemMenuButtonComponent, SelectBoxComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'folder-item',
        '[class.copied]': 'copied()',
        '[class.cut]': 'transfers.cutPaths().has(item().path)',
        '[class.unindexed]': 'unindexed()',
        '[class.revealed]': 'store.revealed() === item().path',
        '[class.selected]': 'store.selection().has(item().path)',
        '[attr.data-path]': 'item().path',
        // Items dropped on it are moved (copied with Ctrl) into it: OperationsPanelComponent.
        '[attr.data-drop]': 'item().path',
        '(contextmenu)': 'menu.fromEvent($event, item())',
    },
    template: `
        <a class="folder item kind-directory" [class.card]="cards()" routerLink="/" [queryParams]="store.browseParams(item().path)" [title]="linkTitle()">
            @if (cards()) {
                <sfe-folder-cover [path]="item().path">
                    @if (indexedItems() !== null) {
                        <span class="badge folder-count" [title]="countTitle()">{{ indexedItems()!.toLocaleString() }}</span>
                    }
                </sfe-folder-cover>
                <span class="folder-caption"><svg sfeIcon="folder"></svg><span class="item-name">{{ item().name }}</span></span>
            } @else {
                <svg sfeIcon="folder"></svg><span class="item-name">{{ item().name }}</span>
                @if (indexedItems() !== null) {
                    <span class="folder-count" [title]="countTitle()">{{ indexedItems()!.toLocaleString() }}</span>
                }
            }
        </a>
        <label sfeSelectBox [item]="item()"></label>
        <span class="folder-actions">
            <button [sfePin]="item().path" [name]="item().name" class="small"></button>
            @if (canTag()) {
                <button type="button" class="icon-button small folder-tags-button" [attr.aria-label]="'Tags of the files in ' + item().name"
                        title="Add or remove tags on all the files of this folder, or of its sub-folders too (asks first)" (click)="openTags()">
                    <svg sfeIcon="tag"></svg>
                </button>
            }
            @if (copyPaths.enabled()) {
                <button [sfeCopyPath]="item().path" [name]="item().name"></button>
            }
            <button [sfeItemMenu]="item()"></button>
        </span>
    `,
})
export class FolderItemComponent {
    readonly item = input.required<LibraryItem>();
    /** Folders are cards with pictures (`config.folderPictures`), except in the list view. */
    readonly cards = input(false);
    /** Tagging a whole folder changes tags: hidden without the right (there, with folder rules). */
    protected readonly canTag = computed(() => this.store.can(Rights.assignTags, this.item()));
    protected readonly store = inject(ListingStore);
    protected readonly copyPaths = inject(CopyPathService);
    protected readonly menu = inject(ItemMenu);
    protected readonly transfers = inject(FileTransfers);
    protected readonly copied = computed(() => this.copyPaths.copied().has(this.item().path));
    protected readonly unindexed = computed(() => this.item().indexed === false);
    protected readonly linkTitle = computed(() => this.item().name + (this.unindexed() ? ` ${NOT_INDEXED}` : ''));
    /** Indexed files and sub-folders directly inside; null when unknown (not indexed, older server). */
    protected readonly indexedItems = computed(() => this.item().indexedItems ?? null);
    protected readonly countTitle = computed(() =>
        `${plural(this.indexedItems() ?? 0, 'indexed item')} directly in this folder (files and sub-folders, as of the last scan)`);
    private readonly dialogs = inject(Dialogs);

    constructor() {
        // While items are selected, a click on the folder selects it: caught before the link's own handler navigates.
        const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
        const actions = inject(ItemActions);
        const capture = (event: MouseEvent) => {
            if ((event.target as Element).closest('a.folder') && actions.selectInstead(event, this.item()))
                event.stopPropagation();
        };
        host.addEventListener('click', capture, { capture: true });
        inject(DestroyRef).onDestroy(() => host.removeEventListener('click', capture, { capture: true }));
    }

    /** The tags of the files of this folder (the dialog offers its sub-folders too). */
    protected openTags(): void {
        this.dialogs.scopeTags()?.open({ path: this.item().path, recursive: false, query: null });
    }
}
