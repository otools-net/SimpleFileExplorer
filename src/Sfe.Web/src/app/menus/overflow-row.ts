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
export class OverflowRowDirective {
    /** What the row is ("Player controls"): the menu's title on phones (a sheet). */
    readonly sfeOverflowRow = input.required<string>();
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

    constructor() {
        // No ResizeObserver (jsdom): nothing leaves.
        const supported = typeof window.ResizeObserver === 'function';
        this.manager = supported ? createOverflowManager({
            padding: 0,
            onUpdateItemVisibility: ({ item, visible }) => item.element.toggleAttribute('data-overflowed', !visible),
            onUpdateOverflow: ({ invisibleItems }) => this.hidden.set(invisibleItems
                .map(item => item.element)
                .sort((a, b) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1)
                .map(element => [...this.items.values()].find(item => item.element === element)!)
                .filter(Boolean)),
        }) : null;
        this.manager?.observe(this.element);
        this.resizes = supported ? new ResizeObserver(() => this.manager?.update()) : null;
        inject(DestroyRef).onDestroy(() => {
            this.resizes?.disconnect();
            this.manager?.disconnect();
            this.contextMenu.closeIf(this.key);
        });
    }

    add(item: OverflowItemDirective): void {
        this.items.set(item.id, item);
        this.manager?.addItem({ id: item.id, element: item.element, priority: item.overflowPriority(), pinned: item.overflowStay() });
        this.resizes?.observe(item.element);
        this.updateGaps();
    }

    remove(item: OverflowItemDirective): void {
        this.items.delete(item.id);
        this.resizes?.unobserve(item.element);
        this.manager?.removeItem(item.id);
        this.updateGaps();
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
     * The library adds up the items' widths, not the gaps between them: they are kept as padding (one per item but the
     * first; with the "⋯" shown, up to a gap per item gone too many, so one may leave a few pixels early).
     */
    private updateGaps(): void {
        const gap = parseFloat(getComputedStyle(this.element).columnGap) || 0;
        this.manager?.setOptions({ padding: gap * Math.max(this.items.size - 1, 0) });
        this.manager?.update();
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
