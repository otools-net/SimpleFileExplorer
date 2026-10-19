import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injectable, Injector, input, signal, untracked, viewChild } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { CopyPathService } from '../core/copy-path.service';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { formatOf } from '../formats/formats';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ItemActions } from '../library/items/item-actions.service';
import { QuickLinksService } from '../library/quick-links/quick-links.service';
import { FileDownloads } from './downloads';
import { FileActions } from './file-actions';
import { FileTransfers } from './file-transfers';

/** One entry of an item's menu. */
export interface MenuEntry {
    id: 'openAsText' | 'editText' | 'editPicture' | 'rename' | 'delete' | 'cut' | 'copy' | 'paste' | 'moveTo' | 'copyTo' | 'download' | 'tags' | 'quickLink' | 'copyPath' | 'properties';
    label: string;
    icon: string;
    /** Its key, shown at the end of the line. */
    shortcut?: string;
    title: string;
    run: () => void;
}

/**
 * The menu of a listed file or folder, as in an OS file browser: opened by a right click (the browser's own menu
 * with Shift), the item's ⋮ button, or a long press on phones. Only items that something of file management applies
 * to have one, so that the browser's menu ("open in a new tab") stays for everyone else.
 */
@Injectable({ providedIn: 'root' })
export class ItemMenu {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly actions = inject(ItemActions);
    private readonly copyPaths = inject(CopyPathService);
    private readonly files = inject(FileActions);
    private readonly transfers = inject(FileTransfers);
    private readonly downloads = inject(FileDownloads);
    private readonly config = inject(ConfigService);
    private readonly quickLinks = inject(QuickLinksService);

    /** The item whose menu is open, and where. */
    readonly open = signal<{ item: LibraryItem; x: number; y: number; trigger: HTMLElement | null } | null>(null);

    /**
     * Everyone who browses has the menu, for Properties at least (the browser's own menu with Shift). The item's card
     * only has its tags and ⋮ buttons: quick links and copying the path are here too.
     */
    hasActions(item: LibraryItem): boolean {
        return this.canSeeProperties() || this.canOpenAsText(item) || this.canEditText(item) || this.canEditPicture(item) || this.quickLinks.enabled() || this.copyPaths.enabled() || this.canRename(item) || this.files.canDelete(item) || this.transfers.canMove(item) || this.transfers.canCopy(item);
    }

    /** Properties of what is listed: whoever browses. */
    canSeeProperties(): boolean {
        return this.config.can(Rights.browse);
    }

    /** Several items selected that may be renamed: the batch rename opens for them. False when fewer than two. */
    renameSelection(): boolean {
        const items = this.store.selectedItems().filter(item => this.canRename(item));
        if (items.length < 2 || this.store.searching())
            return false;
        this.dialogs.batchRename()?.open(items);
        return true;
    }

    /** May rename `item`: in the folder listed (its list of names changes) and on the item itself. */
    canRename(item: LibraryItem): boolean {
        return this.files.canRename(item);
    }

    /** A file without a viewer of its own may be opened as text (the server refuses what isn't). */
    canOpenAsText(item: LibraryItem): boolean {
        return item.kind === 'file' && !formatOf(item) && this.store.can(Rights.viewFiles, item) && Boolean(this.dialogs.viewer());
    }

    /**
     * A text file that something could be done with once changed (save over it, a copy, a download): the server tells
     * exactly when it is opened (`access`), the viewer then offers what applies.
     */
    canEditText(item: LibraryItem): boolean {
        const format = formatOf(item);
        return Boolean(format?.edits && this.dialogs.viewer() && this.store.can(Rights.viewFiles, item)
            && (this.store.can(Rights.createFiles, item) || this.store.can(Rights.download, item)));
    }

    /** A picture the editor can crop (the right, and a server with the editor). */
    canEditPicture(item: LibraryItem): boolean {
        return item.kind === 'image' && this.store.can(Rights.editImages, item) && Boolean(this.config.config()?.editor) && Boolean(this.dialogs.imageEditor());
    }

