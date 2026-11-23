import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { valueOf } from '../shared/events';
import { GroupInfo, RightsCatalog, TenantDetails, UserInfo } from './access.models';
import { inSpace } from './access-parts';
import { AccountsPageComponent } from './accounts-page.component';
import { FolderAccessComponent } from './folder-access.component';
import { SpaceGroupsComponent } from './space-groups.component';
import { SpaceMembersComponent } from './space-members.component';
import { spaceView, SpaceView } from './space-view';
import { SpaceVisitorsComponent } from './space-visitors.component';

/** The sub-pages of Spaces & accounts about one space. */
export type SpacePart = 'groups' | 'accounts' | 'folders' | 'visitors';

/**
 * Administration › Spaces & accounts › Groups, Accounts, Folder access or Visitors: that part of one space. The space
 * is `space` in the address, the session's own without it; server administrators pick another above. Accounts without
 * `space` lists every account of the server (server administrators) or the space's (its administrators). `folder` in
 * the address (a folder's "Folder access…") starts a rule on that folder.
 */
@Component({
    selector: 'sfe-space-part',
    imports: [
        AccountsPageComponent,
        FolderAccessComponent,
        SpaceGroupsComponent,
        SpaceMembersComponent,
        SpaceVisitorsComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'access-page' },
    templateUrl: './space-part.component.html',
})
export class SpacePartComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly location = inject(Location);

    readonly part = input.required<SpacePart>();

    /** Every space (server administrators), to pick one. */
    protected readonly spaces = signal<TenantDetails[]>([]);
    /** The session's space, for a space's administrator. */
    private readonly own = signal<SpaceView | null>(null);
    protected readonly groups = signal<GroupInfo[]>([]);
    /** The accounts the session manages: every one for server administrators, else the space's. */
    protected readonly users = signal<UserInfo[]>([]);
    protected readonly catalog = signal<RightsCatalog | null>(null);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);

    protected readonly serverWide = computed(() => this.config.can(Rights.manageTenants));
    private readonly allAccounts = computed(() => this.config.can(Rights.manageUsers));
    private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });
    protected readonly folder = computed(() => this.params().get('folder'));
    /** Every account rather than one space's: Accounts without `space`, or for a space's administrator. */
    protected readonly everyAccount = computed(
        () => this.part() === 'accounts' && (!this.serverWide() || !this.params().has('space')),
    );
    protected readonly selected = computed<SpaceView | null>(() => {
        if (!this.serverWide()) return this.own();
        const id = Number(this.params().get('space') ?? this.config.tenant()?.id);
        const space = this.spaces().find((candidate) => candidate.id === id);
        return space ? spaceView(space, this.config.tenant()?.id) : null;
    });
    protected readonly tenantParam = computed(() => {
        const space = this.selected();
        return !space || space.current ? null : space.id;
    });
    protected readonly members = computed(() => this.users().filter((user) => inSpace(user, this.selected()?.id ?? 0)));
    protected readonly candidates = computed(() => (this.allAccounts() ? this.users() : this.members()));
    /** The space picked; '' for every account. */
    protected readonly picked = computed(() => (this.everyAccount() ? '' : String(this.selected()?.id ?? '')));

    protected readonly valueOf = valueOf;

    constructor() {
        void this.loadSpaces();
        effect(() => {
            const id = this.selected()?.id;
            if (id !== undefined && !this.everyAccount()) untracked(() => void this.load());
        });
    }

    /** Shows another space ('' : every account), in place of the current entry, keeping where "back" leads. */
    protected pick(id: string): void {
        void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { space: id === '' ? null : Number(id), folder: null },
            queryParamsHandling: 'merge',
            replaceUrl: true,
            state: (this.location.getState() as Record<string, unknown> | null) ?? {},
        });
    }

    /** The space's visitors changed: the picker and the page follow. */
    protected onChanged(space: SpaceView): void {
        if (!this.serverWide()) this.own.set(space);
        else
            this.spaces.update((spaces) =>
                spaces.map((existing) =>
                    existing.id === space.id ? { ...existing, guestAccess: space.guestAccess } : existing,
                ),
            );
    }

    async load(): Promise<void> {
        const tenant = this.tenantParam();
        const part = this.part();
        try {
            const [groups, users, catalog] = await Promise.all([
                part === 'visitors' ? [] : this.api.groups(false, tenant),
                part === 'visitors' ? [] : this.loadUsers(tenant),
                part === 'groups' ? this.api.rights(tenant) : null,
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

    private loadUsers(tenant: number | null): Promise<UserInfo[]> {
        return this.allAccounts() ? this.api.users(true) : this.api.users(false, tenant);
    }

    private async loadSpaces(): Promise<void> {
        try {
            if (this.serverWide()) this.spaces.set(await this.api.tenants());
            else {
                const access = await this.api.tenantAccess();
                this.own.set({
                    id: access.id,
                    name: access.name,
                    root: null,
                    guestAccess: access.guestAccess,
                    isDefault: false,
                    disabled: false,
                    current: true,
                });
            }
        } catch (error) {
            this.error.set((error as Error).message);
            this.loaded.set(true);
        }
    }
}
