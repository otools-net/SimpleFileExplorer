import { ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CopyPathService } from '../../core/copy-path.service';
import { icu } from '../../core/i18n/icu';
import { LibraryItem } from '../../core/models';
import { Rights } from '../../core/rights';
import { FileTransfers } from '../../files/file-transfers';
import { ItemMenu, ItemMenuButtonComponent } from '../../files/item-menu';
import { useContextMenu } from '../../menus/context-menu';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { FolderCoverComponent } from './folder-cover.component';
import { ItemActions } from './item-actions.service';
import { DefaultTagsBadgeComponent } from '../../tags/default-tags-badge.component';
import { exclusionChip, exclusionTitle } from './exclusions';
import { LockMarkComponent, NOT_INDEXED, PartialMarkComponent, SelectBoxComponent } from './item-parts';
import { BEING_WRITTEN_TITLE, isPartial, shownName } from '../../files/partial-files';

/**
 * A listed folder: a card with its first pictures (`cards`), or a line with its name. Buttons can't sit inside
 * the link: the tags and ⋮ buttons go next to it, in the box this component is (the rest is in the item menu).
 */
@Component({
    selector: 'sfe-folder-item',
    imports: [
        IconComponent,
        RouterLink,
        FolderCoverComponent,
        ItemMenuButtonComponent,
        SelectBoxComponent,
        DefaultTagsBadgeComponent,
        PartialMarkComponent,
        LockMarkComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'folder-item',
        '[class.copied]': 'copied()',
        '[class.cut]': 'transfers.cutPaths().has(item().path)',
        '[class.unindexed]': 'unindexed()',
        '[class.partial]': 'partial()',
        '[class.revealed]': 'store.revealed() === item().path',
        '[class.selected]': 'store.selection().has(item().path)',
        '[attr.data-path]': 'item().path',
        // Items dropped on it are moved (copied with Ctrl) into it: OperationsPanelComponent.
        '[attr.data-drop]': 'item().path',
    },
    templateUrl: './folder-item.component.html',
})
export class FolderItemComponent {
    readonly item = input.required<LibraryItem>();
    /** Folders are cards with pictures (`config.folderPictures`), except in the list view. */
    readonly cards = input(false);
    /** Tagging a whole folder changes tags: hidden without the right (there, with folder rules). */
    protected readonly canTag = computed(
        () => this.store.can(Rights.assignTags, this.item()) || this.store.can(Rights.folderDefaultTags, this.item()),
    );
    protected readonly store = inject(ListingStore);
    private readonly copyPaths = inject(CopyPathService);
    private readonly menu = inject(ItemMenu);
    protected readonly transfers = inject(FileTransfers);
    protected readonly copied = computed(() => this.copyPaths.copied().has(this.item().path));
    protected readonly unindexed = computed(() => this.item().indexed === false);
    /** Still being written (an extraction): marked, not opened. */
    protected readonly partial = computed(() => isPartial(this.item()));
    /** Its name, or the one it takes once written. */
    protected readonly name = computed(() => shownName(this.item()));
    /** Kept out of scans, trickplay or thumbnails (for everyone: it tells why). */
    protected readonly exclusion = computed(() => exclusionChip(this.item().excluded));
    protected readonly exclusionTitle = computed(() => exclusionTitle(this.item().excluded, this.item().path));
    /** The card's count lets clicks through to the link: its hint is the link's. */
    protected readonly linkTitle = computed(() =>
        [
            this.name(),
            this.unindexed() ? NOT_INDEXED : null,
            this.partial() ? BEING_WRITTEN_TITLE : null,
            this.cards() && this.indexedItems() !== null ? this.countTitle() : null,
            this.exclusionTitle(),
        ]
            .filter(Boolean)
            .join('\n'),
    );
    /** Indexed files and sub-folders directly inside; null when unknown (not indexed, older server). */
    protected readonly indexedItems = computed(() => this.item().indexedItems ?? null);
    protected readonly countTitle = computed(() =>
        icu(
            $localize`{count, plural, \
one {# indexed item directly in this folder (files and sub-folders, as of the last scan).} \
other {# indexed items directly in this folder (files and sub-folders, as of the last scan).}}`,
            { count: this.indexedItems() ?? 0 },
        ),
    );
    protected readonly tagsLabel = computed(() => $localize`Tags of the files in “${this.item().name}:name:”`);
    private readonly dialogs = inject(Dialogs);

    constructor() {
        useContextMenu(() => this.menu.target(this.item()));
        // While items are selected, a click on the folder selects it: caught before the link's own handler navigates.
        // One still being written isn't opened.
        const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
        const actions = inject(ItemActions);
        const capture = (event: MouseEvent) => {
            if (!(event.target as Element).closest('a.folder')) return;
            if (actions.selectInstead(event, this.item())) event.stopPropagation();
            else if (this.partial()) {
                event.preventDefault();
                event.stopPropagation();
            }
        };
        host.addEventListener('click', capture, { capture: true });
        inject(DestroyRef).onDestroy(() => host.removeEventListener('click', capture, { capture: true }));
    }

    /** The tags of the files of this folder (the dialog offers its sub-folders too). */
    protected openTags(): void {
        this.dialogs.scopeTags()?.open({ path: this.item().path, recursive: false, query: null });
    }
}
