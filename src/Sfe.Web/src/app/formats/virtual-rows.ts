import { signal } from '@angular/core';
import { elementScroll, observeElementOffset, observeElementRect, VirtualItem, Virtualizer, VirtualizerOptions } from '@tanstack/virtual-core';

/** Before the element is measured (and in jsdom, where everything measures 0): this tall. */
const FALLBACK_HEIGHT = 600;

/**
 * Rows of equal height drawn only where they show (`@tanstack/virtual-core`), as signals for a template: `items`
 * (index, start in px) and `total` (the height of all). Only for the views of `formats/` (lazy chunks): not in the
 * initial bundle.
 */
export class VirtualRows {
    readonly items = signal<VirtualItem[]>([]);
    readonly total = signal(0);
    private readonly virtualizer: Virtualizer<HTMLElement, Element>;
    private options: VirtualizerOptions<HTMLElement, Element>;
    private unmount: (() => void) | null = null;

    constructor(getScrollElement: () => HTMLElement | null, private rowHeight = 28, overscan = 12) {
        this.options = {
            count: 0,
            getScrollElement,
            estimateSize: () => this.rowHeight,
            overscan,
            scrollToFn: elementScroll,
            observeElementOffset,
            observeElementRect: (instance, notify) =>
                observeElementRect(instance, rect => notify(rect.height ? rect : { width: rect.width, height: FALLBACK_HEIGHT })),
            initialRect: { width: 0, height: FALLBACK_HEIGHT },
            onChange: instance => this.publish(instance),
        };
        this.virtualizer = new Virtualizer(this.options);
    }

    /** Starts following the scroll element (once it is in the page). */
    mount(): void {
        if (this.unmount)
            return;
        this.unmount = this.virtualizer._didMount();
        this.virtualizer._willUpdate();
        this.publish(this.virtualizer);
    }

    /** How many rows there are now. */
    setCount(count: number): void {
        this.update({ count });
    }

    /** The rows' height changed (font, size): measured again. */
    setRowHeight(height: number): void {
        if (height <= 0 || height === this.rowHeight)
            return;
        this.rowHeight = height;
        this.virtualizer.measure();
        this.publish(this.virtualizer);
    }

    get height(): number {
        return this.rowHeight;
    }

    /** Scrolls so that row `index` shows (`auto`: only when it doesn't). */
    scrollTo(index: number, align: 'auto' | 'start' | 'center' = 'auto'): void {
        this.virtualizer.scrollToIndex(index, { align });
    }

    destroy(): void {
        this.unmount?.();
        this.unmount = null;
    }

    private update(changes: Partial<VirtualizerOptions<HTMLElement, Element>>): void {
        this.options = { ...this.options, ...changes };
        this.virtualizer.setOptions(this.options);
        this.virtualizer._willUpdate();
        this.publish(this.virtualizer);
    }

    private publish(instance: Virtualizer<HTMLElement, Element>): void {
        this.items.set(instance.getVirtualItems());
        this.total.set(instance.getTotalSize());
    }
}
