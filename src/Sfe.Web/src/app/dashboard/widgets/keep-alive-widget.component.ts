import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { KeepAliveFolder, KeepAliveView } from '../../admin/admin.models';
import { Api } from '../../core/api.service';
import { icu } from '../../core/i18n/icu';
import { formatRelative } from '../../tasks/tasks';

/**
 * The dashboard's Keep alive: whether the library folder is pinged so its file server keeps the connection, and the
 * folders not answering (with why); the Status page (Administration › Keep alive) has every folder and Ping now.
 * Read once when the dashboard opens.
 */
@Component({
    selector: 'sfe-keep-alive-widget',
    imports: [RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'keep-alive-widget' },
    template: `
        @if (view(); as view) {
            <a routerLink="/admin" [queryParams]="{ section: 'keepAlive', page: 'status' }"
               [class.warning]="failing().length" [title]="hint">{{ summary() }}</a>
            @if (failing().length) {
                <ul class="dashboard-list">
                    @for (folder of failing(); track folder.path) {
                        <li class="dashboard-keep-alive" [attr.data-folder]="folder.path">
                            <span class="quick-link-name">{{ name(folder) }}</span>
                            <span class="dashboard-card-note">{{ folder.error }}</span>
                        </li>
                    }
                </ul>
            } @else if (lastPing(); as lastPing) {
                <span class="dashboard-card-note">{{ lastPing }}</span>
            }
        } @else if (failed()) {
            <p class="dashboard-card-note" i18n>The keep-alive status could not be read.</p>
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class KeepAliveWidgetComponent implements OnInit {
    private readonly api = inject(Api);

    protected readonly view = signal<KeepAliveView | null>(null);
    protected readonly failed = signal(false);
    protected readonly hint = $localize`Open the keep-alive status: every folder's last ping, and Ping now.`;

    protected readonly failing = computed(() => this.view()?.folders.filter((folder) => folder.error) ?? []);

    protected readonly summary = computed(() => {
        const view = this.view();
        if (!view) return '';
        const failing = this.failing().length;
        if (view.mode === 'off') return $localize`Off: the library folder isn't pinged.`;
        if (failing)
            return icu($localize`{failing, plural, one {# folder not answering} other {# folders not answering}}`, {
                failing,
            });
        return view.mode === 'auto'
            ? $localize`On for folders on a network share.`
            : $localize`On: the folders are pinged.`;
    });

    protected readonly lastPing = computed(() => {
        const pings = (this.view()?.folders ?? []).map((folder) => folder.lastPing).filter((ping) => ping !== null);
        if (this.view()?.mode === 'off' || !pings.length) return null;
        const last = pings.reduce((latest, ping) => (Date.parse(ping) > Date.parse(latest) ? ping : latest));
        return $localize`Last ping ${formatRelative(last)}:when:`;
    });

    async ngOnInit(): Promise<void> {
        try {
            this.view.set(await this.api.keepAlive());
        } catch {
            this.failed.set(true);
        }
    }

    protected name(folder: KeepAliveFolder): string {
        return folder.isRoot ? $localize`Library folder` : folder.path;
    }
}
