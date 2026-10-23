import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, Directive, effect, ElementRef, inject, Injectable, Injector, input, signal, untracked, viewChild } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { LibraryItem } from '../core/models';
import { IconComponent } from '../shared/icon.component';

/**
 * One entry of a context menu: an action (`run`), a link (`href`; `newTab` opens it in a tab of its own, as a real
 * link so no popup blocker stops it and a middle click works too), or a submenu shown in its place.
 */
export interface MenuEntry {
    id: string;
    label: string;
    icon: string;
    /** Its key, shown at the end of the line. */
    shortcut?: string;
    title: string;
    run?: () => void;
    href?: string;
    newTab?: boolean;
    submenu?: () => MenuEntry[];
}

/** What a menu is about (see agents/docs/development/context-menu-plan.md for which menu shows where). */
export type MenuTargetKind = 'file' | 'folder' | 'folderLink' | 'fileLink' | 'tag' | 'background' | 'page' | 'job' | 'trash' | 'historyEntry' | 'appLink';

/** Something with a context menu: it brings its own entries, so the pages, the viewer or the administration add theirs. */
export interface MenuTarget {
    kind: MenuTargetKind;
    /** The same key: the same target (its ⋮ button closes the menu it opened). */
    key: string;
    /** The menu's title on phones (a sheet), and its label. */
    name: string;
    entries(): MenuEntry[];
    /** A long press on a touch screen does this instead of opening the menu (listed items: selected). */
    touch?(): void;
    /** The listed item it is about. */
    item?: LibraryItem;
}

/** How long after the ContextMenu key or Shift+F10 the browser's own `contextmenu` event of that key is swallowed. */
const keyboardEcho = 800;

/**
 * Every right click of the app goes through here: the menu of what was clicked (an element using `useContextMenu`
 * or `[sfeContextMenu]`), else the page's own (`page`). The browser's own menu stays with Shift, in text fields and
 * on selected text (`keepsBrowserMenu`), and where there's no menu to show.
 */
@Injectable({ providedIn: 'root' })
export class ContextMenu {
    /** The open menu: its target, where, and what opened it (focus goes back there). */
    readonly open = signal<{ target: MenuTarget; x: number; y: number; trigger: HTMLElement | null } | null>(null);
    /** The menu of the page shown, for a right click on nothing with a menu of its own; set by the page. */
    readonly page = signal<MenuTarget | null>(null);
    private readonly elements = new WeakMap<Element, () => MenuTarget | null>();
    private keyboardAt = -Infinity;

    /** `element` has the menu of `target` (for the keyboard: the focused element's menu). */
    register(element: Element, target: () => MenuTarget | null): void {
        this.elements.set(element, target);
    }

    /**
     * A right click on `element`, whose menu is `target`'s: shown at the pointer. Nothing when an element inside it
     * already showed its own, or the browser's menu is kept (Shift, text, nothing to offer). A long press on a touch
     * screen does the target's `touch` instead, when it has one (listed items: selected, as phone file managers do).
     */
    fromEvent(event: MouseEvent, target: MenuTarget | null, element: HTMLElement | null = event.currentTarget as HTMLElement | null): void {
        if (event.defaultPrevented)
            return;
        if (this.keyboardEcho()) {
            event.preventDefault();
            return;
        }
        if (!target || keepsBrowserMenu(event) || target.entries().length === 0)
            return;
        event.preventDefault();
        if (target.touch && fromTouch(event)) {
            target.touch();
            navigator.vibrate?.(15);
            return;
        }
        this.show(target, event.clientX, event.clientY, element);
    }

    /**
     * A right click nothing handled: the page's menu. Not in dialogs (the viewer, sheets): they keep the browser's menu
     * until they have menus of their own (phases 2 and 3 of the plan).
     */
    fromPage(event: MouseEvent): void {
        if (event.target instanceof Element && event.target.closest('dialog'))
            return;
        this.fromEvent(event, this.page(), null);
    }

    /** The ContextMenu key or Shift+F10: the menu of the focused element (or of the page), under it. */
    fromKey(event: KeyboardEvent): void {
        if (event.key !== 'ContextMenu' && !(event.key === 'F10' && event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey))
            return;
        const focused = document.activeElement instanceof HTMLElement && document.activeElement !== document.body ? document.activeElement : null;
        if (focused && isTextField(focused))
            return;
        const [element, target] = this.targetOf(focused);
        if (!target || target.entries().length === 0)
            return;
        event.preventDefault();
        this.keyboardAt = performance.now();
        const box = element?.getBoundingClientRect();
        this.show(target, box ? box.left : window.innerWidth / 2, box ? box.bottom + 4 : window.innerHeight / 3, element ?? focused);
    }

