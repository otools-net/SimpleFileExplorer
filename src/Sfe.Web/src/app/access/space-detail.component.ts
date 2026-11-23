import {
    ChangeDetectionStrategy,
    Component,
    computed,
    effect,
    inject,
    input,
    output,
    signal,
    untracked,
    viewChild,
} from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate } from '../core/format';
import { icu } from '../core/i18n/icu';
import { GroupInfo, RightsCatalog, UserInfo } from './access.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { checkedOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { AccountEditorComponent } from './account-editor.component';
import { BUILT_IN_PURPOSE, groupNamesIn, inSpace, SpaceRef } from './access-parts';
import { FolderAccessComponent } from './folder-access.component';
import { GroupEditorComponent } from './group-editor.component';

/**
 * The space a page shows: from `api/tenants` for server administrators, from `api/access/tenant` for the space's own.
 */
export interface SpaceView {
    id: number;
    name: string;
    /** Full path of its library folder; null when the session may not see it (a space's administrator). */
    root: string | null;
    guestAccess: boolean;
    isDefault: boolean;
    disabled: boolean;
    /** The session's own space (the requests about it need no `tenant`). */
    current: boolean;
}

/**
 * One space, laid out as it works: its library (a folder), its groups (each a set of rights), its accounts (each in
 * some of its groups), and the visitors without an account it lets in (with its Guests group's rights). Server
 * administrators manage any space from here; a space's administrator, their own.
 */
@Component({
    selector: 'sfe-space-detail',
    imports: [IconComponent, HelpComponent, GroupEditorComponent, AccountEditorComponent, FolderAccessComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'space-detail' },
    templateUrl: './space-detail.component.html',
})
export class SpaceDetailComponent {
    private readonly api = inject(Api);
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly groupEditor = viewChild.required(GroupEditorComponent);
    protected readonly accountEditor = viewChild.required(AccountEditorComponent);

    readonly space = input.required<SpaceView>();
    /** The space's guest access changed (the list of spaces shows it). */
    readonly changed = output<SpaceView>();

    protected readonly groups = signal<GroupInfo[]>([]);
    /** The accounts the session manages: every one for server administrators, else the space's. */
    private readonly users = signal<UserInfo[]>([]);
    protected readonly catalog = signal<RightsCatalog | null>(null);
    protected readonly anonymousAllowed = signal(true);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly busy = signal(false);

    /** Server administrators: every space and every account. */
    protected readonly serverWide = computed(() => this.config.can(Rights.manageTenants));
    protected readonly allAccounts = computed(() => this.config.can(Rights.manageUsers));
    protected readonly tenantParam = computed(() => (this.space().current ? null : this.space().id));
    protected readonly spaceRefs = computed<SpaceRef[]>(() => [{ id: this.space().id, name: this.space().name }]);
    protected readonly members = computed(() => this.users().filter((user) => inSpace(user, this.space().id)));
    /** Accounts of other spaces (server administrators), to add to this one. */
    protected readonly outsiders = computed(() =>
        this.allAccounts() ? this.users().filter((user) => !inSpace(user, this.space().id) && !user.isServerAdmin) : [],
    );
    protected readonly candidates = computed(() => (this.allAccounts() ? this.users() : this.members()));

    protected readonly formatDate = formatDate;
    protected readonly checkedOf = checkedOf;

    /** Loads again for another space only, not for the same one with a new name or guest access. */
    private readonly spaceId = computed(() => this.space().id);

    constructor() {
        effect(() => {
            this.spaceId();
            untracked(() => void this.load());
        });
    }

    async load(): Promise<void> {
        const tenant = this.tenantParam();
        // Whether the server lets visitors in at all: the same for every space, asked about the session's.
        this.api.tenantAccess().then(
            (access) => this.anonymousAllowed.set(access.anonymousGuestsAllowed),
            () => {},
        );
        try {
            const [groups, users, catalog] = await Promise.all([
                this.api.groups(false, tenant),
                this.allAccounts() ? this.api.users(true) : this.api.users(false, tenant),
                this.api.rights(tenant),
            ]);
            this.groups.set(groups);
            this.users.set(users);
            this.catalog.set(catalog);
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        } finally {
            this.loaded.set(true);
        }
    }

    protected purpose(group: GroupInfo): string {
        return group.builtIn ? BUILT_IN_PURPOSE[group.builtIn] : '';
    }

    protected groupNames(user: UserInfo): string[] {
        return groupNamesIn(user, this.space().id, this.groups());
    }