    entries(item: LibraryItem): MenuEntry[] {
        const entries: MenuEntry[] = [];
        if (this.canOpenAsText(item))
            entries.push({ id: 'openAsText', label: 'Open as text', icon: 'file', title: 'Show its content as text, in the viewer', run: () => this.dialogs.viewer()?.open(item, { asText: true }) });
        if (this.canEditText(item))
            entries.push({ id: 'editText', label: 'Edit', icon: 'edit', title: 'Open it in the viewer, ready to be changed', run: () => this.dialogs.viewer()?.open(item, { edit: true }) });
        if (this.canEditPicture(item))
            entries.push({ id: 'editPicture', label: 'Crop…', icon: 'crop', title: 'Crop the picture, then save it over the original or as a new file', run: () => void this.dialogs.imageEditor()?.open(item) });
        if (this.canRename(item)) {
            const several = this.targets(item).filter(target => this.canRename(target)).length > 1 && !this.store.searching();
            entries.push({
                id: 'rename', label: 'Rename…', icon: 'edit', shortcut: 'F2',
                title: several ? 'Rename the selected items together: find and replace, or numbered names (F2)' : `Give ${item.name} another name (F2)`,
                run: () => several ? this.renameSelection() : this.dialogs.name()?.rename(item),
            });
        }
        if (this.files.canDelete(item))
            entries.push({
                id: 'delete',
                label: this.files.alwaysPermanent ? 'Delete permanently…' : 'Delete…',
                icon: 'trash',
                shortcut: 'Del',
                title: this.files.alwaysPermanent ? `Delete ${item.name} for good (Delete)` : `Move ${item.name} to the trash (Delete; Shift+Delete: for good)`,
                run: () => this.files.delete(this.targets(item)),
            });
        const canMove = this.transfers.canMove(item);
        const canCopy = this.transfers.canCopy(item);
        if (canMove)
            entries.push({ id: 'cut', label: 'Cut', icon: 'cut', shortcut: 'Ctrl+X', title: 'Keep it to move into a folder with Paste (Ctrl+X)', run: () => this.transfers.keep('move', this.targets(item)) });
        if (canCopy)
            entries.push({ id: 'copy', label: 'Copy', icon: 'copy', shortcut: 'Ctrl+C', title: 'Keep it to copy into a folder with Paste (Ctrl+C)', run: () => this.transfers.keep('copy', this.targets(item)) });
        const clipboard = this.transfers.clipboard();
        if (clipboard && item.kind === 'directory' && this.transfers.canInto(clipboard.kind, item))
            entries.push({
                id: 'paste',
                label: `Paste into ${item.name}`,
                icon: 'paste',
                title: `${clipboard.kind === 'move' ? 'Move' : 'Copy'} ${clipboard.items.length === 1 ? clipboard.items[0].name : `the ${clipboard.items.length} items kept`} into this folder`,
                run: () => this.transfers.paste(item.path),
            });
        if (canMove)
            entries.push({ id: 'moveTo', label: 'Move to…', icon: 'move', title: 'Move it to another folder, picked next', run: () => this.transfers.pick('move', this.targets(item)) });
        if (canCopy)
            entries.push({ id: 'copyTo', label: 'Copy to…', icon: 'copy', title: 'Copy it into another folder, picked next', run: () => this.transfers.pick('copy', this.targets(item)) });
        if (this.downloads.canDownload(item)) {
            const targets = this.targets(item);
            const zip = this.downloads.isZip(targets);
            entries.push({
                id: 'download',
                label: zip ? 'Download as zip' : 'Download',
                icon: 'download',
                title: zip ? (targets.length > 1 ? 'Save the selected items to this device, in one zip file' : 'Save this folder, with everything in it, to this device as a zip file') : 'Save this file to this device',
                run: () => void this.downloads.download(targets),
            });
        }
        if (item.kind === 'directory' ? this.store.can(Rights.assignTags, item) : this.store.can(Rights.viewTags, item))
            entries.push({
                id: 'tags',
                label: item.kind === 'directory' ? 'Tag its files…' : 'Tags…',
                icon: 'tag',
                title: item.kind === 'directory' ? 'Add or remove tags on all the files of this folder (asks first)' : 'The tags of this file',
                run: () => item.kind === 'directory'
                    ? this.dialogs.scopeTags()?.open({ path: item.path, recursive: false, query: null })
                    : this.actions.openTags(item),
            });
        if (this.quickLinks.enabled()) {
            const pinned = this.quickLinks.paths().has(item.path);
            entries.push({
                id: 'quickLink',
                label: pinned ? 'Remove from quick links' : 'Add to quick links',
                icon: 'pin',
                title: pinned ? `Take ${item.name} out of your quick links` : `Show ${item.name} in your quick links, in the sidebar`,
                run: () => void this.quickLinks.toggle(item.path),
            });
        }
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => void this.copyPaths.copy(item.path) });
        if (this.canSeeProperties())
            entries.push({
                id: 'properties', label: 'Properties', icon: 'info', shortcut: 'Alt+Enter',
                title: item.kind === 'directory' ? 'Its dates, and what it holds (Alt+Enter)' : 'Its size, dates and what the index knows of it; its checksum (Alt+Enter)',
                run: () => void this.dialogs.properties()?.open(item),
            });
        return entries;
    }

    /** The selection when `item` is part of it (the menu acts on all of it, as in a file browser), else `item`. */
    private targets(item: LibraryItem): LibraryItem[] {
        return this.store.isSelected(item.path) ? this.store.selectedItems() : [item];
    }

    /**
     * A right click on `item`: its menu, at the pointer; the browser's own with Shift, or when there's none. A long press
     * on a touch screen selects it instead, as phone file managers do (its ⋮ button opens the menu there).
     */
    fromEvent(event: MouseEvent, item: LibraryItem): void {
        if (event.shiftKey || !this.hasActions(item))
            return;
        event.preventDefault();
        if (fromTouch(event)) {
            this.store.select(item, !this.store.isSelected(item.path));
            navigator.vibrate?.(15);
            return;
        }
        this.show(item, event.clientX, event.clientY, event.currentTarget as HTMLElement | null);
    }

    /** Opens the menu of `item` under `trigger` (its ⋮ button). */
    showAt(item: LibraryItem, trigger: HTMLElement): void {
        const box = trigger.getBoundingClientRect();
        this.show(item, box.left, box.bottom + 4, trigger);
    }

    close(): void {
        this.open.set(null);
    }

    private show(item: LibraryItem, x: number, y: number, trigger: HTMLElement | null): void {
        this.open.set({ item, x, y, trigger });
    }
}

