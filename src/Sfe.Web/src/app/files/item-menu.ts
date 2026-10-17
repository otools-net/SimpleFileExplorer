import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injectable, Injector, input, signal, untracked, viewChild } from '@angular/core';
import { CopyPathService } from '../core/copy-path.service';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ItemActions } from '../library/items/item-actions.service';

/** One entry of an item's menu. */
export interface MenuEntry {
    id: 'rename' | 'tags' | 'copyPath';
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

    /** The item whose menu is open, and where. */
    readonly open = signal<{ item: LibraryItem; x: number; y: number; trigger: HTMLElement | null } | null>(null);

    /** What the session may do with `item` that file management adds: the menu is worth showing. */
    hasActions(item: LibraryItem): boolean {
        return this.canRename(item);
    }

    /** May rename `item`: in the folder listed (its list of names changes) and on the item itself. */
    canRename(item: LibraryItem): boolean {
        return this.store.can(Rights.renameFiles) && this.store.can(Rights.renameFiles, item);
    }

    entries(item: LibraryItem): MenuEntry[] {
        const entries: MenuEntry[] = [];
        if (this.canRename(item))
            entries.push({ id: 'rename', label: 'Rename…', icon: 'edit', shortcut: 'F2', title: `Give ${item.name} another name (F2)`, run: () => this.dialogs.name()?.rename(item) });
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
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => void this.copyPaths.copy(item.path) });
        return entries;
    }

    /** A right click (or long press) on `item`: its menu, at the pointer; the browser's own with Shift, or when there's none. */
    fromEvent(event: MouseEvent, item: LibraryItem): void {
        if (event.shiftKey || !this.hasActions(item))
            return;
        event.preventDefault();
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
            <div #box class="item-menu" role="menu" [attr.aria-label]="'Actions for ' + open.item.name"
                 [style.left.px]="position().x" [style.top.px]="position().y" (keydown)="keydown($event)">
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
