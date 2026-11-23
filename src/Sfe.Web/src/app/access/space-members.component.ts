import { ChangeDetectionStrategy, Component, computed, inject, input, output, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate } from '../core/format';
import { icu } from '../core/i18n/icu';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { GroupInfo, UserInfo } from './access.models';
import { groupNamesIn, inSpace, SpaceRef } from './access-parts';
import { AccountEditorComponent } from './account-editor.component';
import { SpaceView } from './space-view';

/**
 * The accounts of a space: those in at least one of its groups. Create them, change them, take them out of the space
 * (or delete one no other space has), and put an account of another space in this one (server administrators).
 */
@Component({
    selector: 'sfe-space-members',
    imports: [IconComponent, HelpComponent, AccountEditorComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './space-members.component.html',
})
export class SpaceMembersComponent {
    private readonly api = inject(Api);
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly editor = viewChild.required(AccountEditorComponent);

    readonly space = input.required<SpaceView>();
    readonly groups = input.required<GroupInfo[]>();
    /** The accounts the session manages: every one for server administrators, else the space's. */
    readonly users = input.required<UserInfo[]>();
    /** An account was created, changed, taken out or added. */
    readonly changed = output<void>();

    /** Server administrators: every space and every account. */
    protected readonly serverWide = computed(() => this.config.can(Rights.manageTenants));
    protected readonly allAccounts = computed(() => this.config.can(Rights.manageUsers));
    protected readonly spaceRefs = computed<SpaceRef[]>(() => [{ id: this.space().id, name: this.space().name }]);
    protected readonly members = computed(() => this.users().filter((user) => inSpace(user, this.space().id)));
    /** Accounts of other spaces (server administrators), to add to this one. */
    protected readonly outsiders = computed(() =>
        this.allAccounts() ? this.users().filter((user) => !inSpace(user, this.space().id) && !user.isServerAdmin) : [],
    );

    protected readonly formatDate = formatDate;

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
            this.changed.emit();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error(
                deletes
                    ? $localize`Could not delete the account “${name}:name:”: ${reason}:reason:`
                    : $localize`Could not take “${name}:name:” out of “${space}:space:”: ${reason}:reason:`,
            );
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
            this.changed.emit();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error(
                $localize`Could not add “${user.userName}:name:” to “${this.space().name}:space:”: ${reason}:reason:`,
            );
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