/** The menu event comes from a finger: a long press (Chrome tells it; others, by a screen without a mouse). */
export function fromTouch(event: MouseEvent): boolean {
    const pointer = (event as Partial<PointerEvent>).pointerType;
    if (pointer)
        return pointer === 'touch';
    return typeof matchMedia === 'function' && matchMedia('(hover: none) and (pointer: coarse)').matches;
}

/** The open item menu (one for the page), kept inside the window, closed by a click elsewhere, Esc, scrolling or navigating. */
@Component({
    selector: 'sfe-item-menu',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '(document:pointerdown)': 'outside($event)',
        '(window:scroll)': 'menu.close()',
        '(window:resize)': 'menu.close()',
    },
    template: `
        @if (menu.open(); as open) {
            <!-- On phones: an action sheet at the bottom, under its item's name, over a backdrop that closes it. -->
            <div class="item-menu-backdrop" aria-hidden="true"></div>
            <div #box class="item-menu" role="menu" [attr.aria-label]="'Actions for ' + open.item.name"
                 [style.left.px]="position().x" [style.top.px]="position().y" (keydown)="keydown($event)">
                <div class="item-menu-title">{{ open.item.name }}</div>
                @for (entry of entries(); track entry.id) {
                    <button type="button" role="menuitem" class="item-menu-entry" [attr.data-entry]="entry.id" [title]="entry.title" (click)="run(entry)">
                        <svg [sfeIcon]="entry.icon"></svg>
                        <span>{{ entry.label }}</span>
                        @if (entry.shortcut) {
                            <kbd>{{ entry.shortcut }}</kbd>
                        }
                    </button>
                }
            </div>
        }
    `,
})
export class ItemMenuComponent {
    protected readonly menu = inject(ItemMenu);
    private readonly injector = inject(Injector);
    private readonly box = viewChild<ElementRef<HTMLElement>>('box');
    protected readonly entries = computed(() => {
        const open = this.menu.open();
        return open ? this.menu.entries(open.item) : [];
    });
    /** Where the menu shows: at the pointer, moved back inside the window once its size is known. */
    protected readonly position = signal({ x: 0, y: 0 });

