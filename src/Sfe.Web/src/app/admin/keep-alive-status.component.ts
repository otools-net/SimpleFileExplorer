import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, plural } from '../core/format';
import { KeepAliveFolder, KeepAliveView } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';

const REFRESH_MS = 10_000;

/** The dot's color: `idle` (green) working, `unavailable` (red) failing, none (grey) off or not pinged yet. */
type State = 'idle' | 'unavailable' | null;

/**
 * Keep alive › Status on the administration page (right `settings.keepAlive`): the mode in effect, each folder kept
 * alive with its last ping, and Ping now (also while it is off, to try a share). Refreshed while shown.
 */
@Component({
    selector: 'sfe-keep-alive-status',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'keep-alive-status' },
    template: `
        <div class="settings-status" aria-live="polite" [attr.data-state]="state()">
            <div class="settings-status-line"><span class="settings-status-dot"></span><span class="settings-status-text">{{ summary() }}</span></div>
            <div class="settings-action inline">
                <button type="button" class="button small" title="Ping the library folder now, also while keep alive is off, to try the share"
                        [sfeBusy]="pinging()" [disabled]="pinging()" (click)="ping()"><svg sfeIcon="refresh"></svg>Ping now</button>
                <button type="button" class="button small" title="Turn keep alive on or off, and change how often it pings" (click)="openSettings.emit()">
                    <svg sfeIcon="settings"></svg>Settings</button>
            </div>
        </div>
        <ul class="keep-alive-list">
            @for (folder of view()?.folders ?? []; track folder.path) {
                <li class="keep-alive-row" [class.failed]="folder.error" [attr.data-path]="folder.path">
                    <svg [sfeIcon]="folder.error ? 'close' : folder.lastPing ? 'check' : 'clock'"></svg>
                    <span class="keep-alive-folder">
                        <span class="keep-alive-name">{{ folder.path || 'Library folder' }}</span>
                        <span class="keep-alive-network">{{ network(folder) }}</span>
                        @if (folder.error) {
                            <span class="keep-alive-error">{{ failure(folder) }}</span>
                        }
                    </span>
                    <span class="keep-alive-detail">{{ detail(folder) }}</span>
                </li>
            } @empty {
                <li class="muted">No ping since the server started{{ view()?.mode === 'off' ? ': keep alive is off. Ping now tries the library folder once.' : '.' }}</li>
            }
        </ul>
    `,
})
export class KeepAliveStatusComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    readonly openSettings = output<void>();

    protected readonly view = signal<KeepAliveView | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly pinging = signal(false);

    protected readonly state = computed((): State => {
        const view = this.view();
        if (!view || view.mode === 'off')
            return null;
        if (view.folders.some(f => f.error))
            return 'unavailable';
        return view.folders.some(f => f.lastPing) ? 'idle' : null;
    });

    protected readonly summary = computed(() => {
        const error = this.error();
        if (error)
            return `Could not read the status: ${error}`;
        const view = this.view();
        if (!view)
            return '…';
        const every = `every ${plural(view.intervalSeconds, 'second')}`;
        const failing = view.folders.filter(f => f.error).length;
        switch (view.mode) {
            case 'off': return 'Off: the library folder is not pinged. Turn it on in Settings.';
            case 'auto': return failing ? `${plural(failing, 'folder')} not answering` : `On for folders on a network share, ${every}.`;
            default: return failing ? `${plural(failing, 'folder')} not answering` : `On, ${every}.`;
        }
    });

    constructor() {
        void this.refresh();
        const timer = setInterval(() => this.refresh(), REFRESH_MS);
        inject(DestroyRef).onDestroy(() => clearInterval(timer));
    }

    protected network(folder: KeepAliveFolder): string {
        if (folder.isNetwork === null)
            return 'Not looked at yet';
        if (folder.isNetwork)
            return 'On a network share';
        return this.view()?.mode === 'auto' ? 'Not on a network share: not pinged (choose “Always” to ping it anyway)' : 'Not on a network share';
    }

    protected failure(folder: KeepAliveFolder): string {
        const since = folder.failingSince ? `Failing since ${formatDate(folder.failingSince)}: ` : '';
        return since + (folder.error ?? '');
    }

    protected detail(folder: KeepAliveFolder): string {
        if (folder.waiting)
            return 'waiting for an answer';
        if (!folder.lastPing)
            return '';
        const took = folder.durationMilliseconds !== null ? ` · ${Math.round(folder.durationMilliseconds).toLocaleString()} ms` : '';
        return `last ping ${formatDate(folder.lastPing)}${took}`;
    }

    protected async ping(): Promise<void> {
        try {
            this.view.set(await whileBusy(this.pinging, () => this.api.pingKeepAlive()));
            this.error.set(null);
        } catch (error) {
            this.toasts.error(`Could not ping: ${(error as Error).message}`);
        }
    }

    async refresh(): Promise<void> {
        try {
            this.view.set(await this.api.keepAlive());
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }
}