    /** Opens the menu of `target` under `trigger` (a ⋮ button). */
    showAt(target: MenuTarget, trigger: HTMLElement): void {
        const box = trigger.getBoundingClientRect();
        this.show(target, box.left, box.bottom + 4, trigger);
    }

    /** The ⋮ button of `target`: opens its menu, or closes it when it's the one open. */
    toggle(target: MenuTarget, trigger: HTMLElement): void {
        if (this.open()?.target.key === target.key)
            this.close();
        else
            this.showAt(target, trigger);
    }

    close(): void {
        this.open.set(null);
    }

    private show(target: MenuTarget, x: number, y: number, trigger: HTMLElement | null): void {
        this.open.set({ target, x, y, trigger });
    }

    /** The nearest element around `element` with a menu, and that menu; else the page's. */
    private targetOf(element: HTMLElement | null): [HTMLElement | null, MenuTarget | null] {
        for (let node: HTMLElement | null = element; node; node = node.parentElement) {
            const target = this.elements.get(node)?.();
            if (target)
                return [node, target];
        }
        return [null, this.page()];
    }

    /** The browser's own `contextmenu` event following the key that already opened the menu. */
    private keyboardEcho(): boolean {
        return performance.now() - this.keyboardAt < keyboardEcho;
    }
}

/**
 * The browser's own menu is kept: with Shift (on purpose), in a text field (spelling, paste), and on selected text
 * (copy it).
 */
export function keepsBrowserMenu(event: MouseEvent): boolean {
    if (event.shiftKey)
        return true;
    const target = event.target instanceof Element ? event.target : null;
    if (target && isTextField(target))
        return true;
    const selection = typeof getSelection === 'function' ? getSelection() : null;
    if (target && selection && !selection.isCollapsed && selection.toString().trim())
        for (let i = 0; i < selection.rangeCount; i++)
            if (selection.getRangeAt(i).intersectsNode(target))
                return true;
    return false;
}

/** Inputs taking text (not check boxes or buttons), text areas and editable content (CodeMirror). */
const textInputs = new Set(['', 'text', 'search', 'url', 'email', 'password', 'number', 'tel', 'date', 'datetime-local', 'month', 'week', 'time']);

export function isTextField(element: Element): boolean {
    const field = element.closest('input, textarea, [contenteditable]');
    if (!field)
        return false;
    if (field instanceof HTMLInputElement)
        return textInputs.has((field.getAttribute('type') ?? '').toLowerCase());
    if (field instanceof HTMLTextAreaElement)
        return true;
    return field.getAttribute('contenteditable') !== 'false';
}

/** The menu event comes from a finger: a long press (Chrome tells it; others, by a screen without a mouse). */
export function fromTouch(event: MouseEvent): boolean {
    const pointer = (event as Partial<PointerEvent>).pointerType;
    if (pointer)
        return pointer === 'touch';
    return typeof matchMedia === 'function' && matchMedia('(hover: none) and (pointer: coarse)').matches;
}

/** The host element of the component or directive calling it has the menu of `target` (called while it is made). */
export function useContextMenu(target: () => MenuTarget | null): void {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const menu = inject(ContextMenu);
    menu.register(host, target);
    const listener = (event: MouseEvent) => menu.fromEvent(event, target(), host);
    host.addEventListener('contextmenu', listener);
    inject(DestroyRef).onDestroy(() => host.removeEventListener('contextmenu', listener));
}

/** `[sfeContextMenu]="target"`: the element has the menu of `target` (none: null). */
@Directive({ selector: '[sfeContextMenu]' })
export class ContextMenuDirective {
    readonly sfeContextMenu = input<MenuTarget | null>(null);

    constructor() {
        useContextMenu(() => this.sfeContextMenu());
    }
}

/**
 * The open context menu (one for the app, in the root component), kept inside the window, closed by a click
 * elsewhere, Esc, scrolling or navigating; a sheet at the bottom on phones. It also takes the right clicks nothing
 * else handled (the page's menu) and the ContextMenu key.
 */
