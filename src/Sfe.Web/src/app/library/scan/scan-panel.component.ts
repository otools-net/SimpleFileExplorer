import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, signal } from '@angular/core';
import { formatElapsed } from '../../core/format';
import { visibleInterval } from '../../core/visible-interval';
import { IconComponent } from '../../shared/icon.component';
import { describeScan, ScanService } from './scan.service';

const TITLES: Record<string, (where: string) => string> = {
    running: where => `Scanning ${where}…`,
    completed: where => `Scan of ${where} finished`,
    cancelled: where => `Scan of ${where} cancelled`,
    failed: where => `Scan of ${where} failed`,
};

/** Progress of the scan under the top bar, then its result until dismissed. */
@Component({
    selector: 'sfe-scan-panel',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'scan-panel',
        'aria-live': 'polite',
        '[hidden]': '!scan.visible()',
        '[class.running]': 'scan.running()',
        '[class.failed]': 'scan.status()?.state === "failed"',
    },
    template: `
        @if (scan.status(); as status) {
            <div class="scan-bar"><span></span></div>
            <div class="scan-text">
                <strong>{{ title() }}</strong>
                <span class="scan-elapsed">{{ elapsed() }}</span>
                <span>{{ status.state === 'failed' && status.message ? status.message : describe(status) }}</span>
            </div>
            @if (scan.running()) {
                <button type="button" class="button small" title="Stop the scan" (click)="scan.cancel()">Cancel</button>
            } @else {
                <button type="button" class="icon-button ghost small" aria-label="Dismiss" title="Hide this panel" (click)="scan.dismiss()"><svg sfeIcon="close"></svg></button>
            }
        }
    `,
})
export class ScanPanelComponent {
    protected readonly scan = inject(ScanService);
    protected readonly describe = describeScan;
    /** Ticks the elapsed time between polls, only while a scan runs and the page shows. */
    private readonly now = signal(Date.now());

    protected readonly title = computed(() => {
        const status = this.scan.status();
        return status ? TITLES[status.state]?.(status.path ? `“${status.path}”` : 'the library') ?? '' : '';
    });
    protected readonly elapsed = computed(() => {
        const status = this.scan.status();
        if (!status?.startedAt)
            return '';
        const end = status.finishedAt ? Date.parse(status.finishedAt) : this.now();
        return formatElapsed((end - Date.parse(status.startedAt)) / 1000);
    });

    constructor() {
        effect(onCleanup => {
            if (this.scan.running())
                onCleanup(visibleInterval(() => this.now.set(Date.now()), 1000));
        });
        const destroyRef = inject(DestroyRef);
        this.scan.watch();

        // The panel sticks under the top bar: its height (0 while hidden) puts the compact location bar below it.
        if (typeof ResizeObserver !== 'undefined') {
            const root = document.documentElement.style;
            const listingTop = () => document.querySelector('sfe-listing')?.getBoundingClientRect().top ?? null;
            let height = 0;
            let top = listingTop();
            const scrolled = () => top = listingTop();
            const panel = inject(ElementRef<HTMLElement>).nativeElement;
            // The panel's size, and the page's (the listing coming in): where the listing is before the panel moves it.
            const observer = new ResizeObserver(entries => {
                const entry = entries.find(each => each.target === panel);
                if (entry) {
                    const next = entry.borderBoxSize?.[0]?.blockSize ?? entry.contentRect.height;
                    root.setProperty('--scan-panel-h', `${next}px`);
                    // Coming in (a scan started elsewhere, found by a poll) or growing, it pushes the listing down under
                    // a finger: the page scrolls by as much, unless the browser already did (scroll anchoring, Chrome
                    // once scrolled), and the panel covers the top instead (e2e selection.spec.ts, a long press).
                    const now = listingTop();
                    if (next > height && top !== null && now !== null && Math.abs(now - top - (next - height)) < 1)
                        scrollBy(0, next - height);
                    height = next;
                }
                top = listingTop();
            });
            observer.observe(panel);
            observer.observe(document.body);
            addEventListener('scroll', scrolled, { passive: true });
            destroyRef.onDestroy(() => {
                observer.disconnect();
                removeEventListener('scroll', scrolled);
                root.removeProperty('--scan-panel-h');
            });
        }
    }
}
