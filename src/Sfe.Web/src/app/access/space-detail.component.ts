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
} from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { GroupInfo, RightsCatalog, UserInfo } from './access.models';
import { Rights } from '../core/rights';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { inSpace } from './access-parts';
import { FolderAccessComponent } from './folder-access.component';
import { SpaceGroupsComponent } from './space-groups.component';
import { SpaceMembersComponent } from './space-members.component';
import { SpaceView } from './space-view';
import { SpaceVisitorsComponent } from './space-visitors.component';

/**
 * One space, laid out as it works: its library (a folder), its groups (each a set of rights), its accounts (each in
 * some of its groups), and the visitors without an account it lets in (with its Guests group's rights). Server
 * administrators manage any space from here; a space's administrator, their own.
 */
@Component({
    selector: 'sfe-space-detail',
    imports: [
        IconComponent,
        HelpComponent,
        SpaceGroupsComponent,
        SpaceMembersComponent,
        SpaceVisitorsComponent,
        FolderAccessComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'space-detail' },
    templateUrl: './space-detail.component.html',
})
export class SpaceDetailComponent {
    private readonly api = inject(Api);
    protected readonly config = inject(ConfigService);

    readonly space = input.required<SpaceView>();
    /** The space's guest access changed (the list of spaces shows it). */
    readonly changed = output<SpaceView>();

    protected readonly groups = signal<GroupInfo[]>([]);
    /** The accounts the session manages: every one for server administrators, else the space's. */
    protected readonly users = signal<UserInfo[]>([]);
    protected readonly catalog = signal<RightsCatalog | null>(null);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);

    private readonly allAccounts = computed(() => this.config.can(Rights.manageUsers));
    protected readonly tenantParam = computed(() => (this.space().current ? null : this.space().id));
    protected readonly members = computed(() => this.users().filter((user) => inSpace(user, this.space().id)));
    protected readonly candidates = computed(() => (this.allAccounts() ? this.users() : this.members()));

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
}
