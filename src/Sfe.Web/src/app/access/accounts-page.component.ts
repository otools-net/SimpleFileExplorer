import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate } from '../core/format';
import { GroupInfo, UserInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { AccountEditorComponent } from './account-editor.component';
import { groupNamesIn, SpaceRef } from './access-parts';

/** The spaces of `groups`, in the order of `known` (the first space, then by name), by their names there. */
function spacesOf(groups: GroupInfo[], known: SpaceRef[]): SpaceRef[] {
    const ids = new Set(groups.map(group => group.tenantId));
    const named = known.filter(space => ids.has(space.id)).map(space => ({ id: space.id, name: space.name }));
    const unnamed = [...ids].filter(id => !named.some(space => space.id === id)).map(id => ({ id, name: `Space ${id}` }));
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
    template: `
        <div class="help-row access-intro">
            <p>
                @if (serverWide()) {
                    Everyone who can sign in to this server, with the spaces each one sees and their groups there.
                } @else {
                    The people who can sign in to {{ config.tenant()?.name }}, with their groups in it.
                }
            </p>
            <sfe-help about="Accounts">
                <p>An <strong>account</strong> is a person (or a device) that signs in with a user name and a password. What it sees
                   depends on its <strong>spaces</strong>: it is in a space when it is in at least one of the space's groups, and those
                   groups decide what it may do there.</p>
                <p>Example: <em>anna</em> — Family: Administrators · Work: Users. Anna sees both spaces, manages Family and only uses Work.</p>
                @if (!serverWide()) {
                    <p>Accounts that other spaces share stay as they are there: here you only change their groups in this space.</p>
                }
            </sfe-help>
        </div>
        <div class="access-actions">
            <button type="button" class="button primary" [title]="serverWide() ? 'Create an account, and choose its spaces and groups' : 'Create an account in this space'"
                    (click)="editor().open(null, serverWide() ? null : config.tenant()?.id ?? null)">
                <svg sfeIcon="plus"></svg>New account
            </button>
            @if (users().length > 8) {
                <input type="search" class="access-filter" placeholder="Find an account" aria-label="Find an account" title="Show only the accounts whose name contains this"
                       [value]="filter()" (input)="filter.set(valueOf($event))">
            }
        </div>
        @if (error(); as error) {
            <p class="form-error">{{ error }}</p>
        } @else if (!loaded()) {
            <div class="tag-loading"><span class="spinner"></span></div>
        } @else {
            <ul class="access-list">
                @for (user of shown(); track user.id) {
                    <li class="access-row">
                        <div class="access-row-main">
                            <strong>{{ user.displayName || user.userName }}</strong>
                            @if (user.displayName) {
                                <span class="muted">{{ user.userName }}</span>
                            }
                            <span class="access-badges">
                                @if (user.isServerAdmin) {
                                    <span class="access-badge accent" title="Every right in every space, and the server's own">Server administrator</span>
                                }
                                @if (user.kind === 'guest') {
                                    <span class="access-badge" title="Can only look, whatever its groups allow">Guest account</span>
                                }
                                @if (user.source !== 'local') {
                                    <span class="access-badge" [title]="'Its password is checked by the directory ' + user.source">{{ user.source }}</span>
                                }
                                @if (user.disabled) {
                                    <span class="access-badge danger" title="Can't sign in">Disabled</span>
                                }
                                @if (user.lockedOut) {
                                    <span class="access-badge danger" title="Too many wrong passwords: locked for a while">Locked</span>
                                }
                            </span>
                        </div>
                        <div class="access-row-detail">
                            <ul class="access-memberships">
                                @for (line of spaceLines(user); track line.space) {
                                    <li><svg sfeIcon="space"></svg><span class="access-space-name">{{ line.space }}</span>
                                        @for (group of line.groups; track group) {
                                            <span class="access-badge group">{{ group }}</span>
                                        }
                                    </li>
                                } @empty {
                                    <li class="muted">{{ user.isServerAdmin ? 'Opens every space as server administrator' : 'In no space: it can sign in but sees nothing' }}</li>
                                }
                                @if (otherSpaces(user); as others) {
                                    <li class="muted">and {{ others === 1 ? 'another space' : others + ' other spaces' }}</li>
                                }
                            </ul>
                            <span class="muted">{{ user.lastSignIn ? 'Last signed in ' + formatDate(user.lastSignIn) : 'Never signed in' }}</span>
                        </div>
                        <div class="access-row-actions">
                            <button type="button" class="button small" [title]="'Change ' + user.userName + ': password, spaces, groups'" (click)="editor().open(user)">Edit</button>
                            @if (user.id !== config.user()?.id) {
                                <button type="button" class="button small" [title]="removeTitle(user)" (click)="remove(user)">{{ removesOnly(user) ? 'Remove' : 'Delete' }}</button>
                            }
                        </div>
                    </li>
                } @empty {
                    <li class="access-empty muted">{{ filter() ? 'No account matches.' : 'No accounts yet.' }}</li>
                }
            </ul>
        }
        <sfe-account-editor [spaces]="spaces()" [groups]="groups()" [serverWide]="serverWide()" (saved)="load()" />
    `,
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
            ? this.users().filter(user => `${user.userName} ${user.displayName ?? ''}`.toLocaleLowerCase().includes(filter))
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
            this.spaces.set(serverWide ? spacesOf(groups, spaces ?? this.config.config()?.tenants ?? []) : tenant ? [{ id: tenant.id, name: tenant.name }] : []);
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
            .filter(space => user.groups.some(group => group.tenantId === space.id))
            .map(space => ({ space: space.name, groups: groupNamesIn(user, space.id, this.groups()) }));
    }

    /** Its spaces a space's administrator doesn't see. */
    protected otherSpaces(user: UserInfo): number {
        const listed = new Set(this.spaces().map(space => space.id));
        return new Set(user.groups.filter(group => !listed.has(group.tenantId)).map(group => group.tenantId)).size;
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
        if (!confirm(outOnly
            ? `Take ${user.userName} out of this space? The account stays in its other spaces.`
            : `Delete the account ${user.userName}? It can no longer sign in anywhere; its preferences go with it, the tags it set stay.`))
            return;
        try {
            await this.api.deleteUser(user.id);
            this.toasts.show(outOnly ? `${user.userName} is no longer in this space.` : `Account ${user.userName} deleted.`);
            await this.load();
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }
}
