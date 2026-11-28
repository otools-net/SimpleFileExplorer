import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { ScanStatus } from '../../library/library.models';
import { describeScan } from '../../library/scan/scan.service';
import { formatRelative } from '../../tasks/tasks';

/** How often a running scan's counts are read again while the dashboard shows. */
export const SCAN_POLL_MS = 3000;

/**
 * The dashboard's Index scan: the scan of the space running now, or how the last one ended (since the server
 * started), with its counts; the folder it covers opens its listing, whose panel cancels a running scan. Read when
 * the dashboard opens, then again while a scan runs and the page shows.
 */
@Component({
    selector: 'sfe-scan-widget',
    imports: [RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'scan-widget' },
    template: `
        @if (status(); as status) {
            @if (status.state === 'idle') {
                <p class="dashboard-card-note" i18n>No scan since the server started. Scans start from a folder's
                    menu, or on a schedule (Administration › Background tasks).</p>
            } @else {
                <p class="dashboard-scan-state" [class.warning]="status.state === 'failed'">{{ state() }}</p>
                <a routerLink="/" [queryParams]="{ path: status.path || undefined }" [title]="hint">{{ name() }}</a>
                <span class="dashboard-card-note">{{ counts() }}</span>
                @if (status.state === 'failed' && status.message) {
                    <span class="dashboard-card-note">{{ status.message }}</span>
                }
            }
        } @else if (failed()) {
            <p class="dashboard-card-note" i18n>The scan's progress could not be read.</p>
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class ScanWidgetComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private timer: ReturnType<typeof setTimeout> | undefined;
    private destroyed = false;

    protected readonly status = signal<ScanStatus | null>(null);
    protected readonly failed = signal(false);
    protected readonly hint = $localize`Open this folder: its scan panel shows the progress and cancels the scan.`;

    protected readonly name = computed(() => {
        const path = this.status()?.path;
        const library = this.config.value.rootName;
        return path ? `“${path}”` : $localize`The whole library (${library}:library:)`;
    });
    protected readonly counts = computed(() => {
        const status = this.status();
        return status ? describeScan(status) : '';
    });
    protected readonly state = computed(() => {
        const status = this.status();
        if (!status) return '';
        const ago = status.finishedAt ? formatRelative(status.finishedAt) : '';
        switch (status.state) {
            case 'running':
                return $localize`:a scan's state:Running`;
            case 'completed':
                return $localize`:a scan's state:Completed ${ago}:when:`;
            case 'cancelled':
                return $localize`:a scan's state:Cancelled ${ago}:when:`;
            default:
                return $localize`:a scan's state:Failed ${ago}:when:`;
        }
    });

    constructor() {
        inject(DestroyRef).onDestroy(() => {
            this.destroyed = true;
            clearTimeout(this.timer);
        });
    }

    async ngOnInit(): Promise<void> {
        await this.read();
    }

    private async read(): Promise<void> {
        if (this.destroyed) return;
        try {
            const status = await this.api.scanStatus();
            this.status.set(status);
            this.failed.set(false);
            if (status.state === 'running') this.later();
        } catch {
            this.failed.set(!this.status());
            if (this.status()?.state === 'running') this.later();
        }
    }

    /** The next read of a running scan; while the page is hidden, when it shows again. */
    private later(): void {
        this.timer = setTimeout(() => {
            if (!document.hidden) void this.read();
            else document.addEventListener('visibilitychange', () => void this.read(), { once: true });
        }, SCAN_POLL_MS);
    }
}
