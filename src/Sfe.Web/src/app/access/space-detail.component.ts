import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate, plural } from '../core/format';
import { GroupInfo, RightsCatalog, UserInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { checkedOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { AccountEditorComponent } from './account-editor.component';
import { BUILT_IN_PURPOSE, groupNamesIn, inSpace, SpaceRef } from './access-parts';
import { FolderAccessComponent } from './folder-access.component';
import { GroupEditorComponent } from './group-editor.component';

/** The space a page shows: from `api/tenants` for server administrators, from `api/access/tenant` for the space's own. */
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
    template: `
        @if (error(); as error) {
            <p class="form-error">{{ error }}</p>
        } @else if (!loaded()) {
            <div class="tag-loading"><span class="spinner"></span></div>
        } @else {
            <section class="access-card" aria-labelledby="space-library">
                <header class="access-card-head help-row">
                    <svg sfeIcon="folder"></svg>
                    <h3 id="space-library">Library</h3>
                    <sfe-help about="The library">
                        <p>The folder of the server whose files this space shows, with everything below it. Its index, tags, settings,
                           duplicates and background tasks belong to this space alone: tags set here are not seen in another space.</p>
                        <p>The folder is chosen when the space is created and can't change afterwards.</p>
                    </sfe-help>
                </header>
                @if (space().root; as root) {
                    <p class="access-path access-library">{{ root }}</p>
                } @else {
                    <p class="access-library">{{ config.config()?.rootName }}</p>
                }
                <p class="field-hint">Everyone below sees these files, each with the rights of their groups.</p>
            </section>

            <section class="access-card" aria-labelledby="space-groups">
                <header class="access-card-head help-row">
                    <svg sfeIcon="group"></svg>
                    <h3 id="space-groups">Groups <span class="access-count">{{ groups().length }}</span></h3>
                    <sfe-help about="Groups">
                        <p>A <strong>group</strong> is a set of rights: what its members may do in this space (browse, download, tag, scan,
                           change settings...). Accounts get rights only through their groups.</p>
                        <p>Every space has three built-in groups:</p>
                        <ul>
                            <li><strong>Administrators</strong> — manage the space: its accounts, groups, settings, tags and scans.</li>
                            <li><strong>Users</strong> — everyday use: browse, play, download, tag.</li>
                            <li><strong>Guests</strong> — what visitors without an account may do (only looking).</li>
                        </ul>
                        <p>Make your own for anything in between. Example: <em>Kids</em>, who may browse and play but not download; put
                           the children's accounts in Kids only.</p>
                    </sfe-help>
                    <span class="spacer"></span>
                    <button type="button" class="button small" title="Create a group of this space, with its own rights" (click)="groupEditor().open(null)">
                        <svg sfeIcon="plus"></svg>New group
                    </button>
                </header>
                <ul class="access-list">
                    @for (group of groups(); track group.id) {
                        <li class="access-row">
                            <div class="access-row-main">
                                <strong>{{ group.name }}</strong>
                                <span class="access-badges">
                                    @if (group.builtIn) {
                                        <span class="access-badge" title="Every space has it; it can't be deleted">Built-in</span>
                                    }
                                    @if (group.externalName) {
                                        <span class="access-badge" [title]="'Its members are those of the directory group ' + group.externalName">{{ group.externalName }}</span>
                                    }
                                </span>
                            </div>
                            <div class="access-row-detail muted">
                                @if (group.builtIn) {
                                    {{ purpose(group) }} ·
                                }
                                {{ plural(group.rights.length, 'right') }} ·
                                {{ group.builtIn === 'guests' ? 'used for visitors without an account' : plural(group.memberIds.length, 'account') }}
                            </div>
                            <div class="access-row-actions">
                                <button type="button" class="button small" [title]="'Change what ' + group.name + ' may do, and its members'" (click)="groupEditor().open(group)">Edit</button>
                                @if (!group.builtIn) {
                                    <button type="button" class="button small" [title]="'Delete the group ' + group.name + ' (asks first)'" (click)="removeGroup(group)">Delete</button>
                                }
                            </div>
                        </li>
                    }
                </ul>
            </section>

            <section class="access-card" aria-labelledby="space-accounts">
                <header class="access-card-head help-row">
                    <svg sfeIcon="user"></svg>
                    <h3 id="space-accounts">Accounts <span class="access-count">{{ members().length }}</span></h3>
                    <sfe-help about="Accounts of a space">
                        <p>The people who sign in to this space: every account in at least one of its groups. An account may be in several
                           spaces, with different groups in each, and switches between them from the ⋮ menu.</p>
                        <p>Example: <em>anna</em> is in Administrators here and in Users in the <em>Work</em> space; she sees both.</p>
                        @if (serverWide()) {
                            <p>Server administrators open every space, even one where they are in no group (and then not listed here).</p>
                        }
                    </sfe-help>
                    <span class="spacer"></span>
                    @if (outsiders().length) {
                        <select class="access-add-existing" title="Put an account of another space in this one too (in its Users group)" (change)="addExisting($event)">
                            <option value="" selected>Add an existing account…</option>
                            @for (user of outsiders(); track user.id) {
                                <option [value]="user.id">{{ user.displayName ? user.displayName + ' (' + user.userName + ')' : user.userName }}</option>
                            }
                        </select>
                    }
                    <button type="button" class="button small primary" [title]="'Create an account in ' + space().name + ' (in its Users group to start with)'"
                            (click)="accountEditor().open(null, space().id)">
                        <svg sfeIcon="plus"></svg>New account
                    </button>
                </header>
                <ul class="access-list">
                    @for (user of members(); track user.id) {
                        <li class="access-row">
                            <div class="access-row-main">
                                <strong>{{ user.displayName || user.userName }}</strong>
                                @if (user.displayName) {
                                    <span class="muted">{{ user.userName }}</span>
                                }
                                <span class="access-badges">
                                    @for (name of groupNames(user); track name) {
                                        <span class="access-badge group">{{ name }}</span>
                                    }
                                    @if (user.isServerAdmin) {
                                        <span class="access-badge accent" title="Every right in every space">Server administrator</span>
                                    }
                                    @if (user.kind === 'guest') {
                                        <span class="access-badge" title="Can only look, whatever its groups allow">Guest account</span>
                                    }
                                    @if (user.disabled) {
                                        <span class="access-badge danger" title="Can't sign in">Disabled</span>
                                    }
                                </span>
                            </div>
                            <div class="access-row-detail muted">
                                {{ elsewhere(user) }}{{ user.lastSignIn ? 'last signed in ' + formatDate(user.lastSignIn) : 'never signed in' }}
                            </div>
                            <div class="access-row-actions">
                                <button type="button" class="button small" [title]="'Change ' + user.userName + ', its groups here'" (click)="accountEditor().open(user)">Edit</button>
                                @if (user.id !== config.user()?.id) {
                                    <button type="button" class="button small" [title]="removeTitle(user)" (click)="remove(user)">{{ deletes(user) ? 'Delete' : 'Remove' }}</button>
                                }
                            </div>
                        </li>
                    } @empty {
                        <li class="access-empty muted">No accounts in this space yet. Create one, and it will be able to sign in and see these files.</li>
                    }
                </ul>
            </section>

            <section class="access-card" aria-labelledby="space-visitors">
                <header class="access-card-head help-row">
                    <svg sfeIcon="globe"></svg>
                    <h3 id="space-visitors">Visitors without an account</h3>
                    <sfe-help about="Visitors without an account">
                        <p>Anyone who reaches the server can browse a space open to visitors, without signing in: the sign-in page offers a
                           button for each such space. They get the rights of the <strong>Guests</strong> group, and never a right that
                           changes something.</p>
                        <p>Example: open a <em>Holiday photos</em> space so relatives on your network can look at it, while <em>Family</em>
                           stays private.</p>
                        <p>For someone who should sign in but only look, make a <em>guest account</em> instead (New account › Guest account).</p>
                    </sfe-help>
                </header>
                <label class="check">
                    <input type="checkbox" [checked]="space().guestAccess" [disabled]="busy() || !anonymousAllowed()" (change)="setGuestAccess(checkedOf($event))">
                    <span>
                        Let visitors browse {{ space().name }} without an account
                        <span class="field-hint">
                            {{ space().guestAccess ? 'Open: anyone who can reach the server sees these files.' : 'Closed: only the accounts above can open this space.' }}
                            They may do what the Guests group allows.
                        </span>
                    </span>
                </label>
                @if (!anonymousAllowed()) {
                    @if (config.config()?.mode === 'local') {
                        <p class="field-hint">SFE is in Local mode: only its administrators open it (Administration › System › Installation).</p>
                    } @else {
                        <p class="field-hint">The server's configuration forbids visitors without an account (Sfe:Security:AnonymousGuests).</p>
                    }
                }
            </section>

            <sfe-folder-access [space]="tenantParam()" [groups]="groups()" [members]="members()" />

            <sfe-group-editor [space]="tenantParam()" [spaceName]="space().name" [candidates]="candidates()" [catalog]="catalog()" (saved)="load()" />
            <sfe-account-editor [spaces]="spaceRefs()" [groups]="groups()" [serverWide]="allAccounts()" (saved)="load()" />
        }
    `,
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
    protected readonly tenantParam = computed(() => this.space().current ? null : this.space().id);
    protected readonly spaceRefs = computed<SpaceRef[]>(() => [{ id: this.space().id, name: this.space().name }]);
    protected readonly members = computed(() => this.users().filter(user => inSpace(user, this.space().id)));
    /** Accounts of other spaces (server administrators), to add to this one. */
    protected readonly outsiders = computed(() => this.allAccounts() ? this.users().filter(user => !inSpace(user, this.space().id) && !user.isServerAdmin) : []);
    protected readonly candidates = computed(() => this.allAccounts() ? this.users() : this.members());

    protected readonly plural = plural;
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
        this.api.tenantAccess().then(access => this.anonymousAllowed.set(access.anonymousGuestsAllowed), () => { });
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
            this.error.set(`Could not load the space: ${(error as Error).message}`);
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
        const others = new Set(user.groups.filter(group => group.tenantId !== this.space().id).map(group => group.tenantId)).size;
        return others ? `Also in ${others === 1 ? 'another space' : `${others} other spaces`} · ` : '';
    }

    /** Taking the account out deletes it: a space's administrator removing an account no other space has. */
    protected deletes(user: UserInfo): boolean {
        return !this.allAccounts() && !user.isServerAdmin && user.groups.every(group => group.tenantId === this.space().id);
    }

    protected removeTitle(user: UserInfo): string {
        return this.deletes(user)
            ? `Delete the account ${user.userName}: it is in no other space (asks first)`
            : `Take ${user.userName} out of ${this.space().name}; the account itself stays (asks first)`;
    }

    protected async remove(user: UserInfo): Promise<void> {
        const deletes = this.deletes(user);
        if (!confirm(deletes
            ? `Delete the account ${user.userName}? It is in no other space. Its preferences go with it; the tags it set stay.`
            : `Take ${user.userName} out of ${this.space().name}? The account stays, and keeps its other spaces.`))
            return;
        try {
            if (this.allAccounts())
                await this.api.updateUser(user.id, this.inputOf(user, user.groups.filter(group => group.tenantId !== this.space().id).map(group => group.groupId)));
            else
                await this.api.deleteUser(user.id);
            this.toasts.show(deletes ? `Account ${user.userName} deleted.` : `${user.userName} is no longer in ${this.space().name}.`);
            await this.load();
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }

    protected async removeGroup(group: GroupInfo): Promise<void> {
        if (!confirm(`Delete the group ${group.name}? Its members lose the rights it gave them; an account in no other group here loses access to ${this.space().name}.`))
            return;
        try {
            await this.api.deleteGroup(group.id, this.tenantParam());
            this.toasts.show(`Group ${group.name} deleted.`);
            await this.load();
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }

    /** Puts an account of another space in this one too, in its Users group. */
    protected async addExisting(event: Event): Promise<void> {
        const select = event.target as HTMLSelectElement;
        const user = this.users().find(candidate => candidate.id === Number(select.value));
        select.value = '';
        const usersGroup = this.groups().find(group => group.builtIn === 'users');
        if (!user || !usersGroup)
            return;
        try {
            await this.api.updateUser(user.id, this.inputOf(user, [...user.groups.map(group => group.groupId), usersGroup.id]));
            this.toasts.show(`${user.userName} is now in ${this.space().name}, in the group ${usersGroup.name}.`);
            await this.load();
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }

    protected async setGuestAccess(open: boolean): Promise<void> {
        const space = this.space();
        this.busy.set(true);
        try {
            if (space.current)
                await this.api.setGuestAccess(open);
            else
                await this.api.updateTenant(space.id, { name: space.name, guestAccess: open, disabled: space.disabled, disabledRights: null });
            this.changed.emit({ ...space, guestAccess: open });
            this.toasts.show(open ? `Visitors without an account may browse ${space.name}.` : `Only accounts may open ${space.name} now.`);
        } catch (error) {
            this.toasts.error(`Could not change the visitors' access: ${(error as Error).message}`);
        } finally {
            this.busy.set(false);
        }
    }

    /** The account as it is, with other groups (server administrators replace them all). */
    private inputOf(user: UserInfo, groupIds: number[]) {
        return {
            userName: user.userName, displayName: user.displayName, password: null, kind: user.kind, isServerAdmin: user.isServerAdmin,
            disabled: user.disabled, groupIds,
        };
    }
}
