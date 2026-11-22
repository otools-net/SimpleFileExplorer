import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { joinParts } from '../core/format';
import { TrickplayStatus } from './trickplay.models';
import { lastCheck, passCounts, waiting, workingOn } from './trickplay-texts';
import { ToastService } from '../core/toast.service';
import { visibleInterval } from '../core/visible-interval';
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
            <button
                type="button"
                class="button small"
                title="Go over the trickplay folders now for videos without trickplay."
                i18n-title
                [sfeBusy]="checking()"
                [disabled]="checking() || cannotCheck()"
                (click)="check()"
                i18n
            >Look for new videos now</button>
            <button
                type="button"
                class="button small"
                title="Show the videos being processed, waiting and finished."
                i18n-title
                (click)="activity.emit()"
                i18n
            >Show activity</button>
        </div>
    `,
})
export class TrickplayStatusComponent {
    readonly activity = output<void>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private stopTimer: (() => void) | null = null;

    protected readonly status = signal<TrickplayStatus | null>(null);
    private readonly error = signal<string | null>(null);
    protected readonly checking = signal(false);
    protected readonly cannotCheck = computed(
        () => this.status()?.state === 'disabled' || this.status()?.state === 'unavailable',
    );

    protected readonly text = computed(() => {
        const error = this.error();
        if (error) return $localize`Could not read the state: ${error}:reason:`;
        const status = this.status();
        if (!status) return '…';
        switch (status.state) {
            case 'disabled':
                return $localize`Turned off.`;
            case 'unavailable':
                return $localize`ffmpeg can't be found.`;
            case 'running':
                return status.current.length ? workingOn(status.current) : $localize`Looking for videos…`;
            default:
                return status.lastPassFinished ? $localize`Up to date.` : $localize`Waiting.`;
        }
    });
    protected readonly detail = computed(() => {
        const status = this.status();
        if (!status || this.error()) return '';
        switch (status.state) {
            case 'disabled':
                return $localize`Tick “Create trickplay” and save to start.`;
            case 'unavailable':
                return $localize`Set its path (Thumbnails:FfmpegPath) in the server's configuration file.`;
            default:
                return joinParts([...passCounts(status), ...waiting(status), lastCheck(status)]);
        }
    });

    constructor() {
        inject(DestroyRef).onDestroy(() => this.stop());
    }

    start(): void {
        if (this.stopTimer) return;
        void this.refresh();
        this.stopTimer = visibleInterval(() => this.refresh(), REFRESH_MS);
    }

    stop(): void {
        this.stopTimer?.();
        this.stopTimer = null;
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