    /** "Also in 2 other spaces · " for an account shared with other spaces. */
    protected elsewhere(user: UserInfo): string {
        const others = new Set(
            user.groups.filter((group) => group.tenantId !== this.space().id).map((group) => group.tenantId),
        ).size;
        if (!others) return '';
        const text = icu($localize`{others, plural, one {Also in another space} other {Also in # other spaces}}`, {
            others,
        });
        return `${text} · `;
    }

    /** Taking the account out deletes it: a space's administrator removing an account no other space has. */
    protected deletes(user: UserInfo): boolean {
        return (
            !this.allAccounts() &&
            !user.isServerAdmin &&
            user.groups.every((group) => group.tenantId === this.space().id)
        );
    }

    protected removeTitle(user: UserInfo): string {
        const name = user.userName;
        const space = this.space().name;
        return this.deletes(user)
            ? $localize`Delete the account ${name}:name:, which is in no other space (asks first)`
            : $localize`Take ${name}:name: out of ${space}:space:; the account itself stays (asks first)`;
    }

    protected async remove(user: UserInfo): Promise<void> {
        const deletes = this.deletes(user);
        const name = user.userName;
        const space = this.space().name;
        if (
            !confirm(
                deletes
                    ? $localize`Delete the account “${name}:name:”? It is in no other space. \
Its preferences go with it; the tags it set stay. This cannot be undone.`
                    : $localize`Take “${name}:name:” out of “${space}:space:”? \
The account stays, and keeps its other spaces.`,
            )
        )
            return;
        try {
            if (this.allAccounts())
                await this.api.updateUser(
                    user.id,
                    this.inputOf(
                        user,
                        user.groups.filter((group) => group.tenantId !== this.space().id).map((group) => group.groupId),
                    ),
                );
            else await this.api.deleteUser(user.id);
            this.toasts.show(
                deletes
                    ? $localize`Account “${name}:name:” deleted.`
                    : $localize`“${name}:name:” is no longer in “${space}:space:”.`,
            );
            await this.load();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error(
                deletes
                    ? $localize`Could not delete the account “${name}:name:”: ${reason}:reason:`
                    : $localize`Could not take “${name}:name:” out of “${space}:space:”: ${reason}:reason:`,
            );
        }
    }

    protected async removeGroup(group: GroupInfo): Promise<void> {
        if (
            !confirm(
                $localize`Delete the group “${group.name}:name:”? Its members lose the rights it gave them; \
an account in no other group here loses access to “${this.space().name}:space:”. This cannot be undone.`,
            )
        )
            return;
        try {
            await this.api.deleteGroup(group.id, this.tenantParam());
            this.toasts.show($localize`Group “${group.name}:name:” deleted.`);
            await this.load();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error($localize`Could not delete the group “${group.name}:name:”: ${reason}:reason:`);
        }
    }

    /** Puts an account of another space in this one too, in its Users group. */
    protected async addExisting(event: Event): Promise<void> {
        const select = event.target as HTMLSelectElement;
        const user = this.users().find((candidate) => candidate.id === Number(select.value));
        select.value = '';
        const usersGroup = this.groups().find((group) => group.builtIn === 'users');
        if (!user || !usersGroup) return;
        try {
            await this.api.updateUser(
                user.id,
                this.inputOf(user, [...user.groups.map((group) => group.groupId), usersGroup.id]),
            );
            this.toasts.show(
                $localize`“${user.userName}:name:” is now in “${this.space().name}:space:”, \
in the group “${usersGroup.name}:group:”.`,
            );
            await this.load();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error(
                $localize`Could not add “${user.userName}:name:” to “${this.space().name}:space:”: ${reason}:reason:`,
            );
        }
    }

    protected async setGuestAccess(open: boolean): Promise<void> {
        const space = this.space();
        this.busy.set(true);
        try {
            if (space.current) await this.api.setGuestAccess(open);
            else
                await this.api.updateTenant(space.id, {
                    name: space.name,
                    guestAccess: open,
                    disabled: space.disabled,
                    disabledRights: null,
                });
            this.changed.emit({ ...space, guestAccess: open });
            this.toasts.show(
                open
                    ? $localize`Visitors without an account may now browse “${space.name}:space:”.`
                    : $localize`Only accounts may open “${space.name}:space:” now.`,
            );
        } catch (error) {
            this.toasts.error($localize`Could not change the visitors' access: ${(error as Error).message}:reason:`);
        } finally {
            this.busy.set(false);
        }
    }

    /** The account as it is, with other groups (server administrators replace them all). */
    private inputOf(user: UserInfo, groupIds: number[]) {
        return {
            userName: user.userName,
            displayName: user.displayName,
            password: null,
            kind: user.kind,
            isServerAdmin: user.isServerAdmin,
            disabled: user.disabled,
            groupIds,
        };
    }
}
