import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { BuiltInGroup, GroupInfo, RightDefinition, RightsCatalog, UserInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { checkedOf, valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { SheetComponent } from '../shared/sheet.component';
import { fieldErrors, rightAreas, toggled } from './access-parts';

/** A group being created (`id` null) or changed. */
interface GroupDraft {
    id: number | null;
    name: string;
    externalName: string;
    builtIn: BuiltInGroup | null;
    rights: ReadonlySet<string>;
    memberIds: ReadonlySet<number>;
}

/**
 * Creates or changes a group of a space: its name, what its members may do (rights), and its members. The built-in
 * Guests group has no members: its rights are what visitors without an account get, so the rights guests may never
 * have can't be given to it.
 */
@Component({
    selector: 'sfe-group-editor',
    imports: [SheetComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet [heading]="draft()?.id === null ? 'New group' : 'Group ' + (draft()?.name ?? '')" [subtitle]="'In the space ' + spaceName()" [wide]="true">
            @if (draft(); as draft) {
                <form class="access-form" novalidate (submit)="save($event)">
                    @if (draft.builtIn === 'guests') {
                        <p class="access-note">
                            The rights below are what <strong>visitors without an account</strong> may do in this space, when the space lets them in
                            (the space's "Visitors" part). Nobody is a member of this group: it stands for anyone who reaches the server.
                        </p>
                    }
                    <div class="field">
                        <label class="field-label" for="group-name">Name</label>
                        <input id="group-name" type="text" autocomplete="off" placeholder="e.g. Kids, Family, Friends" [disabled]="draft.builtIn !== null"
                               [value]="draft.name" (input)="change({ name: valueOf($event) })">
                        @if (draft.builtIn) {
                            <span class="field-hint">Built-in groups keep their name.</span>
                        }
                        @if (errors()['name']; as error) {
                            <span class="field-error">{{ error }}</span>
                        }
                    </div>

                    <div class="access-subhead help-row">
                        <h3>What its members may do</h3>
                        <sfe-help about="Rights">
                            <p>Each box is a <strong>right</strong>: something the group's members may do in this space. Leave it unticked and they can't.</p>
                            <p>An account in several groups gets the rights of all of them together. Example: <em>tom</em> is in
                               <em>Users</em> (browse, play, tag) and in <em>Downloaders</em> (download files): he may do all four.</p>
                            <p>A right greyed out can't be given here: guests may never change anything, and an administrator of the server
                               may switch a right off for a whole space or for the whole server.</p>
                        </sfe-help>
                    </div>
                    @if (draft.id === null) {
                        <div class="access-presets">
                            <span class="field-hint">Start from the rights of</span>
                            @for (preset of presets; track preset.builtIn) {
                                <button type="button" class="button small" [title]="'Tick the rights a new space gives its ' + preset.label + ' group'"
                                        (click)="usePreset(preset.builtIn)">{{ preset.label }}</button>
                            }
                        </div>
                    }
                    @for (area of areas(); track area.title) {
                        <fieldset class="access-choices rights">
                            <legend class="field-label">{{ area.title }}</legend>
                            @for (right of area.rights; track right.id) {
                                @let note = rightNote(right, draft);
                                <label class="check" [title]="right.description">
                                    <input type="checkbox" [disabled]="note !== null && !draft.rights.has(right.id)" [checked]="draft.rights.has(right.id)"
                                           (change)="toggleRight(right.id, checkedOf($event))">
                                    <span>{{ right.title }}<span class="field-hint">{{ note ?? right.description }}</span></span>
                                </label>
                            }
                        </fieldset>
                    }
                    @if (errors()['rights']; as error) {
                        <span class="field-error">{{ error }}</span>
                    }

                    @if (draft.builtIn !== 'guests') {
                        <div class="access-subhead help-row">
                            <h3>Members</h3>
                            <sfe-help about="Members">
                                <p>The accounts in this group. Ticking an account that isn't in the space yet brings it in: an account sees a space
                                   as soon as it is in one of the space's groups.</p>
                                <p>Accounts are created on the space's page (New account) or under Accounts.</p>
                            </sfe-help>
                        </div>
                        <fieldset class="access-choices members">
                            <legend class="visually-hidden">Members</legend>
                            @for (user of candidates(); track user.id) {
                                <label class="check">
                                    <input type="checkbox" [checked]="draft.memberIds.has(user.id)" (change)="toggleMember(user.id, checkedOf($event))">
                                    <span>{{ user.displayName || user.userName }}
                                        <span class="field-hint">{{ user.displayName ? user.userName : '' }}{{ user.kind === 'guest' ? (user.displayName ? ' · ' : '') + 'guest account' : '' }}</span>
                                    </span>
                                </label>
                            } @empty {
                                <p class="muted">No accounts yet: create one first.</p>
                            }
                        </fieldset>
                    }

                    <div class="field">
                        <div class="help-row">
                            <label class="field-label" for="group-external">Directory group <span class="field-hint">(optional, advanced)</span></label>
                            <sfe-help about="Directory group">
                                <p>Only for servers that check passwords against a directory such as LDAP or Active Directory. Name a group of
                                   that directory here (e.g. <code>CN=Media,OU=Groups,DC=home</code>): its members join this group each time
                                   they sign in, and leave it when the directory drops them.</p>
                                <p>Leave it empty to choose the members by hand, above.</p>
                            </sfe-help>
                        </div>
                        <input id="group-external" type="text" autocomplete="off" spellcheck="false" [value]="draft.externalName" (input)="change({ externalName: valueOf($event) })">
                    </div>
                    @if (problem(); as problem) {
                        <p class="form-error" role="alert">{{ problem }}</p>
                    }
                    <button type="submit" hidden aria-hidden="true" tabindex="-1"></button>
                </form>
            }
            <button sheetFooter type="button" class="button" title="Close without saving (Esc)" (click)="sheet.close()">Cancel</button>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button primary" [title]="draft()?.id === null ? 'Create the group' : 'Save the group'"
                    [disabled]="saving()" (click)="save()">{{ draft()?.id === null ? 'Create' : 'Save' }}</button>
        </sfe-sheet>
    `,
})
export class GroupEditorComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly sheet = viewChild.required<SheetComponent>('sheet');

    /** The space's id for another space than the session's (server administrators), null for the session's. */
    readonly space = input<number | null>(null);
    readonly spaceName = input.required<string>();
    /** The accounts that can be made members. */
    readonly candidates = input.required<UserInfo[]>();
    readonly catalog = input.required<RightsCatalog | null>();
    readonly saved = output<GroupInfo>();

    protected readonly draft = signal<GroupDraft | null>(null);
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);
    protected readonly saving = signal(false);

    protected readonly areas = computed(() => rightAreas(this.catalog()?.rights ?? []));
    protected readonly presets: { builtIn: BuiltInGroup; label: string }[] =
        [{ builtIn: 'administrators', label: 'Administrators' }, { builtIn: 'users', label: 'Users' }, { builtIn: 'guests', label: 'Guests' }];

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    open(group: GroupInfo | null): void {
        this.draft.set({
            id: group?.id ?? null,
            name: group?.name ?? '',
            externalName: group?.externalName ?? '',
            builtIn: group?.builtIn ?? null,
            rights: new Set(group?.rights ?? [Rights.browse, Rights.viewFiles, Rights.viewTags]),
            memberIds: new Set(group?.memberIds ?? []),
        });
        this.errors.set({});
        this.problem.set(null);
        this.sheet().open();
    }

    /** Why a right can't be given to the group (guests, switched off); null when it can. */
    protected rightNote(right: RightDefinition, draft: GroupDraft): string | null {
        if (draft.builtIn === 'guests' && !right.guestAllowed)
            return 'Visitors without an account can never have it.';
        if (this.catalog()?.disabledByServer.includes(right.id))
            return 'Switched off for the whole server: nobody gets it.';
        if (this.catalog()?.disabledInTenant.includes(right.id))
            return 'Switched off in this space: nobody gets it here.';
        return null;
    }

    protected change(changes: Partial<GroupDraft>): void {
        this.draft.update(draft => draft && { ...draft, ...changes });
    }

    protected usePreset(builtIn: BuiltInGroup): void {
        const rights = (this.catalog()?.rights ?? []).filter(right => right.scope === 'tenant' && right.defaultGroups.includes(builtIn)).map(right => right.id);
        this.change({ rights: new Set(rights) });
    }

    protected toggleRight(id: string, granted: boolean): void {
        this.draft.update(draft => draft && { ...draft, rights: toggled(draft.rights, id, granted) });
    }

    protected toggleMember(id: number, member: boolean): void {
        this.draft.update(draft => draft && { ...draft, memberIds: toggled(draft.memberIds, id, member) });
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
            name: draft.name.trim(),
            externalName: draft.externalName.trim() || null,
            rights: [...draft.rights],
            memberIds: draft.builtIn === 'guests' ? null : [...draft.memberIds],
        };
        try {
            const group = draft.id === null
                ? await this.api.createGroup(input, this.space())
                : await this.api.updateGroup(draft.id, input, this.space());
            this.sheet().close();
            this.toasts.show(draft.id === null ? `Group ${group.name} created.` : `Group ${group.name} saved.`);
            this.saved.emit(group);
            // The signed-in account's own rights may have changed.
            if (this.space() === null)
                await this.config.load();
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
}
