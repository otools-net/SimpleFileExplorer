import { ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { formatElapsed } from '../../core/format';
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
    /** Ticks the elapsed time between polls. */
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
        const clock = setInterval(() => {
            if (this.scan.running())
                this.now.set(Date.now());
        }, 1000);
        const destroyRef = inject(DestroyRef);
        destroyRef.onDestroy(() => clearInterval(clock));
        this.scan.watch();

        // The panel sticks under the top bar: its height (0 while hidden) puts the compact location bar below it.
        if (typeof ResizeObserver !== 'undefined') {
            const root = document.documentElement.style;
            const observer = new ResizeObserver(([entry]) =>
                root.setProperty('--scan-panel-h', `${entry.borderBoxSize?.[0]?.blockSize ?? entry.contentRect.height}px`));
            observer.observe(inject(ElementRef<HTMLElement>).nativeElement);
            destroyRef.onDestroy(() => {
                observer.disconnect();
                root.removeProperty('--scan-panel-h');
            });
        }
    }
}
