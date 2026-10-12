import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, joinParts, plural } from '../core/format';
import { TrickplayStatus } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';

const REFRESH_MS = 3000;

/**
 * State of the background trickplay job (Trickplay › Settings), refreshed while shown, with a button to look for new
 * videos now and one showing the activity tab (`activity`).
 */
@Component({
    selector: 'sfe-trickplay-status',
    imports: [BusyDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-status', 'aria-live': 'polite', '[attr.data-state]': 'status()?.state' },
    template: `
        <div class="settings-status-line"><span class="settings-status-dot"></span><span class="settings-status-text">{{ text() }}</span></div>
        <span class="field-hint">{{ detail() }}</span>
        <div class="settings-action inline">
            <button type="button" class="button small" title="Go over the trickplay folders now for videos without trickplay"
                    [sfeBusy]="checking()" [disabled]="checking() || cannotCheck()" (click)="check()">Look for new videos now</button>
            <button type="button" class="button small" title="Videos being processed, waiting and finished" (click)="activity.emit()">Show activity</button>
        </div>
    `,
})
export class TrickplayStatusComponent {
    readonly activity = output<void>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private timer = 0;

    protected readonly status = signal<TrickplayStatus | null>(null);
    private readonly error = signal<string | null>(null);
    protected readonly checking = signal(false);
    protected readonly cannotCheck = computed(() => this.status()?.state === 'disabled' || this.status()?.state === 'unavailable');

    protected readonly text = computed(() => {
        const error = this.error();
        if (error)
            return `Could not read the state: ${error}`;
        const status = this.status();
        if (!status)
            return '…';
        switch (status.state) {
            case 'disabled': return 'Turned off.';
            case 'unavailable': return 'ffmpeg can\'t be found.';
            case 'running': return status.current.length ? `Working on ${status.current.join(', ')}` : 'Looking for videos…';
            default: return status.lastPassFinished ? 'Up to date.' : 'Waiting.';
        }
    });
    protected readonly detail = computed(() => {
        const status = this.status();
        if (!status || this.error())
            return '';
        const counts = joinParts([
            plural(status.checked, 'video') + ' checked',
            plural(status.generated, 'created', 'created'),
            status.failed ? plural(status.failed, 'failed', 'failed') : null,
            status.queued ? plural(status.queued, 'opened video', 'opened videos') + ' waiting' : null,
            status.pending ? `${status.pending.toLocaleString()}${status.discovering ? '+' : ''} to do` : null,
        ]);
        const finished = status.lastPassFinished ? `Last complete check ${formatDate(status.lastPassFinished)}` : null;
        switch (status.state) {
            case 'disabled': return 'Tick “Create trickplay” and save to start.';
            case 'unavailable': return 'Set its path (Thumbnails:FfmpegPath) in the server\'s configuration file.';
            case 'running': return joinParts([counts, finished]);
            default: return joinParts([finished, counts]);
        }
    });

    constructor() {
        inject(DestroyRef).onDestroy(() => this.stop());
    }

    start(): void {
        if (this.timer)
            return;
        void this.refresh();
        this.timer = setInterval(() => this.refresh(), REFRESH_MS);
    }

    stop(): void {
        clearInterval(this.timer);
        this.timer = 0;
    }

    async refresh(): Promise<void> {
        try {
            this.status.set(await this.api.trickplayStatus());
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }

    protected async check(): Promise<void> {
        try {
            // Shown once the button is back: off or without ffmpeg, it stays disabled.
            this.status.set(await whileBusy(this.checking, () => this.api.checkTrickplay()));
        } catch (error) {
            this.toasts.error(`Could not start the check: ${(error as Error).message}`);
        }
    }
}
