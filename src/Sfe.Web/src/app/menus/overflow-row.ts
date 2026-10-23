import { booleanAttribute, ChangeDetectionStrategy, Component, computed, DestroyRef, Directive, ElementRef, inject, input, OnDestroy, OnInit, signal } from '@angular/core';
import { createOverflowManager, OverflowManager } from '@fluentui/priority-overflow';
import { IconComponent } from '../shared/icon.component';
import { ContextMenu, MenuEntry, MenuTarget } from './context-menu';

let nextId = 0;

/**
 * A row of buttons that never wraps nor cuts one off: when it is too narrow, buttons leave it, the rightmost first
 * (`overflowPriority`: a lower one leaves earlier), and a "⋯" button (`button[sfeOverflowMore]`, the row's last child)
 * shows them in the app's menu. Every child of the row is a `[sfeOverflowItem]` (`overflowStay` for the ones that never
 * leave: their width must be counted too) but the "⋯"; what doesn't take part goes outside the row. Measuring and
 * choosing: `@fluentui/priority-overflow`, from the items' widths and the row's (agents/docs/development/overflow-menus-plan.md).
 */
@Directive({ selector: '[sfeOverflowRow]', host: { class: 'overflow-row' } })
export class OverflowRowDirective implements OnInit {
    /** What the row is ("Player controls"): the menu's title on phones (a sheet). */
    readonly sfeOverflowRow = input.required<string>();
    /**
     * The element whose width is the room the row has, when not the row itself: a row as wide as its content (a bar
     * centred at the bottom) is measured in a full-width element around it. Read once.
     */
    readonly overflowIn = input<HTMLElement | null>(null);
    private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private readonly contextMenu = inject(ContextMenu);
    private readonly items = new Map<string, OverflowItemDirective>();
    private readonly key = `overflow:${++nextId}`;
    /** The items in the menu, in the row's order. */
    readonly hidden = signal<readonly OverflowItemDirective[]>([]);
    /** Some of the items that left have an entry: the "⋯" shows (a part of a text leaving alone doesn't need it). */
    readonly hasMenu = computed(() => this.hidden().some(item => item.sfeOverflowItem()));
    private readonly manager: OverflowManager | null;
    /** An item whose width changes (a label) changes what fits, as the row's own width does. */
    private readonly resizes: ResizeObserver | null;
    /** The room kept for the gaps (and the row's padding), given to the library as its padding (see `fitGaps`). */
    private padding = 0;
    /** The width `padding` was found for. */
    private measuredWidth = -1;

    constructor() {
        // No ResizeObserver (jsdom): nothing leaves.
        const supported = typeof window.ResizeObserver === 'function';
        this.manager = supported ? createOverflowManager({
            padding: 0,
            onUpdateItemVisibility: ({ item, visible }) => item.element.toggleAttribute('data-overflowed', !visible),
            onUpdateOverflow: ({ invisibleItems }) => {
                this.hidden.set(invisibleItems
                    .map(item => item.element)
                    .sort((a, b) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1)
                    .map(element => [...this.items.values()].find(item => item.element === element)!)
                    .filter(Boolean));
                this.fitGaps();
            },
        }) : null;
        this.resizes = supported ? new ResizeObserver(() => this.manager?.update()) : null;
        inject(DestroyRef).onDestroy(() => {
            this.resizes?.disconnect();
            this.manager?.disconnect();
            this.contextMenu.closeIf(this.key);
        });
    }

    ngOnInit(): void {
        // After the inputs (`overflowIn`), before the items' own ngOnInit add them.
        this.manager?.observe(this.overflowIn() ?? this.element);
    }

    add(item: OverflowItemDirective): void {
        this.items.set(item.id, item);
        this.manager?.addItem({ id: item.id, element: item.element, priority: item.overflowPriority(), pinned: item.overflowStay() });
        this.resizes?.observe(item.element);
        this.measuredWidth = -1;
        this.manager?.update();
    }

    remove(item: OverflowItemDirective): void {
        this.items.delete(item.id);
        this.resizes?.unobserve(item.element);
        this.manager?.removeItem(item.id);
        this.measuredWidth = -1;
        this.manager?.update();
    }

    setMore(button: HTMLElement | null): void {
        if (button)
            this.manager?.addOverflowMenu(button);
        else
            this.manager?.removeOverflowMenu();
    }

    /** Measures again now (tests; a change the observers can't see). */
    update(): void {
        this.manager?.update();
    }

