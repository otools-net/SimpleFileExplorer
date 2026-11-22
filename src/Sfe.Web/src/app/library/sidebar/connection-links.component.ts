import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { UsableConnection } from '../../core/connection.models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { FileTransfers } from '../../files/file-transfers';
import { parseDragItems } from '../../files/drag-items';
import { ListingStore } from '../listing.store';
import { DRAG_ITEMS } from '../quick-links/quick-links.service';
import { SidebarService } from './sidebar.service';

/**
 * The sidebar's connections: those the session may use in its library (`GET api/connections`), each opening its
 * files in the listing (`?c=`). Items dragged onto one it writes to go to its root: a move, a copy with Ctrl (as on
 * the folders of the listing). Hidden without the right or without any.
 */
@Component({
    selector: 'section[sfeConnectionLinks]',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'sidebar-section connection-links', '[hidden]': '!connections().length' },
    template: `
        <div class="sidebar-heading"><h2 i18n>Connections</h2></div>
        <ul class="quick-link-list">
            @for (connection of connections(); track connection.id) {
                <li
                    class="quick-link"
                    [class.current]="store.route().c === connection.id"
                    [class.drop-over]="over() === connection.id"
                    (dragover)="dragOver($event, connection)"
                    (dragleave)="over.set(null)"
                    (drop)="drop($event, connection)"
                >
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
                            <svg sfeIcon="lock" class="connection-warning" aria-label="Sign in again"
                                 i18n-aria-label></svg>
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
    private readonly transfers = inject(FileTransfers);
    protected readonly connections = signal<UsableConnection[]>([]);
    /** The connection under the pointer during a drag of items it takes. */
    protected readonly over = signal<number | null>(null);
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

    protected dragOver(event: DragEvent, connection: UsableConnection): void {
        if (!takes(event, connection)) return;
        event.preventDefault();
        event.dataTransfer!.dropEffect = copying(event) ? 'copy' : 'move';
        this.over.set(connection.id);
    }

    /** Items dropped: moved (copied with Ctrl) to the connection's root, from wherever they are. */
    protected drop(event: DragEvent, connection: UsableConnection): void {
        this.over.set(null);
        if (!takes(event, connection)) return;
        const dragged = parseDragItems(event.dataTransfer!.getData(DRAG_ITEMS));
        if (!dragged.paths.length) return;
        event.preventDefault();
        const places = { from: dragged.connection, to: connection.id };
        void this.transfers.transfer(copying(event) ? 'copy' : 'move', dragged.paths, '', places);
    }

    protected kindIcon(connection: UsableConnection): string {
        return this.kinds().find((kind) => kind.kind === connection.kind)?.icon ?? 'globe';
    }

    protected hint(connection: UsableConnection): string {
        const kind = this.kinds().find((candidate) => candidate.kind === connection.kind)?.name ?? connection.kind;
        const name = connection.name;
        if (connection.state === 'needsReconnecting')
            return $localize`${name}:connection: (${kind}:kind:): sign in to it again to use it.`;
        return connection.access === 'write'
            ? $localize`${name}:connection: (${kind}:kind:): you can read and change its files.`
            : $localize`${name}:connection: (${kind}:kind:): you can read its files.`;
    }
}

/** Items of the page are dragged onto a connection the session writes to (and may use now). */
function takes(event: DragEvent, connection: UsableConnection): boolean {
    return (
        Boolean(event.dataTransfer?.types.includes(DRAG_ITEMS)) &&
        connection.access === 'write' &&
        connection.state === 'ready'
    );
}

function copying(event: DragEvent): boolean {
    return event.ctrlKey || event.metaKey;
}
