import { Api } from '../core/api.service';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ConnectionLevelName } from '../core/config.models';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { ConnectDialog, ConnectPurpose, problemOf } from './connect.dialog';
import {
    ConnectionChange,
    ConnectionInfo,
    ConnectionsView,
    FolderLevelName,
    LEVEL_NAMES,
    ManagedConnection,
} from '../core/connection.models';
import { ProviderSetupComponent } from './provider-setup.component';

/**
 * Administration › My account › Connections (`mine`: the account's own) and Administration › Connections
 * (`shared`: the space's and the server's, who uses them; `setup`: how each kind is set up). What each one may do
 * comes from the server (`manage`, `remove`): agents/docs/features/connections.md.
 */
@Component({
    selector: 'sfe-connections-section',
    imports: [ConnectDialog, IconComponent, ProviderSetupComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'connections-section' },
    templateUrl: './connections-section.component.html',
})
export class ConnectionsSectionComponent {
    /** `mine`, `shared` or `setup`. */
    readonly tab = input.required<string>();

    private readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    protected readonly view = signal<ConnectionsView | null>(null);
    protected readonly loadError = signal<string | null>(null);
    /** The connection whose users are open below it. */
    protected readonly openId = signal<number | null>(null);
    protected readonly renaming = signal<{ id: number; name: string } | null>(null);

    protected readonly levelNames = LEVEL_NAMES;
    protected readonly value = valueOf;
    protected readonly checked = checkedOf;

    /** The connections of this page. */
    protected readonly shown = computed(() => {
        const me = this.config.user()?.id;
        const managed = this.view()?.managed ?? [];
        return this.tab() === 'mine'
            ? managed.filter((m) => m.connection.level === 'account' && m.connection.ownerId === me)
            : managed.filter((m) => m.connection.level !== 'account' || m.connection.ownerId !== me);
    });
    /** The levels a connection may be added at from this page. */
    protected readonly addable = computed<ConnectionLevelName[]>(() => {
        const levels = this.config.config()?.connections?.levels ?? [];
        const wanted: [ConnectionLevelName, string][] =
            this.tab() === 'mine'
                ? [['account', Rights.ownConnections]]
                : [
                      ['library', Rights.libraryConnections],
                      ['server', Rights.serverConnections],
                  ];
        return wanted.filter(([level, right]) => levels.includes(level) && this.config.can(right)).map(([l]) => l);
    });
    protected readonly enabled = computed(() => !!this.config.config()?.connections);

    constructor() {
        effect(() => {
            if (this.tab() !== 'setup') untracked(() => void this.load());
        });
    }

    async load(): Promise<void> {
        try {
            this.view.set(await this.api.connections());
            this.loadError.set(null);
        } catch (error) {
            this.loadError.set((error as Error).message);
        }
    }

    protected add(dialog: ConnectDialog, level: ConnectionLevelName): void {
        dialog.open({ level } satisfies ConnectPurpose);
    }

    protected reconnect(dialog: ConnectDialog, connection: ConnectionInfo): void {
        dialog.open({ reconnect: connection });
    }

    protected made(connection: ConnectionInfo): void {
        this.toasts.show(`“${connection.name}” is connected.`);
        void this.load();
    }

    protected kindName(kind: string): string {
        return this.config.config()?.connections?.kinds.find((k) => k.kind === kind)?.name ?? kind;
    }

    protected togglePanel(id: number): void {
        this.openId.update((open) => (open === id ? null : id));
    }

    protected startRename(connection: ConnectionInfo): void {
        this.renaming.set({ id: connection.id, name: connection.name });
    }

    protected async saveName(): Promise<void> {
        const renaming = this.renaming();
        if (!renaming) return;
        if (await this.change(renaming.id, { name: renaming.name })) this.renaming.set(null);
    }

    protected async change(id: number, change: ConnectionChange): Promise<boolean> {
        return this.run(() => this.api.updateConnection(id, change), 'Could not change the connection');
    }

    protected async remove(item: ManagedConnection): Promise<void> {
        const name = item.connection.name;
        if (!confirm(`Remove “${name}”? Its files stay where they are; whoever used it here no longer sees it.`))
            return;
        if (await this.run(() => this.api.removeConnection(item.connection.id), 'Could not remove it'))
            this.toasts.show(`“${name}” removed.`);
    }

    /** The level of a group of this space for a connection ('' = none). */
    protected groupLevel(connection: ConnectionInfo, groupId: number): FolderLevelName | '' {
        return connection.groups.find((g) => g.groupId === groupId)?.level ?? '';
    }

    protected async setGroup(connection: ConnectionInfo, groupId: number, level: string): Promise<void> {
        const tenant = this.config.tenant()?.id;
        const groups: Record<number, FolderLevelName> = {};
        for (const group of connection.groups.filter((g) => g.tenantId === tenant)) groups[group.groupId] = group.level;
        if (level) groups[groupId] = level as FolderLevelName;
        else delete groups[groupId];
        await this.run(() => this.api.setConnectionGroups(connection.id, groups), 'Could not change who uses it');
    }

    protected async setLibrary(connection: ConnectionInfo, libraryId: number | 'all', on: boolean): Promise<void> {
        const all = libraryId === 'all' ? on : connection.allTenants;
        const ids = new Set(connection.tenantIds);
        if (libraryId !== 'all') {
            if (on) ids.add(libraryId);
            else ids.delete(libraryId);
        }
        await this.run(
            () => this.api.setConnectionLibraries(connection.id, all, [...ids]),
            'Could not change the spaces it is offered to',
        );
    }

    /** Whether this space may give the connection to its groups (its own, or the server's offered here). */
    protected sharedHere(connection: ConnectionInfo): boolean {
        const tenant = this.config.tenant()?.id;
        if (!this.config.can(Rights.libraryConnections) || tenant === undefined) return false;
        if (connection.level === 'library') return connection.tenantId === tenant;
        return connection.level === 'server' && (connection.allTenants || connection.tenantIds.includes(tenant));
    }

    /** Runs a change and loads the list again; false (and a toast) when it was refused. */
    private async run(request: () => Promise<unknown>, failure: string): Promise<boolean> {
        try {
            const problem = problemOf(await request());
            if (problem) {
                this.toasts.error(`${failure}: ${problem}`);
                return false;
            }
            return true;
        } catch (error) {
            this.toasts.error(`${failure}: ${(error as Error).message}`);
            return false;
        } finally {
            await this.load();
        }
    }
}
