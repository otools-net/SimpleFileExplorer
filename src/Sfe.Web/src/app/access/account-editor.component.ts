import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { UserKind } from '../core/access.models';
import { GroupInfo, UserInfo } from './access.models';
import { ToastService } from '../core/toast.service';
import { checkedOf, valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { SheetComponent } from '../shared/sheet.component';
import { fieldErrors, SpaceRef, toggled } from './access-parts';

/** An account being created (`id` null) or changed. */
interface AccountDraft {
    id: number | null;
    userName: string;
    displayName: string;
    /** A new password; empty keeps the current one of an existing account. */
    password: string;
    kind: UserKind;
    isServerAdmin: boolean;
    disabled: boolean;
    unlock: boolean;
    local: boolean;
    lockedOut: boolean;
    /** The spaces shown, in order: those the account is in, then those added here. */
    spaceIds: number[];
    /** Its groups in the spaces this editor manages. */
    groupIds: ReadonlySet<number>;
    /** Its groups in other spaces, kept as they are. */
    otherGroupIds: number[];
    /** How many other spaces it is in. */
    otherSpaces: number;
}

/**
 * Creates or changes an account: how it signs in, what kind of account it is, and its spaces with its groups in each.
 * Server administrators (`serverWide`) manage every space and everything about the account; a space's administrator
 * manages that space only, and only the groups there of an account that other spaces share.
 */
@Component({
    selector: 'sfe-account-editor',
    imports: [SheetComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './account-editor.component.html',
})
export class AccountEditorComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    protected readonly sheet = viewChild.required<SheetComponent>('sheet');

    /** The spaces this editor manages: every one for server administrators, else the session's. */
    readonly spaces = input.required<SpaceRef[]>();
    /** The groups of those spaces. */
    readonly groups = input.required<GroupInfo[]>();
    /** A server administrator: every account, every space, server administrators. */
    readonly serverWide = input(false);
    readonly saved = output<UserInfo>();

    protected readonly draft = signal<AccountDraft | null>(null);
    protected readonly source = signal('');
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);
    protected readonly saving = signal(false);

    /** A space's administrator editing an account other spaces share (or a server administrator): groups only. */
    protected readonly limited = computed(() => {
        const draft = this.draft();
        return Boolean(
            draft && draft.id !== null && !this.serverWide() && (draft.otherSpaces > 0 || draft.isServerAdmin),
        );
    });
    protected readonly shownSpaces = computed(() => {
        const ids = this.draft()?.spaceIds ?? [];
        return ids
            .map((id) => this.spaces().find((space) => space.id === id))
            .filter((space): space is SpaceRef => Boolean(space));
    });
    protected readonly addable = computed(() => {
        const shown = new Set(this.draft()?.spaceIds ?? []);
        return this.limited() ? [] : this.spaces().filter((space) => !shown.has(space.id));
    });

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    /** Opens the account, or a new one; `space` puts a new account in that space's Users group. */
    open(user: UserInfo | null, space: number | null = null): void {
        const managed = new Set(this.spaces().map((s) => s.id));
        const memberships = user?.groups ?? [];
        const here = memberships.filter((m) => managed.has(m.tenantId));
        const elsewhere = memberships.filter((m) => !managed.has(m.tenantId));
        const start = !user && space !== null ? this.usersGroup(space) : undefined;
        this.draft.set({
            id: user?.id ?? null,
            userName: user?.userName ?? '',
            displayName: user?.displayName ?? '',
            password: '',
            kind: user?.kind ?? 'user',
            isServerAdmin: user?.isServerAdmin ?? false,
            disabled: user?.disabled ?? false,
            unlock: false,
            local: (user?.source ?? 'local') === 'local',
            lockedOut: user?.lockedOut ?? false,
            spaceIds: user ? [...new Set(here.map((m) => m.tenantId))] : space !== null ? [space] : [],
            groupIds: new Set(user ? here.map((m) => m.groupId) : start ? [start.id] : []),
            otherGroupIds: elsewhere.map((m) => m.groupId),
            otherSpaces: new Set(elsewhere.map((m) => m.tenantId)).size,
        });
        this.source.set(user?.source ?? 'local');
        this.errors.set({});
        this.problem.set(null);
        this.sheet().open();
    }

    protected groupsOf(space: number): GroupInfo[] {
        return this.groups().filter((group) => group.tenantId === space);
    }

    protected groupsChosenIn(space: number): number {
        const chosen = this.draft()?.groupIds ?? new Set<number>();
        return this.groupsOf(space).filter((group) => chosen.has(group.id)).length;
    }

    protected purpose(group: GroupInfo): string {
        return group.builtIn === 'guests'
            ? 'Its rights are those of visitors without an account; an account in it gets them too'
            : `${group.rights.length} rights`;
    }

    protected change(changes: Partial<AccountDraft>): void {
        this.draft.update((draft) => draft && { ...draft, ...changes });
    }

    protected toggleGroup(id: number, member: boolean): void {
        this.draft.update((draft) => draft && { ...draft, groupIds: toggled(draft.groupIds, id, member) });
    }

    /** Puts the account in another space, in its Users group to start with. */
    protected addSpace(event: Event): void {
        const select = event.target as HTMLSelectElement;
        const space = Number(select.value);
        select.value = '';
        if (!space) return;
        const start = this.usersGroup(space);
        this.draft.update(
            (draft) =>
                draft && {
                    ...draft,
                    spaceIds: [...draft.spaceIds, space],
                    groupIds: start ? toggled(draft.groupIds, start.id, true) : draft.groupIds,
                },
        );
    }

    protected removeSpace(space: number): void {
        const ids = new Set(this.groupsOf(space).map((group) => group.id));
        this.draft.update(
            (draft) =>
                draft && {
                    ...draft,
                    spaceIds: draft.spaceIds.filter((id) => id !== space),
                    groupIds: new Set([...draft.groupIds].filter((id) => !ids.has(id))),
                },
        );
    }

    protected async save(event?: Event): Promise<void> {
        event?.preventDefault();
        const draft = this.draft();
        if (!draft) return;
        this.saving.set(true);
        this.errors.set({});
        this.problem.set(null);
        const input = {
            userName: draft.userName.trim(),
            displayName: draft.displayName.trim() || null,
            password: draft.password || null,
            kind: draft.kind,
            isServerAdmin: draft.isServerAdmin,
            disabled: draft.disabled,
            unlock: draft.unlock,
            // Server administrators replace every group of the account: those of the spaces not shown go as they are.
            groupIds: [...draft.groupIds, ...(this.serverWide() ? draft.otherGroupIds : [])],
        };
        try {
            const user =
                draft.id === null ? await this.api.createUser(input) : await this.api.updateUser(draft.id, input);
            this.sheet().close();
            this.toasts.show(
                draft.id === null ? `Account ${input.userName} created.` : `Account ${input.userName} saved.`,
            );
            this.saved.emit(user);
        } catch (error) {
            const errors = fieldErrors(error);
            if (errors) this.errors.set(errors);
            else this.problem.set((error as Error).message);
        } finally {
            this.saving.set(false);
        }
    }

    private usersGroup(space: number): GroupInfo | undefined {
        const groups = this.groupsOf(space);
        return groups.find((group) => group.builtIn === 'users') ?? groups.find((group) => group.builtIn !== 'guests');
    }
}
