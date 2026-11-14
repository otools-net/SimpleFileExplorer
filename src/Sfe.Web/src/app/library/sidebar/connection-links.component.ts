import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { UsableConnection } from '../../core/connection.models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { ListingStore } from '../listing.store';
import { SidebarService } from './sidebar.service';

/**
 * The sidebar's connections: those the session may use in its library (`GET api/connections`), each opening its
 * files in the listing (`?c=`). Hidden without the right or without any.
 */
@Component({
    selector: 'section[sfeConnectionLinks]',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'sidebar-section connection-links', '[hidden]': '!connections().length' },
    template: `
        <div class="sidebar-heading"><h2>Connections</h2></div>
        <ul class="quick-link-list">
            @for (connection of connections(); track connection.id) {
                <li class="quick-link" [class.current]="store.route().c === connection.id">
                    <a
                        class="quick-link-target"
                        routerLink="/"
                        [queryParams]="{ c: connection.id }"
                        [attr.aria-current]="store.route().c === connection.id ? 'page' : null"
                        [title]="hint(connection)"
                        (click)="sidebar.closeIfOverlay()"
                    >
                        <svg [sfeIcon]="kindIcon(connection)"></svg>
                        <span class="quick-link-name">{{ connection.name }}</span>
                        @if (connection.state === 'needsReconnecting') {
                            <svg sfeIcon="lock" class="connection-warning" aria-label="Sign in again"></svg>
                        }
                    </a>
                </li>
            }
        </ul>
    `,
})
export class ConnectionLinksComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    protected readonly store = inject(ListingStore);
    protected readonly sidebar = inject(SidebarService);
    protected readonly connections = signal<UsableConnection[]>([]);
    private readonly kinds = computed(() => this.config.config()?.connections?.kinds ?? []);

    ngOnInit(): void {
        if (!this.config.config()?.connections || !this.config.can(Rights.useConnections)) return;
        this.api
            .connections()
            .then((view) => this.connections.set(view.usable))
            .catch(() => {
                /* the section stays hidden */
            });
    }

    protected kindIcon(connection: UsableConnection): string {
        return this.kinds().find((kind) => kind.kind === connection.kind)?.icon ?? 'globe';
    }

    protected hint(connection: UsableConnection): string {
        const kind = this.kinds().find((candidate) => candidate.kind === connection.kind)?.name ?? connection.kind;
        const access = connection.access === 'write' ? 'reads and changes' : 'reads only';
        return connection.state === 'needsReconnecting'
            ? `${connection.name} (${kind}): it must be signed in to again`
            : `${connection.name} (${kind}, ${access})`;
    }
}
