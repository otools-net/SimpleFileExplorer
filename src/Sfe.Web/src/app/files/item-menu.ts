import {
    ChangeDetectionStrategy,
    Component,
    computed,
    effect,
    ElementRef,
    inject,
    Injectable,
    input,
    untracked,
} from '@angular/core';
import { ToastService } from '../core/toast.service';
import { LibraryItem } from '../core/models';
import { ContextMenu, MenuTarget } from '../menus/context-menu';
import { IconComponent } from '../shared/icon.component';
import { ListingStore } from '../library/listing.store';
import { FileActions } from './file-actions';
import { quoted } from './file-dialog-texts';
import { ItemCommands } from './item-commands';
import { ItemMenuEntry } from './item-scope';
import { OrganisingCommands } from './organising-commands';
import { TransferCommands } from './transfer-commands';

export type { ItemMenuEntry } from './item-scope';

/**
 * The menu of a listed file or folder, as in an OS file browser (shown by `ContextMenu`): opened by a right click
 * (the browser's own menu with Shift) or the item's ⋮ button; a long press on phones selects the item instead. When
 * the item is part of the selection, its entries act on all of it.
 */
@Injectable({ providedIn: 'root' })
export class ItemMenu {
    private readonly store = inject(ListingStore);
    private readonly files = inject(FileActions);
    private readonly commands = inject(ItemCommands);
    private readonly organising = inject(OrganisingCommands);
    private readonly transferCommands = inject(TransferCommands);
    private readonly menu = inject(ContextMenu);
    private readonly toasts = inject(ToastService);

    constructor() {
        // Another listing: the item whose menu is open may not be there any more. The same listing refreshed (the
        // change feed) keeps the menu open, unless its item left the folder (deleted or moved elsewhere): closed with a
        // word, never silently gone.
        let generation = untracked(() => this.store.generation());
        let watched: string | null = null;
        effect(() => {
            const now = this.store.generation();
            const items = this.store.items();
            const item = this.menu.open()?.target.item;
            untracked(() => {
                if (now !== generation) {
                    generation = now;
                    watched = null;
                    if (item) this.menu.close();
                    return;
                }
                const listed = !!item && items.some((other) => other.path === item.path);
                if (!item || watched !== item.path) {
                    // A new menu: only an item of the listing can leave it.
                    watched = listed ? item!.path : null;
                    return;
                }
                if (!listed) {
                    watched = null;
                    this.menu.close();
                    this.toasts.show(
                        $localize`${quoted(item.name)}:name: is no longer in this folder. \
It was moved or deleted elsewhere.`,
                    );
                }
            });
        });
    }

    /** The menu of `item`, for `ContextMenu`. */
    target(item: LibraryItem): MenuTarget {
        return {
            kind: item.kind === 'directory' ? 'folder' : 'file',
            key: `item:${item.path}`,
            name: item.name,
            item,
            entries: () => (this.hasActions(item) ? this.entries(item) : []),
            touch: () => this.store.select(item, !this.store.isSelected(item.path)),
        };
    }

    /** Everyone who browses has the menu, for Properties at least (the browser's own menu with Shift). */
    hasActions(item: LibraryItem): boolean {
        return this.canSeeProperties() || this.entries(item).length > 0;
    }

    /** Properties of what is listed: whoever browses. */
    canSeeProperties(): boolean {
        return this.organising.canSeeProperties();
    }

    /** Several items selected that may be renamed: the batch rename opens for them. False when fewer than two. */
    renameSelection(): boolean {
        return this.transferCommands.renameTogether(this.store.selectedItems());
    }

    /** May rename `item`: in the folder listed (its list of names changes) and on the item itself. */
    canRename(item: LibraryItem): boolean {
        return this.files.canRename(item);
    }

    /** The entries of `item`'s menu; on the selection when `item` is part of it, unless `single` (the viewer). */
    entries(item: LibraryItem, single = false): ItemMenuEntry[] {
        return this.commands.menu({ item, targets: single ? [item] : this.targets(item) });
    }

    /** The selection when `item` is part of it (the menu acts on all of it, as in a file browser), else `item`. */
    private targets(item: LibraryItem): LibraryItem[] {
        return this.store.isSelected(item.path) ? this.store.selectedItems() : [item];
    }
}

/** The ⋮ button of a listed item opening its menu; only on items with one (see `ItemMenu.hasActions`). */
@Component({
    selector: 'button[sfeItemMenu]',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        type: 'button',
        class: 'icon-button small item-menu-button',
        '[title]': 'hint',
        'aria-haspopup': 'menu',
        '[attr.aria-label]': 'label()',
        '[hidden]': '!menu.hasActions(sfeItemMenu())',
        '(click)': 'toggle($event)',
    },
    template: `<svg sfeIcon="more"></svg>`,
})
export class ItemMenuButtonComponent {
    readonly sfeItemMenu = input.required<LibraryItem>();
    protected readonly hint = $localize`Show more actions (or right-click the item).`;
    protected readonly label = computed(() => $localize`More actions for “${this.sfeItemMenu().name}:name:”`);
    protected readonly menu = inject(ItemMenu);
    private readonly contextMenu = inject(ContextMenu);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    protected toggle(event: MouseEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.contextMenu.toggle(this.menu.target(this.sfeItemMenu()), this.host);
    }
}