@Component({
    selector: 'sfe-context-menu',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '(document:pointerdown)': 'outside($event)',
        '(document:contextmenu)': 'menu.fromPage($event)',
        '(document:keydown)': 'menu.fromKey($event)',
        '(window:scroll)': 'menu.close()',
        '(window:resize)': 'menu.close()',
    },
    template: `
        @if (menu.open(); as open) {
            <!-- On phones: an action sheet at the bottom, under its target's name, over a backdrop that closes it. -->
            <div class="item-menu-backdrop sheet-backdrop" aria-hidden="true"></div>
            <div #box class="item-menu phone-sheet" role="menu" [attr.aria-label]="'Actions for ' + open.target.name" [attr.data-menu]="open.target.kind"
                 [style.left.px]="position().x" [style.top.px]="position().y" (keydown)="keydown($event)">
                <div class="item-menu-title">{{ open.target.name }}</div>
                @if (submenu()) {
                    <button type="button" role="menuitem" class="item-menu-entry" data-entry="back" title="Back to the other actions (Left arrow)" (click)="back()">
                        <svg sfeIcon="left"></svg>
                        <span>Back</span>
                    </button>
                }
                @for (entry of entries(); track entry.id) {
                    @if (entry.href) {
                        <a role="menuitem" class="item-menu-entry" [attr.data-entry]="entry.id" [href]="entry.href" [title]="entry.title"
                           [attr.target]="entry.newTab ? '_blank' : null" rel="noopener" (click)="followed(entry)">
                            <svg [sfeIcon]="entry.icon"></svg>
                            <span>{{ entry.label }}</span>
                            @if (entry.shortcut) {
                                <kbd>{{ entry.shortcut }}</kbd>
                            }
                        </a>
                    } @else {
                        <button type="button" role="menuitem" class="item-menu-entry" [attr.data-entry]="entry.id" [title]="entry.title"
                                [attr.aria-haspopup]="entry.submenu ? 'menu' : null" (click)="run(entry)">
                            <svg [sfeIcon]="entry.icon"></svg>
                            <span>{{ entry.label }}</span>
                            @if (entry.shortcut) {
                                <kbd>{{ entry.shortcut }}</kbd>
                            }
                            @if (entry.submenu) {
                                <svg sfeIcon="right"></svg>
                            }
                        </button>
                    }
                }
            </div>
        }
    `,
})
export class ContextMenuComponent {
    protected readonly menu = inject(ContextMenu);
    private readonly injector = inject(Injector);
    private readonly box = viewChild<ElementRef<HTMLElement>>('box');
    /** The submenu shown in the menu's place, if any. */
    protected readonly submenu = signal<MenuEntry[] | null>(null);
    protected readonly entries = computed(() => {
        const open = this.menu.open();
        return this.submenu() ?? (open ? open.target.entries() : []);
    });
    /** Where the menu shows: at the pointer, moved back inside the window once its size is known. */
    protected readonly position = signal({ x: 0, y: 0 });

    constructor() {
        effect(() => {
            const open = this.menu.open();
            untracked(() => this.submenu.set(null));
            if (!open)
                return;
            untracked(() => this.position.set({ x: open.x, y: open.y }));
            afterNextRender(() => this.place(open.x, open.y), { injector: this.injector });
        });
        const navigations = inject(Router, { optional: true })?.events.subscribe(event => {
            if (event instanceof NavigationStart)
                this.menu.close();
        });
        inject(DestroyRef).onDestroy(() => {
            navigations?.unsubscribe();
            this.menu.close();
        });
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
        this.focusFirst();
    }

    private focusFirst(): void {
        this.box()?.nativeElement.querySelector<HTMLElement>('.item-menu-entry')?.focus({ preventScroll: true });
    }

    protected run(entry: MenuEntry): void {
        if (entry.submenu) {
            this.submenu.set(entry.submenu());
            afterNextRender(() => this.focusFirst(), { injector: this.injector });
            return;
        }
        this.menu.close();
        entry.run?.();
    }

    /** A link entry: the browser follows it; the menu closes. */
    protected followed(entry: MenuEntry): void {
        this.menu.close();
        entry.run?.();
    }

    protected back(): void {
        this.submenu.set(null);
        afterNextRender(() => this.focusFirst(), { injector: this.injector });
    }

    protected outside(event: PointerEvent): void {
        const open = this.menu.open();
        const target = event.target as Node;
        // The ⋮ button that opened it closes it itself (a toggle).
        if (open && !this.box()?.nativeElement.contains(target) && !open.trigger?.contains(target))
            this.menu.close();
    }

    /** Arrows move between the entries (Right opens a submenu, Left goes back); Esc closes and goes back to what opened the menu. */
    protected keydown(event: KeyboardEvent): void {
        const entries = [...this.box()?.nativeElement.querySelectorAll<HTMLElement>('.item-menu-entry') ?? []];
        const index = entries.indexOf(document.activeElement as HTMLElement);
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            const step = event.key === 'ArrowDown' ? 1 : -1;
            entries[(index + step + entries.length) % entries.length]?.focus();
        } else if (event.key === 'ArrowRight' && entries[index]?.getAttribute('aria-haspopup')) {
            event.preventDefault();
            entries[index].click();
        } else if (event.key === 'ArrowLeft' && this.submenu()) {
            event.preventDefault();
            this.back();
        } else if (event.key === 'Escape' || event.key === 'Tab') {
            event.preventDefault();
            event.stopPropagation();
            const trigger = this.menu.open()?.trigger;
            this.menu.close();
            trigger?.focus();
        }
    }
}