    /** The "⋯" menu: the entries of the items that left, computed now (rights, states, labels). */
    target(): MenuTarget {
        return { kind: 'overflow', key: this.key, name: this.sfeOverflowRow(), entries: () => this.hidden().flatMap(item => item.sfeOverflowItem()?.() ?? []) };
    }

    /**
     * The library adds up the items' widths, not the gaps between them nor the row's padding (and border, measured in
     * `overflowIn`): they are its padding, for the items shown (with a width: a text shown in another's place takes
     * none while hidden) and the "⋯". That number changes with what fits, so within one width the padding only grows: an
     * item that fitted without its gap leaves again and stays out, never back and forth. A new width (or items added or
     * removed) starts again from the gaps always there: the items that stay, and the "⋯" or an item that may leave.
     */
    private fitGaps(): void {
        if (!this.manager)
            return;
        const style = getComputedStyle(this.element);
        const px = (value: string) => parseFloat(value) || 0;
        const gap = px(style.columnGap);
        // clientWidth holds the row's padding (not its border): room taken too; measured in `overflowIn`, its border as well.
        const chrome = px(style.paddingLeft) + px(style.paddingRight)
            + (this.overflowIn() ? px(style.borderLeftWidth) + px(style.borderRightWidth) : 0);
        const room = (count: number) => chrome + gap * Math.max(count - 1, 0);
        const width = (this.overflowIn() ?? this.element).clientWidth;
        if (width !== this.measuredWidth) {
            this.measuredWidth = width;
            const staying = [...this.items.values()].filter(item => item.overflowStay() && item.element.offsetWidth > 0).length;
            const floor = room(staying + (this.items.size > staying ? 1 : 0));
            if (floor !== this.padding) {
                // Measured again with it first; then grown from what fits (the library may not report an unchanged row).
                this.setPadding(floor);
                queueMicrotask(() => this.fitGaps());
                return;
            }
        }
        const snapshot = this.manager.getSnapshot();
        const shown = [...this.items.values()].filter(item => snapshot.itemVisibility[item.id] !== false && item.element.offsetWidth > 0).length;
        const padding = Math.max(this.padding, room(shown + (snapshot.invisibleItemCount ? 1 : 0)));
        if (padding !== this.padding)
            this.setPadding(padding);
    }

    private setPadding(padding: number): void {
        this.padding = padding;
        this.manager?.setOptions({ padding });
        this.manager?.update();     // setOptions doesn't for a padding of 0
    }
}

/**
 * A child of a `[sfeOverflowRow]`: `[sfeOverflowItem]="entry"`, `entry` giving what it is in the "⋯" menu; null for
 * one that leaves without an entry (the length after a time: it only shortens the text).
 */
@Directive({ selector: '[sfeOverflowItem]' })
export class OverflowItemDirective implements OnInit, OnDestroy {
    /** Its entry in the "⋯" menu, asked for when the menu opens (current labels, states and rights). */
    readonly sfeOverflowItem = input.required<(() => MenuEntry) | null>();
    /** Never leaves the row. Read once, when it is added. */
    readonly overflowStay = input(false, { transform: booleanAttribute });
    /** Lower leaves first; equal ones from the right. Read once, when it is added. */
    readonly overflowPriority = input(0);
    readonly id = `item-${++nextId}`;
    readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private readonly row = inject(OverflowRowDirective);

    ngOnInit(): void {
        this.row.add(this);
    }

    ngOnDestroy(): void {
        this.row.remove(this);
    }
}

/** The row's "⋯" button (its last child): there only while some buttons left the row; opens them as a menu. */
@Component({
    selector: 'button[sfeOverflowMore]',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        type: 'button',
        class: 'overflow-more',
        title: 'More actions',
        'aria-label': 'More actions',
        'aria-haspopup': 'menu',
        '[class.shown]': 'row.hasMenu()',
        // Kept in the row while nothing left, unseen, so its width is known before it is needed.
        '[attr.aria-hidden]': 'row.hasMenu() ? null : "true"',
        '[tabIndex]': 'row.hasMenu() ? 0 : -1',
        '(click)': 'toggle($event)',
    },
    template: `<svg sfeIcon="more-horizontal"></svg>`,
})
export class OverflowMoreComponent implements OnInit, OnDestroy {
    protected readonly row = inject(OverflowRowDirective);
    private readonly contextMenu = inject(ContextMenu);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    ngOnInit(): void {
        this.row.setMore(this.host);
    }

    ngOnDestroy(): void {
        this.row.setMore(null);
    }

    protected toggle(event: MouseEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.contextMenu.toggle(this.row.target(), this.host);
    }
}
