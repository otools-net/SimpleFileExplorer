import { computed, inject, Injectable, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { joinParts, plural } from '../../core/format';
import { ScanStatus } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { ToastService } from '../../core/toast.service';
import { ListingStore } from '../listing.store';

const DISMISSED_KEY = 'sfe.scan.dismissed';

/** Counts of a scan, and the folder it is in. */
export function describeScan(status: ScanStatus): string {
    const counts = joinParts([
        `${plural(status.directoriesScanned, 'folder')} scanned`,
        status.state === 'running' && status.directoriesQueued ? `${status.directoriesQueued.toLocaleString()} waiting` : null,
        `${plural(status.filesIndexed, 'file')} indexed`,
        status.removed ? `${status.removed.toLocaleString()} removed` : null,
        status.relinked ? `${plural(status.relinked, 'moved file')} relinked` : null,
        status.missing ? `${plural(status.missing, 'tagged file')} missing` : null,
        status.errors ? plural(status.errors, 'error') : null,
    ]);
    return status.currentDirectory ? `${counts} — ${status.currentDirectory}` : counts;
}

/**
 * The scan of the index, which runs on the server: its status is read from GET /api/scan when the page opens, so
 * a refresh (or another tab or device) shows a running scan too, then polled while it runs.
 */
@Injectable({ providedIn: 'root' })
export class ScanService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private timer = 0;
    private started = false;

    readonly status = signal<ScanStatus | null>(null);
    readonly running = computed(() => this.status()?.state === 'running');
    /** `startedAt` of the finished scan whose panel was dismissed (remembered per browser). */
    readonly dismissed = signal(readPreference(DISMISSED_KEY));
    private readonly starting = signal(false);

    /** The panel shows a scan running, or the result of the last one until dismissed. */
    readonly visible = computed(() => {
        const status = this.status();
        return Boolean(status && status.state !== 'idle' && (status.state === 'running' || this.dismissed() !== status.startedAt));
    });
    readonly busy = computed(() => this.running() || this.starting());

    /**
     * Picks up a scan that is running (started before a refresh, or from another tab or device), and keeps checking
     * now and then for scans started elsewhere while the page stays open.
     */
    watch(): void {
        if (this.started)
            return;
        this.started = true;
        void this.poll();
        setInterval(() => {
            if (!this.running() && !document.hidden)
                void this.poll({ quiet: true });
        }, 30000);
    }

    /** Asks first, then scans the folder browsed and its sub-folders. */
    async start(): Promise<void> {
        if (this.busy())
            return;
        const path = this.store.route().path;
        const name = path ? `“${path}”` : `the whole library (${this.config.value.rootName})`;
        // A folder kept out of scans: nothing to do there (the server refuses it too).
        const listing = this.store.listing();
        const through = listing && !this.store.searching() && 'exclusions' in listing ? listing.exclusions?.scan : null;
        if (through) {
            this.toasts.show(through === listing!.path
                ? `${name} is excluded from scans: what the index has of it stays as it is.`
                : `${name} is inside “${through}”, which is excluded from scans.`);
            return;
        }
        if (!confirm(`Scan ${name} and all its sub-folders?\n\nThe search index is updated in the background. This can take a while on large folders.${this.skippedNote()}`))
            return;

        this.starting.set(true);
        try {
            const status = await this.api.startScan(path);
            if (status.state === 'running' && (status.path ?? '') !== path)
                this.toasts.show(`Another scan is already running (${status.path || 'library'}).`);
            this.status.set(status);
            void this.poll();
        } catch (error) {
            this.toasts.error(`Scan failed to start: ${(error as Error).message}`);
        } finally {
            this.starting.set(false);
        }
    }

    /** The folders listed here that scans skip, for the confirmation: '' when none. */
    private skippedNote(): string {
        const skipped = this.store.searching() ? [] : this.store.items().filter(item => item.kind === 'directory' && item.excluded?.scan).map(item => `“${item.name}”`);
        if (!skipped.length)
            return '';
        const names = skipped.length > 3 ? `${skipped.slice(0, 3).join(', ')} and ${skipped.length - 3} more` : skipped.join(', ');
        return `\n\nExcluded from scans: ${names}. ${skipped.length === 1 ? 'It is' : 'They are'} skipped, and what the index has of ${skipped.length === 1 ? 'it' : 'them'} stays as it is.`;
    }

    async cancel(): Promise<void> {
        if (!confirm('Cancel the scan? Folders already scanned stay indexed.'))
            return;
        await this.api.cancelScan().catch(() => { });
        void this.poll();
    }

    dismiss(): void {
        const startedAt = this.status()?.startedAt ?? '';
        this.dismissed.set(startedAt);
        writePreference(DISMISSED_KEY, startedAt);
    }

    private async poll({ quiet = false } = {}): Promise<void> {
        clearTimeout(this.timer);
        const wasRunning = this.running();
        try {
            const status = await this.api.scanStatus();
            this.status.set(status);
            if (status.state === 'running')
                this.timer = setTimeout(() => this.poll(), 1000);
            else if (wasRunning && status.state === 'completed')
                this.toasts.show(`Index updated: ${describeScan(status)}`);
        } catch (error) {
            if (!quiet)
                this.toasts.error(`Cannot read scan status: ${(error as Error).message}`);
            // Keep watching a running scan through a hiccup (e.g. the server restarting).
            if (wasRunning)
                this.timer = setTimeout(() => this.poll({ quiet: true }), 5000);
        }
    }
}