    constructor() {
        const store = inject(ListingStore);
        // Another listing: the item may not be there any more.
        effect(() => {
            store.generation();
            untracked(() => this.menu.close());
        });
        effect(() => {
            const open = this.menu.open();
            if (!open)
                return;
            untracked(() => this.position.set({ x: open.x, y: open.y }));
            afterNextRender(() => this.place(open.x, open.y), { injector: this.injector });
        });
        inject(DestroyRef).onDestroy(() => this.menu.close());
    }

    private place(x: number, y: number): void {
        const box = this.box()?.nativeElement;
        if (!box)
            return;
        const width = box.offsetWidth;
        const height = box.offsetHeight;
        this.position.set({
            x: Math.max(4, Math.min(x, window.innerWidth - width - 4)),
            y: Math.max(4, y + height > window.innerHeight - 4 ? y - height : y),
        });
        box.querySelector<HTMLElement>('button')?.focus({ preventScroll: true });
    }

    protected run(entry: MenuEntry): void {
        this.menu.close();
        entry.run();
    }

    protected outside(event: PointerEvent): void {
        const open = this.menu.open();
        const target = event.target as Node;
        // The ⋮ button that opened it closes it itself (a toggle).
        if (open && !this.box()?.nativeElement.contains(target) && !open.trigger?.contains(target))
            this.menu.close();
    }

    /** Arrows move between the entries; Esc closes and goes back to what opened the menu. */
    protected keydown(event: KeyboardEvent): void {
        const buttons = [...this.box()?.nativeElement.querySelectorAll<HTMLElement>('button') ?? []];
        const index = buttons.indexOf(document.activeElement as HTMLElement);
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            const step = event.key === 'ArrowDown' ? 1 : -1;
            buttons[(index + step + buttons.length) % buttons.length]?.focus();
        } else if (event.key === 'Escape' || event.key === 'Tab') {
            event.preventDefault();
            event.stopPropagation();
            const trigger = this.menu.open()?.trigger;
            this.menu.close();
            trigger?.focus();
        }
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
        title: 'More actions (right click)',
        'aria-haspopup': 'menu',
        '[attr.aria-label]': '"More actions for " + sfeItemMenu().name',
        '[hidden]': '!menu.hasActions(sfeItemMenu())',
        '(click)': 'toggle($event)',
    },
    template: `<svg sfeIcon="more"></svg>`,
})
export class ItemMenuButtonComponent {
    readonly sfeItemMenu = input.required<LibraryItem>();
    protected readonly menu = inject(ItemMenu);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    protected toggle(event: MouseEvent): void {
        event.preventDefault();
        event.stopPropagation();
        if (this.menu.open()?.item.path === this.sfeItemMenu().path)
            this.menu.close();
        else
            this.menu.showAt(this.sfeItemMenu(), this.host);
    }
}
