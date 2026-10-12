import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { GroupInfo, UserInfo, UserKind } from '../core/models';
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
    template: `
        <sfe-sheet #sheet [heading]="draft()?.id === null ? 'New account' : 'Account ' + (draft()?.userName ?? '')" [wide]="true">
            @if (draft(); as draft) {
                <form class="access-form" novalidate (submit)="save($event)">
                    @if (limited()) {
                        <p class="access-note">
                            This account is also in other spaces (or administers the server), so only its groups in this space can be changed here.
                            Its name, password and status belong to the server's administrators.
                        </p>
                    }

                    <h3 class="access-subhead">Signing in</h3>
                    <div class="access-columns">
                        <div class="field">
                            <label class="field-label" for="account-name">User name</label>
                            <input id="account-name" type="text" autocomplete="off" autocapitalize="none" spellcheck="false" placeholder="e.g. anna"
                                   [disabled]="limited() || !draft.local" [value]="draft.userName" (input)="change({ userName: valueOf($event) })">
                            <span class="field-hint">Typed on the sign-in page. Upper and lower case don't matter.</span>
                            @if (errors()['userName']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <div class="field">
                            <label class="field-label" for="account-display">Display name <span class="field-hint">(optional)</span></label>
                            <input id="account-display" type="text" autocomplete="off" placeholder="e.g. Anna Smith" [disabled]="limited()"
                                   [value]="draft.displayName" (input)="change({ displayName: valueOf($event) })">
                            <span class="field-hint">Shown in the menu instead of the user name.</span>
                            @if (errors()['displayName']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                    </div>
                    @if (draft.local) {
                        <div class="field">
                            <label class="field-label" for="account-password">{{ draft.id === null ? 'Password' : 'New password' }}</label>
                            <input id="account-password" type="password" autocomplete="new-password" [disabled]="limited()" [value]="draft.password"
                                   [placeholder]="draft.id === null ? '' : 'Leave empty to keep the current one'" (input)="change({ password: valueOf($event) })">
                            @if (draft.id !== null) {
                                <span class="field-hint">A new password signs the account out everywhere.</span>
                            }
                            @if (errors()['password']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                    } @else {
                        <p class="field-hint">Its password is checked by the directory {{ source() }}, and changed there.</p>
                    }

                    <div class="access-subhead help-row">
                        <h3>Kind of account</h3>
                        <sfe-help about="Kinds of account">
                            <p>A <strong>regular account</strong> gets exactly the rights of its groups.</p>
                            <p>A <strong>guest account</strong> signs in like any other but can only look: it never gets a right that changes
                               something (tagging, scanning, settings, deleting duplicates...), even from a group that grants it. Example: a
                               shared <em>living-room-tv</em> account for the family's television.</p>
                            <p>Not to be confused with <strong>visitors without an account</strong>, who don't sign in at all: a space lets them
                               in from its own page (Visitors), with the rights of its Guests group.</p>
                        </sfe-help>
                    </div>
                    <div class="access-options" role="radiogroup" aria-label="Kind of account">
                        <label class="access-option" [class.selected]="draft.kind === 'user'">
                            <input type="radio" name="account-kind" [disabled]="limited()" [checked]="draft.kind === 'user'" (change)="change({ kind: 'user' })">
                            <span><strong>Regular account</strong><span class="field-hint">Does what its groups allow.</span></span>
                        </label>
                        <label class="access-option" [class.selected]="draft.kind === 'guest'">
                            <input type="radio" name="account-kind" [disabled]="limited() || draft.isServerAdmin" [checked]="draft.kind === 'guest'" (change)="change({ kind: 'guest' })">
                            <span><strong>Guest account</strong><span class="field-hint">Can only look, whatever its groups allow.</span></span>
                        </label>
                    </div>
                    @if (errors()['kind']; as error) {
                        <span class="field-error">{{ error }}</span>
                    }
                    @if (serverWide()) {
                        <div class="help-row">
                            <label class="check">
                                <input type="checkbox" [disabled]="draft.kind === 'guest'" [checked]="draft.isServerAdmin" (change)="change({ isServerAdmin: checkedOf($event) })">
                                <span>Server administrator<span class="field-hint">Every right in every space, plus the spaces themselves, every account and the server's settings.</span></span>
                            </label>
                            <sfe-help about="Server administrators">
                                <p>Server administrators stand above the spaces: they open any space with every right, create and delete spaces,
                                   manage every account and change the settings of the whole server. Their groups don't matter.</p>
                                <p>Keep at least one: the last one can't be removed. For someone who only looks after one space, put them in that
                                   space's <em>Administrators</em> group instead.</p>
                            </sfe-help>
                        </div>
                    }

                    <div class="access-subhead help-row">
                        <h3>Spaces and groups</h3>
                        <sfe-help about="Spaces and groups">
                            <p>An account sees a space when it is in <strong>at least one of the space's groups</strong>. The groups decide what
                               it may do there; in several groups, it gets the rights of all of them.</p>
                            <p>Example:</p>
                            <ul>
                                <li><em>anna</em> — Family: Administrators · Work: Users</li>
                                <li><em>tom</em> — Family: Users, Kids</li>
                            </ul>
                            <p>Anna manages the Family space and uses the Work space; Tom only sees Family, with the rights of Users and Kids together.
                               Someone in several spaces switches between them from the ⋮ menu.</p>
                        </sfe-help>
                    </div>
                    @if (draft.isServerAdmin) {
                        <p class="field-hint">As a server administrator this account opens every space anyway; groups only matter if it stops being one.</p>
                    }
                    <div class="access-spaces">
                        @for (space of shownSpaces(); track space.id) {
                            @let chosen = groupsChosenIn(space.id);
                            <fieldset class="access-space" [class.empty]="!chosen">
                                <legend>
                                    <span class="access-space-name">{{ space.name }}</span>
                                    @if (!limited() && spaces().length > 1) {
                                        <button type="button" class="link-button" [title]="'Take the account out of ' + space.name" (click)="removeSpace(space.id)">Remove from this space</button>
                                    }
                                </legend>
                                <div class="access-group-choices">
                                    @for (group of groupsOf(space.id); track group.id) {
                                        <label class="access-chip" [class.selected]="draft.groupIds.has(group.id)" [title]="purpose(group)">
                                            <input type="checkbox" [checked]="draft.groupIds.has(group.id)" (change)="toggleGroup(group.id, checkedOf($event))">
                                            <span>{{ group.name }}</span>
                                        </label>
                                    }
                                </div>
                                @if (!chosen) {
                                    <span class="field-hint">In no group: the account won't see {{ space.name }}.</span>
                                }
                            </fieldset>
                        } @empty {
                            <p class="muted">In no space yet: it can sign in but sees nothing.</p>
                        }
                        @if (addable().length) {
                            <div class="access-add-space">
                                <label class="field-label" for="account-add-space">Add to a space</label>
                                <select id="account-add-space" title="Put the account in another space (in its Users group, to start with)" (change)="addSpace($event)">
                                    <option value="" selected>Choose a space…</option>
                                    @for (space of addable(); track space.id) {
                                        <option [value]="space.id">{{ space.name }}</option>
                                    }
                                </select>
                            </div>
                        }
                        @if (draft.otherSpaces) {
                            <p class="field-hint">Also in {{ draft.otherSpaces === 1 ? 'another space' : draft.otherSpaces + ' other spaces' }}, kept as they are.</p>
                        }
                    </div>
                    @if (errors()['groupIds']; as error) {
                        <span class="field-error">{{ error }}</span>
                    }

                    @if (draft.id !== null) {
                        <h3 class="access-subhead">Status</h3>
                        <label class="check">
                            <input type="checkbox" [disabled]="limited()" [checked]="draft.disabled" (change)="change({ disabled: checkedOf($event) })">
                            <span>Disabled<span class="field-hint">Can't sign in; signed out everywhere, and the links it gave out stop working. Its tags stay.</span></span>
                        </label>
                        @if (draft.lockedOut) {
                            <label class="check">
                                <input type="checkbox" [disabled]="limited()" [checked]="draft.unlock" (change)="change({ unlock: checkedOf($event) })">
                                <span>Unlock now<span class="field-hint">Locked for a while after too many wrong passwords.</span></span>
                            </label>
                        }
                    }
                    @if (problem(); as problem) {
                        <p class="form-error" role="alert">{{ problem }}</p>
                    }
                    <button type="submit" hidden aria-hidden="true" tabindex="-1"></button>
                </form>
            }
            <button sheetFooter type="button" class="button" title="Close without saving (Esc)" (click)="sheet.close()">Cancel</button>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button primary" [title]="draft()?.id === null ? 'Create the account' : 'Save the account'"
                    [disabled]="saving()" (click)="save()">{{ draft()?.id === null ? 'Create' : 'Save' }}</button>
        </sfe-sheet>
    `,
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
        return Boolean(draft && draft.id !== null && !this.serverWide() && (draft.otherSpaces > 0 || draft.isServerAdmin));
    });
    protected readonly shownSpaces = computed(() => {
        const ids = this.draft()?.spaceIds ?? [];
        return ids.map(id => this.spaces().find(space => space.id === id)).filter((space): space is SpaceRef => Boolean(space));
    });
    protected readonly addable = computed(() => {
        const shown = new Set(this.draft()?.spaceIds ?? []);
        return this.limited() ? [] : this.spaces().filter(space => !shown.has(space.id));
    });

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    /** Opens the account, or a new one; `space` puts a new account in that space's Users group. */
    open(user: UserInfo | null, space: number | null = null): void {
        const managed = new Set(this.spaces().map(s => s.id));
        const memberships = user?.groups ?? [];
        const here = memberships.filter(m => managed.has(m.tenantId));
        const elsewhere = memberships.filter(m => !managed.has(m.tenantId));
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
            spaceIds: user ? [...new Set(here.map(m => m.tenantId))] : space !== null ? [space] : [],
            groupIds: new Set(user ? here.map(m => m.groupId) : start ? [start.id] : []),
            otherGroupIds: elsewhere.map(m => m.groupId),
            otherSpaces: new Set(elsewhere.map(m => m.tenantId)).size,
        });
        this.source.set(user?.source ?? 'local');
        this.errors.set({});
        this.problem.set(null);
        this.sheet().open();
    }

    protected groupsOf(space: number): GroupInfo[] {
        return this.groups().filter(group => group.tenantId === space);
    }

    protected groupsChosenIn(space: number): number {
        const chosen = this.draft()?.groupIds ?? new Set<number>();
        return this.groupsOf(space).filter(group => chosen.has(group.id)).length;
    }

    protected purpose(group: GroupInfo): string {
        return group.builtIn === 'guests'
            ? 'Its rights are those of visitors without an account; an account in it gets them too'
            : `${group.rights.length} rights`;
    }

    protected change(changes: Partial<AccountDraft>): void {
        this.draft.update(draft => draft && { ...draft, ...changes });
    }

    protected toggleGroup(id: number, member: boolean): void {
        this.draft.update(draft => draft && { ...draft, groupIds: toggled(draft.groupIds, id, member) });
    }

    /** Puts the account in another space, in its Users group to start with. */
    protected addSpace(event: Event): void {
        const select = event.target as HTMLSelectElement;
        const space = Number(select.value);
        select.value = '';
        if (!space)
            return;
        const start = this.usersGroup(space);
        this.draft.update(draft => draft && {
            ...draft,
            spaceIds: [...draft.spaceIds, space],
            groupIds: start ? toggled(draft.groupIds, start.id, true) : draft.groupIds,
        });
    }

    protected removeSpace(space: number): void {
        const ids = new Set(this.groupsOf(space).map(group => group.id));
        this.draft.update(draft => draft && {
            ...draft,
            spaceIds: draft.spaceIds.filter(id => id !== space),
            groupIds: new Set([...draft.groupIds].filter(id => !ids.has(id))),
        });
    }

    protected async save(event?: Event): Promise<void> {
        event?.preventDefault();
        const draft = this.draft();
        if (!draft)
            return;
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
            const user = draft.id === null ? await this.api.createUser(input) : await this.api.updateUser(draft.id, input);
            this.sheet().close();
            this.toasts.show(draft.id === null ? `Account ${input.userName} created.` : `Account ${input.userName} saved.`);
            this.saved.emit(user);
        } catch (error) {
            const errors = fieldErrors(error);
            if (errors)
                this.errors.set(errors);
            else
                this.problem.set((error as Error).message);
        } finally {
            this.saving.set(false);
        }
    }

    private usersGroup(space: number): GroupInfo | undefined {
        const groups = this.groupsOf(space);
        return groups.find(group => group.builtIn === 'users') ?? groups.find(group => group.builtIn !== 'guests');
    }
}
