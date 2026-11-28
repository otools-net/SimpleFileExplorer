import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { UsableConnection } from '../../core/connection.models';
import { UsableConnections } from '../../core/usable-connections';
import { IconComponent } from '../../shared/icon.component';

/**
 * The dashboard's Connections: those the session may use in its space, each opening its files, with its state (to
 * sign in to again) and whether the session may change its files. Read once when the dashboard opens.
 */
@Component({
    selector: 'sfe-connections-widget',
    imports: [RouterLink, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'connections-widget' },
    template: `
        @if (loaded()) {
            <ul class="quick-link-list">
                @for (connection of connections.list(); track connection.id) {
                    <li class="quick-link" [attr.data-connection]="connection.id">
                        <a class="quick-link-target" routerLink="/" [queryParams]="{ c: connection.id }"
                           [title]="hint(connection)">
                            <svg [sfeIcon]="kindIcon(connection)"></svg>
                            <span class="quick-link-name">{{ connection.name }}</span>
                            <span class="dashboard-card-note dashboard-state"
                                  [class.warning]="connection.state === 'needsReconnecting'">{{
                                state(connection)
                            }}</span>
                        </a>
                    </li>
                }
            </ul>
            @if (!connections.list().length) {
                <p class="dashboard-card-note" i18n>No connections you may use in this space.</p>
            }
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class ConnectionsWidgetComponent implements OnInit {
    protected readonly connections = inject(UsableConnections);
    private readonly config = inject(ConfigService);

    protected readonly loaded = signal(false);
    private readonly kinds = computed(() => this.config.config()?.connections?.kinds ?? []);

    async ngOnInit(): Promise<void> {
        await this.connections.load(true);
        this.loaded.set(true);
    }

    protected kindIcon(connection: UsableConnection): string {
        return this.kinds().find((kind) => kind.kind === connection.kind)?.icon ?? 'globe';
    }

    protected state(connection: UsableConnection): string {
        if (connection.state === 'needsReconnecting') return $localize`:a connection's state:sign in again`;
        return connection.access === 'write'
            ? $localize`:a connection's state:read and change`
            : $localize`:a connection's state:read only`;
    }

    protected hint(connection: UsableConnection): string {
        const kind = this.kinds().find((candidate) => candidate.kind === connection.kind)?.name ?? connection.kind;
        const name = connection.name;
        if (connection.state === 'needsReconnecting')
            return $localize`${name}:connection: (${kind}:kind:): sign in to it again to use it.`;
        return $localize`Open the files of ${name}:connection: (${kind}:kind:).`;
    }
}
