import { computed, inject, Injectable, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { joinParts } from '../../core/format';
import { icu } from '../../core/i18n/icu';
import { ScanStatus } from '../library.models';
import { readPreference, writePreference } from '../../core/preferences';
import { ToastService } from '../../core/toast.service';
import { ListingStore } from '../listing.store';
import { excludedFromScans, otherScanRunning, scanName, scanQuestion } from './scan-texts';

const DISMISSED_KEY = 'sfe.scan.dismissed';

/** Counts of a scan, and the folder it is in. */
export function describeScan(status: ScanStatus): string {
    const count = (message: string, value: number) => icu(message, { count: value });
    const counts = joinParts([
        count($localize`{count, plural, one {# folder scanned} other {# folders scanned}}`, status.directoriesScanned),
        status.state === 'running' && status.directoriesQueued
            ? count($localize`{count, plural, other {# waiting}}`, status.directoriesQueued)
            : null,
        count($localize`{count, plural, one {# file indexed} other {# files indexed}}`, status.filesIndexed),
        status.removed ? count($localize`{count, plural, other {# removed}}`, status.removed) : null,
        status.relinked
            ? count(
                  $localize`{count, plural, one {# moved file relinked} other {# moved files relinked}}`,
                  status.relinked,
              )
            : null,
        status.missing
            ? count(
                  $localize`{count, plural, one {# tagged file missing} other {# tagged files missing}}`,
                  status.missing,
              )
            : null,
        status.errors ? count($localize`{count, plural, one {# error} other {# errors}}`, status.errors) : null,
    ]);
    return status.currentDirectory ? `${counts} — ${status.currentDirectory}` : counts;
}

/**
 * The scan of the index, which runs on the server: its status is read from GET /api/scan when the page opens, so
 * a refresh (or another tab or device) shows a running scan too, then polled while it runs, and while the page shows:
 * a phone keeping the tab in the background through a scan of hours would otherwise ask every second.
 */
@Injectable({ providedIn: 'root' })
export class ScanService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private timer = 0;
    private started = false;
    /** A poll of a running scan skipped while the page was hidden: made when it shows. */
    private waiting = false;

    readonly status = signal<ScanStatus | null>(null);
    readonly running = computed(() => this.status()?.state === 'running');
    /** `startedAt` of the finished scan whose panel was dismissed (remembered per browser). */
    readonly dismissed = signal(readPreference(DISMISSED_KEY));
    private readonly starting = signal(false);

    /** The panel shows a scan running, or the result of the last one until dismissed. */
    readonly visible = computed(() => {
        const status = this.status();
        return Boolean(
            status && status.state !== 'idle' && (status.state === 'running' || this.dismissed() !== status.startedAt),
        );
    });
    readonly busy = computed(() => this.running() || this.starting());

    constructor() {
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && this.waiting) void this.poll({ quiet: true });
        });
    }

    /**
     * Picks up a scan that is running (started before a refresh, or from another tab or device), and keeps checking
     * now and then for scans started elsewhere while the page stays open.
     */
    watch(): void {
        if (this.started) return;
        this.started = true;
        void this.poll();
        setInterval(() => {
            if (!this.running() && !document.hidden) void this.poll({ quiet: true });
        }, 30000);
    }

    /** Asks first, then scans the folder browsed and its sub-folders. */
    async start(): Promise<void> {
        if (this.busy()) return;
        const path = this.store.route().path;
        const name = scanName(path, this.config.value.rootName);
        // A folder kept out of scans: nothing to do there (the server refuses it too).
        const listing = this.store.listing();
        const through = listing && !this.store.searching() && 'exclusions' in listing ? listing.exclusions?.scan : null;
        if (through) {
            this.toasts.show(excludedFromScans(name, through, through === listing!.path));
            return;
        }
        if (!confirm(scanQuestion(name, this.skipped()))) return;

        this.starting.set(true);
        try {
            const status = await this.api.startScan(path);
            if (status.state === 'running' && (status.path ?? '') !== path)
                this.toasts.show(otherScanRunning(name, status.path ?? ''));
            this.status.set(status);
            void this.poll();
        } catch (error) {
            this.toasts.error($localize`Could not start the scan: ${(error as Error).message}:reason:`);
        } finally {
            this.starting.set(false);
        }
    }

    /** The folders listed here that scans skip, quoted, for the confirmation. */
    private skipped(): string[] {
        return this.store.searching()
            ? []
            : this.store
                  .items()
                  .filter((item) => item.kind === 'directory' && item.excluded?.scan)
                  .map((item) => `“${item.name}”`);
    }

    async cancel(): Promise<void> {
        if (!confirm($localize`Cancel the scan? Folders already scanned stay indexed.`)) return;
        await this.api.cancelScan().catch(() => {});
        void this.poll();
    }

    dismiss(): void {
        const startedAt = this.status()?.startedAt ?? '';
        this.dismissed.set(startedAt);
        writePreference(DISMISSED_KEY, startedAt);
    }

    private async poll({ quiet = false } = {}): Promise<void> {
        clearTimeout(this.timer);
        this.waiting = false;
        const wasRunning = this.running();
        try {
            const status = await this.api.scanStatus();
            this.status.set(status);
            if (status.state === 'running') this.later(() => this.poll(), 1000);
            else if (wasRunning && status.state === 'completed')
                this.toasts.success($localize`Index updated: ${describeScan(status)}:counts:`);
        } catch (error) {
            const reason = (error as Error).message;
            if (!quiet) this.toasts.error($localize`Could not read the scan's progress: ${reason}:reason:`);


            // Keep watching a running scan through a hiccup (e.g. the server restarting).
            if (wasRunning) this.later(() => this.poll({ quiet: true }), 5000);
        }
    }

    /** The next poll of a running scan, or, while the page is hidden, once it shows. */
    private later(poll: () => void, ms: number): void {
        this.timer = setTimeout(() => {
            if (document.hidden) this.waiting = true;
            else poll();
        }, ms);
    }
}
