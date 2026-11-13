import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate } from '../core/format';
import { GroupInfo, UserInfo } from './access.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { AccountEditorComponent } from './account-editor.component';
import { groupNamesIn, SpaceRef } from './access-parts';

/** The spaces of `groups`, in the order of `known` (the first space, then by name), by their names there. */
function spacesOf(groups: GroupInfo[], known: SpaceRef[]): SpaceRef[] {
    const ids = new Set(groups.map((group) => group.tenantId));
    const named = known.filter((space) => ids.has(space.id)).map((space) => ({ id: space.id, name: space.name }));
    const unnamed = [...ids]
        .filter((id) => !named.some((space) => space.id === id))
        .map((id) => ({ id, name: `Space ${id}` }));
    return [...named, ...unnamed];
}

/** An account's groups in one space, for its row. */
interface SpaceLine {
    space: string;
    groups: string[];
}

/**
 * Administration › Spaces & accounts › Accounts: every account of the server with its spaces and its groups in each
 * (server administrators), or the accounts of the session's space (its administrators). Create, change, disable and
 * delete them; the editor puts an account in spaces and groups.
 */
@Component({
    selector: 'sfe-accounts-page',
    imports: [IconComponent, HelpComponent, AccountEditorComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'access-page' },
    templateUrl: './accounts-page.component.html',
})
export class AccountsPageComponent implements OnInit {
    private readonly api = inject(Api);
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly editor = viewChild.required(AccountEditorComponent);

    protected readonly users = signal<UserInfo[]>([]);
    protected readonly groups = signal<GroupInfo[]>([]);
    protected readonly spaces = signal<SpaceRef[]>([]);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly filter = signal('');

    /** Server administrators see and change every account, in every space. */
    protected readonly serverWide = computed(() => this.config.can(Rights.manageUsers));
    protected readonly shown = computed(() => {
        const filter = this.filter().trim().toLocaleLowerCase();
        return filter
            ? this.users().filter((user) =>
                  `${user.userName} ${user.displayName ?? ''}`.toLocaleLowerCase().includes(filter),
              )
            : this.users();
    });

    protected readonly valueOf = valueOf;
    protected readonly formatDate = formatDate;

    ngOnInit(): Promise<void> {
        return this.load();
    }

    async load(): Promise<void> {
        try {
            const serverWide = this.serverWide();
            const [users, groups, spaces] = await Promise.all([
                this.api.users(serverWide),
                this.api.groups(serverWide),
                // Disabled spaces too, by their names: the session only lists those it can open.
                serverWide && this.config.can(Rights.manageTenants) ? this.api.tenants() : Promise.resolve(null),
            ]);
            this.users.set(users);
            this.groups.set(groups);
            const tenant = this.config.tenant();
            this.spaces.set(
                serverWide
                    ? spacesOf(groups, spaces ?? this.config.config()?.tenants ?? [])
                    : tenant
                      ? [{ id: tenant.id, name: tenant.name }]
                      : [],
            );
            this.error.set(null);
        } catch (error) {
            this.error.set(`Could not load the accounts: ${(error as Error).message}`);
        } finally {
            this.loaded.set(true);
        }
    }

    /** The account's spaces among those listed, with its groups in each. */
    protected spaceLines(user: UserInfo): SpaceLine[] {
        return this.spaces()
            .filter((space) => user.groups.some((group) => group.tenantId === space.id))
            .map((space) => ({ space: space.name, groups: groupNamesIn(user, space.id, this.groups()) }));
    }

    /** Its spaces a space's administrator doesn't see. */
    protected otherSpaces(user: UserInfo): number {
        const listed = new Set(this.spaces().map((space) => space.id));
        return new Set(user.groups.filter((group) => !listed.has(group.tenantId)).map((group) => group.tenantId)).size;
    }

    /** A space's administrator takes an account other spaces share out of the space rather than deleting it. */
    protected removesOnly(user: UserInfo): boolean {
        return !this.serverWide() && (this.otherSpaces(user) > 0 || user.isServerAdmin);
    }

    protected removeTitle(user: UserInfo): string {
        return this.removesOnly(user)
            ? `Take ${user.userName} out of this space; its other spaces keep the account (asks first)`
            : `Delete the account ${user.userName} (asks first)`;
    }

    protected async remove(user: UserInfo): Promise<void> {
        const outOnly = this.removesOnly(user);
        if (
            !confirm(
                outOnly
                    ? `Take ${user.userName} out of this space? The account stays in its other spaces.`
                    : `Delete the account ${user.userName}? It can no longer sign in anywhere; ` +
                          `its preferences go with it, the tags it set stay.`,
            )
        )
            return;
        try {
            await this.api.deleteUser(user.id);
            this.toasts.show(
                outOnly ? `${user.userName} is no longer in this space.` : `Account ${user.userName} deleted.`,
            );
            await this.load();
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }
}
