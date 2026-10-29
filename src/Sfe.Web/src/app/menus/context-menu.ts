import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, Directive, effect, ElementRef, inject, Injectable, Injector, input, signal, untracked, viewChild } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { copyText } from '../core/copy-path.service';
import { LibraryItem } from '../core/models';
import { ToastService } from '../core/toast.service';
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
    /** An on/off entry (loop, mute): a check mark while on. */
    checked?: boolean;
    /** Shown greyed out and not run (its title says why), as its button is disabled. */
    disabled?: boolean;
}

/** What a menu is about (see agents/docs/development/context-menu-plan.md for which menu shows where). */
export type MenuTargetKind = 'file' | 'folder' | 'folderLink' | 'fileLink' | 'tag' | 'background' | 'page' | 'job' | 'trash' | 'historyEntry' | 'appLink' | 'overflow';

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
    /**
     * The open menu: its target, where, what opened it (focus goes back there), and whether a finger did (`touch`: the
     * focus stays where it is, nothing to move between entries with).
     */
    readonly open = signal<{ target: MenuTarget; x: number; y: number; trigger: HTMLElement | null; touch: boolean } | null>(null);
    /** The menu of the page shown, for a right click on nothing with a menu of its own; set by the page. */
    readonly page = signal<MenuTarget | null>(null);
    private readonly elements = new WeakMap<Element, () => MenuTarget | null>();
    private readonly toasts = inject(ToastService);
    private keyboardAt = -Infinity;
    /** The pointer of the last press: a menu opened right after a finger's is a touch one. */
    private lastPointer = '';

    constructor() {
        addEventListener('pointerdown', event => { this.lastPointer = event.pointerType; }, { capture: true, passive: true });
    }

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
     * A right click nothing handled: a link's menu (open it, in a new tab, copy its address), else the page's. Where
     * no page menu fits (a dialog, an area marked `data-context-menu="none"`: the folder picker), no menu at all; the
     * browser's still comes with Shift and in text fields. An area marked `data-context-menu="own"` (the viewer's
     * readers and editors) keeps its own right click, or the browser's.
     */
    fromPage(event: MouseEvent): void {
        if (event.defaultPrevented)
            return;
        const area = event.target instanceof Element ? event.target.closest('[data-context-menu], dialog') : null;
        if (area?.getAttribute('data-context-menu') === 'own')
            return;
        const link = linkAt(event.target, 'a[href]');
        if (!link && area) {
            if (this.keyboardEcho() || !keepsBrowserMenu(event))
                event.preventDefault();
            return;
        }
        this.fromEvent(event, link ? this.linkTarget(link) : this.page(), link);
    }

    /** The menu of a link: open it (as a click on it does), in a new tab, or copy its address. */
    linkTarget(link: HTMLAnchorElement): MenuTarget {
        const href = link.href;
        return {
            kind: 'appLink',
            key: `link:${href}`,
            name: link.textContent?.trim() || link.getAttribute('aria-label') || href,
            entries: () => [
                { id: 'open', label: 'Open', icon: 'right', title: 'Follow this link', run: () => link.click() },
                { id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'Follow this link in a tab of its own', href, newTab: true },
                { id: 'copyLink', label: 'Copy link address', icon: 'copy', title: 'Copy where this link goes', run: () => void this.copyLink(href) },
            ],
        };
    }

    /** A page's own menu, for pages with nothing else to offer (the administration): back, reload, its address. */
    pageTarget(name: string): MenuTarget {
        return {
            kind: 'page',
            key: `page:${name}`,
            name,
            entries: () => [
                { id: 'back', label: 'Back', icon: 'left', title: 'The page before this one', run: () => history.back() },
                { id: 'reload', label: 'Reload', icon: 'refresh', title: 'Load this page again', run: () => location.reload() },
                { id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'This page in a tab of its own', href: location.href, newTab: true },
                { id: 'copyLink', label: 'Copy page link', icon: 'copy', title: 'Copy the address of this page', run: () => void this.copyLink(location.href) },
            ],
        };
    }

    private async copyLink(href: string): Promise<void> {
        try {
            await copyText(href);
            this.toasts.show('Link copied.');
        } catch {
            this.toasts.show('The link could not be copied.', { error: true });
        }
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
        this.show(target, box ? box.left : window.innerWidth / 2, box ? box.bottom + 4 : window.innerHeight / 3, element ?? focused, false);
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

    /** Closes the menu if it is the one of `key` (what opened it is going away). */
    closeIf(key: string): void {
        if (this.open()?.target.key === key)
            this.close();
    }

    private show(target: MenuTarget, x: number, y: number, trigger: HTMLElement | null, touch = this.lastPointer === 'touch'): void {
        this.open.set({ target, x, y, trigger, touch });
    }

    /** The nearest element around `element` with a menu, and that menu; else a link's menu, else the page's. */
    private targetOf(element: HTMLElement | null): [HTMLElement | null, MenuTarget | null] {
        for (let node: HTMLElement | null = element; node; node = node.parentElement) {
            const target = this.elements.get(node)?.();
            if (target)
                return [node, target];
        }
        const link = linkAt(element, 'a[href]');
        return link ? [link, this.linkTarget(link)] : [null, this.page()];
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

/**
 * The link around `target` matching `selector` that leads somewhere else: not one to a place in the same page, nor a
 * download, nor a script.
 */
function linkAt(target: EventTarget | null, selector: string): HTMLAnchorElement | null {
    const link = target instanceof Element ? target.closest<HTMLAnchorElement>(selector) : null;
    const href = link?.getAttribute('href') ?? '';
    if (!link || !href || href.startsWith('#') || link.hasAttribute('download') || !/^https?:$/.test(link.protocol))
        return null;
    return link;
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

/** Puts the menu's layer in the top layer; browsers without the popover API (and jsdom) keep it in place, above the page by its z-index. */
function showLayer(layer: HTMLElement | undefined): void {
    try {
        if (layer && typeof layer.showPopover === 'function' && !layer.matches(':popover-open'))
            layer.showPopover();
    } catch {
        // no popovers here
    }
}

/** The open modal dialog `trigger` is in, else the last one opened (the viewer, a sheet, the picture editor); null when none. */
function openDialog(trigger: HTMLElement | null): HTMLDialogElement | null {
    const own = trigger?.closest<HTMLDialogElement>('dialog[open]');
    if (own)
        return own;
    const modal = [...document.querySelectorAll<HTMLDialogElement>('dialog[open]')].filter(dialog => {
        try {
            return dialog.matches(':modal');
        } catch {
            return false;   // no :modal (jsdom)
        }
    });
    return modal.at(-1) ?? null;
}

/** The menu shows as a sheet at the bottom (phone-sheets.css), not at the pointer. */
function isSheet(): boolean {
    return typeof matchMedia === 'function' && matchMedia('(max-width: 640px)').matches;
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

/** The page of the component calling it has the menu of `target` while it is shown (called while it is made). */
export function usePageMenu(target: MenuTarget): void {
    const menu = inject(ContextMenu);
    menu.page.set(target);
    inject(DestroyRef).onDestroy(() => {
        if (menu.page() === target)
            menu.page.set(null);
    });
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
        '(window:scroll)': 'onScroll()',
        '(window:resize)': 'onResize()',
    },
    template: `
        @if (menu.open(); as open) {
            <!-- The top layer (a manual popover): above the page and an open modal dialog, which it is moved into. -->
            <div #layer class="context-menu-layer" [attr.popover]="popovers ? 'manual' : null">
            <!-- On phones: an action sheet at the bottom, under its target's name, over a backdrop that closes it. -->
            <div #backdrop class="item-menu-backdrop sheet-backdrop" aria-hidden="true" (click)="menu.close()"></div>
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
                        <button type="button" [attr.role]="entry.checked === undefined ? 'menuitem' : 'menuitemcheckbox'" class="item-menu-entry" [attr.data-entry]="entry.id"
                                [title]="entry.title" [attr.aria-checked]="entry.checked ?? null" [attr.aria-disabled]="entry.disabled || null"
                                [attr.aria-haspopup]="entry.submenu ? 'menu' : null" (click)="run(entry)">
                            <svg [sfeIcon]="entry.icon"></svg>
                            <span>{{ entry.label }}</span>
                            @if (entry.shortcut) {
                                <kbd>{{ entry.shortcut }}</kbd>
                            }
                            @if (entry.checked) {
                                <svg sfeIcon="check" class="item-menu-check"></svg>
                            }
                            @if (entry.submenu) {
                                <svg sfeIcon="right"></svg>
                            }
                        </button>
                    }
                }
            </div>
            </div>
        }
    `,
})
export class ContextMenuComponent {
    protected readonly menu = inject(ContextMenu);
    private readonly injector = inject(Injector);
    /** The browser has popovers (the top layer); without them (jsdom) the layer stays in the page, and focusable. */
    protected readonly popovers = typeof HTMLElement.prototype.showPopover === 'function';
    private readonly box = viewChild<ElementRef<HTMLElement>>('box');
    private readonly layer = viewChild<ElementRef<HTMLElement>>('layer');
    private readonly backdrop = viewChild<ElementRef<HTMLElement>>('backdrop');
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    /** Where the menu lives when no modal dialog is open (the root component). */
    private home: Node | null = null;
    /** The submenu shown in the menu's place, if any. */
    protected readonly submenu = signal<MenuEntry[] | null>(null);
    protected readonly entries = computed(() => {
        const open = this.menu.open();
        return this.submenu() ?? (open ? open.target.entries() : []);
    });
    /** Where the menu shows: at the pointer, moved back inside the window once its size is known. */
    protected readonly position = signal({ x: 0, y: 0 });
    /** The window's width when the menu opened: a resize changing only the height doesn't close a phone sheet. */
    private openWidth = 0;

    constructor() {
        effect(() => {
            const open = this.menu.open();
            untracked(() => this.submenu.set(null));
            this.home ??= this.host.parentNode;
            // Inside the modal dialog the menu belongs to (the rest of the page is inert while one is open), else home.
            const parent = open ? openDialog(open.trigger) ?? this.home : this.home;
            if (parent && this.host.parentNode !== parent)
                parent.appendChild(this.host);
            if (!open)
                return;
            untracked(() => this.position.set({ x: open.x, y: open.y }));
            this.openWidth = window.innerWidth;
            afterNextRender(() => {
                showLayer(this.layer()?.nativeElement);
                // A phone's sheet is placed by its CSS (phone-sheets.css): measuring it forced a layout of the menu inside
                // this frame (~50 ms of a phone's opening it, e2e menu-profile.spec.ts) for a position it doesn't use.
                if (!isSheet())
                    this.place(open.x, open.y);
                // A finger has no keys to move between the entries: the focus stays (no ring, no work); keys and mouse get it.
                if (!open.touch)
                    this.focusFirst();
            }, { injector: this.injector });
        });
        const navigations = inject(Router, { optional: true })?.events.subscribe(event => {
            if (event instanceof NavigationStart)
                this.menu.close();
        });
        // Esc closes the menu wherever the focus is: one a finger opened leaves it outside (a tablet's keyboard). Caught
        // before a dialog's own Esc, which would close the viewer under the menu.
        const escape = (event: KeyboardEvent) => {
            if (event.key !== 'Escape' || !this.menu.open() || this.box()?.nativeElement.contains(event.target as Node))
                return;
            event.preventDefault();
            event.stopPropagation();
            this.menu.close();
        };
        document.addEventListener('keydown', escape, true);
        inject(DestroyRef).onDestroy(() => {
            document.removeEventListener('keydown', escape, true);
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
    }

    private focusFirst(): void {
        this.box()?.nativeElement.querySelector<HTMLElement>('.item-menu-entry')?.focus({ preventScroll: true });
    }

    protected run(entry: MenuEntry): void {
        // Still focusable (the arrows go through it), but does nothing, as its disabled button.
        if (entry.disabled)
            return;
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

    /** A menu at the pointer would be left behind by the page; a phone sheet stays at the bottom, the page frozen behind it. */
    protected onScroll(): void {
        if (!isSheet())
            this.menu.close();
    }

    /**
     * A phone's address bar shows again when a sheet freezes the page behind it (hidden by scrolling down), which
     * resizes the window right after the sheet opened: only a change of width (turning the phone) closes a sheet.
     */
    protected onResize(): void {
        if (!isSheet() || window.innerWidth !== this.openWidth)
            this.menu.close();
    }

    protected outside(event: PointerEvent): void {
        const open = this.menu.open();
        const target = event.target as Node;
        // The ⋮ button that opened it closes it itself (a toggle); the sheet's backdrop on its click: closed at the
        // press, the tap's click would land on what is under it.
        if (open && !this.box()?.nativeElement.contains(target) && !open.trigger?.contains(target) && target !== this.backdrop()?.nativeElement)
            this.menu.close();
    }

    /** Arrows move between the entries (Right opens a submenu, Left goes back); Esc closes and goes back to what opened the menu. */
    protected keydown(event: KeyboardEvent): void {
        const entries = [...this.box()?.nativeElement.querySelectorAll<HTMLElement>('.item-menu-entry') ?? []];
        const index = entries.indexOf(document.activeElement as HTMLElement);
        // The menu's keys stay in it (a dialog it is in has keys of its own: the viewer's arrows, Esc).
        if (['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Escape'].includes(event.key))
            event.stopPropagation();
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
