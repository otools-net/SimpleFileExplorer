import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate } from '../core/format';
import { icu } from '../core/i18n/icu';
import { KeepAliveFolder, KeepAliveView } from './admin.models';
import { ToastService } from '../core/toast.service';
import { visibleInterval } from '../core/visible-interval';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';

const REFRESH_MS = 10_000;

/** The dot's color: `idle` (green) working, `unavailable` (red) failing, none (grey) off or not pinged yet. */
type State = 'idle' | 'unavailable' | null;

/**
 * Keep alive › Status on the administration page (right `settings.keepAlive`): the mode in effect, the library folder
 * then the more folders of the settings with their last ping (and whether a file was written), and Ping now (also
 * while it is off, to try a share). Refreshed while shown.
 */
@Component({
    selector: 'sfe-keep-alive-status',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'keep-alive-status' },
    templateUrl: './keep-alive-status.component.html',
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
        if (!view || view.mode === 'off') return null;
        if (view.folders.some((f) => f.error)) return 'unavailable';
        return view.folders.some((f) => f.lastPing) ? 'idle' : null;
    });

    protected readonly summary = computed(() => {
        const error = this.error();
        if (error) return $localize`Could not read the status: ${error}:reason:`;
        const view = this.view();
        if (!view) return '…';
        const seconds = view.intervalSeconds;
        const failing = view.folders.filter((f) => f.error).length;
        if (view.mode === 'off') return $localize`Off: the library folder isn't pinged. Turn it on in Settings.`;
        if (failing)
            return icu($localize`{failing, plural, one {# folder not answering} other {# folders not answering}}`, {
                failing,
            });
        return view.mode === 'auto'
            ? icu(
                  $localize`On for folders on a network share, every {seconds, plural, one {# second} other \
{# seconds}}.`,
                  { seconds },
              )
            : icu($localize`On, every {seconds, plural, one {# second} other {# seconds}}.`, { seconds });
    });

    constructor() {
        void this.refresh();
        inject(DestroyRef).onDestroy(visibleInterval(() => this.refresh(), REFRESH_MS));
    }

    protected network(folder: KeepAliveFolder): string {
        if (folder.isNetwork === null) return $localize`Not checked yet`;
        if (folder.isNetwork) return $localize`On a network share`;
        return this.view()?.mode === 'auto'
            ? $localize`Not on a network share, so not pinged (choose “Always” to ping it anyway)`
            : $localize`Not on a network share`;
    }

    protected failure(folder: KeepAliveFolder): string {
        const error = folder.error ?? '';
        return folder.failingSince
            ? $localize`Failing since ${formatDate(folder.failingSince)}:date:. ${error}:reason:`
            : error;
    }

    protected detail(folder: KeepAliveFolder): string {
        if (folder.waiting) return $localize`Waiting for an answer`;
        if (!folder.lastPing) return '';
        const parts = [$localize`Last ping ${formatDate(folder.lastPing)}:date:`];
        if (folder.durationMilliseconds !== null) {
            const ms = Math.round(folder.durationMilliseconds).toLocaleString();
            parts.push($localize`:duration in milliseconds:${ms}:ms: ms`);
        }
        if (folder.method === 'touch') parts.push($localize`file written`);
        return parts.join(' · ');
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
