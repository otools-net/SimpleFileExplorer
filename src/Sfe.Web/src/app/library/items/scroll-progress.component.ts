import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, signal } from '@angular/core';
import { ListingStore } from '../listing.store';
import { countAbove, itemContainers } from './listing-position';

/** Scrolled less than this (px), the listing's start is in view: no need to say where it is. */
const SHOW_AFTER = 200;

/**
 * Where the listing is scrolled, in a corner: the page of the items at the bottom of the screen, and how many items
 * have been scrolled to out of the folder's (or the search's) total, with a bar. Lives in `sfe-listing`, whose
 * folders and files it measures.
 */
@Component({
    selector: 'sfe-scroll-progress',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'scroll-progress', 'aria-hidden': 'true', '[hidden]': '!visible()' },
    template: `
        <span class="scroll-progress-text">
            <span>{{ pageText() }}</span>
            <span class="scroll-progress-count">{{ countText() }}</span>
        </span>
        <span class="scroll-progress-bar"><span [style.width.%]="percent()"></span></span>
    `,
})
export class ScrollProgressComponent {
    private readonly store = inject(ListingStore);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    /** Items whose top is above the bottom of the screen: those scrolled to so far. */
    protected readonly seen = signal(0);
    private readonly scrolled = signal(false);
    private frame = 0;

    /** The folder's items, or the search's matches; `more`: at least that many. */
    private readonly total = computed(() => {
        const total = this.store.total();
        return total ? { count: total.count, more: total.capped } : { count: this.store.items().length, more: this.store.hasMore() };
    });

    protected readonly visible = computed(() => this.scrolled() && this.store.status() === 'ready' && this.seen() > 0);
    protected readonly pageText = computed(() => {
        const size = this.store.pageSize();
        const { count, more } = this.total();
        const page = Math.max(1, Math.ceil(this.seen() / size));
        return `Page ${page.toLocaleString()} of ${Math.max(page, Math.ceil(count / size)).toLocaleString()}${more ? '+' : ''}`;
    });
    protected readonly countText = computed(() => {
        const { count, more } = this.total();
        return `${this.seen().toLocaleString()} / ${count.toLocaleString()}${more ? '+' : ''}`;
    });
    protected readonly percent = computed(() => Math.min(100, 100 * this.seen() / Math.max(1, this.total().count)));

    constructor() {
        const schedule = () => this.frame ||= requestAnimationFrame(() => this.measure());
        window.addEventListener('scroll', schedule, { passive: true });
        window.addEventListener('resize', schedule, { passive: true });
        inject(DestroyRef).onDestroy(() => {
            window.removeEventListener('scroll', schedule);
            window.removeEventListener('resize', schedule);
            cancelAnimationFrame(this.frame);
        });
        // Items added or a new listing: measured once drawn.
        const injector = inject(Injector);
        effect(() => {
            this.store.items();
            this.store.status();
            this.store.view();
            afterNextRender(schedule, { injector });
        });
    }

    private measure(): void {
        this.frame = 0;
        this.scrolled.set(window.scrollY > SHOW_AFTER);
        const bottom = window.innerHeight;
        let seen = 0;
        for (const container of itemContainers(this.host.parentElement ?? undefined))
            seen += countAbove(container, bottom);
        this.seen.set(seen);
    }
}
