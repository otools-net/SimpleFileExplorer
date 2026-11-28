import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { BuiltInGroup, GroupInfo, RightDefinition, RightsCatalog, UserInfo } from './access.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { checkedOf, valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { SheetComponent } from '../shared/sheet.component';
import { fieldErrors, rightAreas, rightMark, toggled } from './access-parts';

/** A group being created (`id` null) or changed. */
interface GroupDraft {
    id: number | null;
    name: string;
    externalName: string;
    homeFolder: string;
    builtIn: BuiltInGroup | null;
    rights: ReadonlySet<string>;
    memberIds: ReadonlySet<number>;
}

/**
 * Creates or changes a group of a space: its name, what its members may do (rights), its members and their home
 * folder. The built-in Guests group has no members: its rights are what visitors without an account get, so the rights
 * guests may never have can't be given to it (nor a home folder).
 */
@Component({
    selector: 'sfe-group-editor',
    imports: [SheetComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './group-editor.component.html',
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
    protected readonly presets: { builtIn: BuiltInGroup; label: string }[] = [
        { builtIn: 'administrators', label: $localize`:built-in group:Administrators` },
        { builtIn: 'users', label: $localize`:built-in group:Users` },
        { builtIn: 'guests', label: $localize`:built-in group:Guests` },
    ];

    protected readonly creating = computed(() => this.draft()?.id === null);
    protected readonly heading = computed(() =>
        this.creating() ? $localize`New group` : $localize`Edit the group “${this.draft()?.name ?? ''}:name:”`,
    );
    protected readonly subtitle = computed(() => $localize`In the space ${this.spaceName()}:space:`);
    protected readonly createTitle = $localize`Create the group`;
    protected readonly saveTitle = $localize`Save the group`;

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;
    protected readonly rightMark = rightMark;

    open(group: GroupInfo | null): void {
        this.draft.set({
            id: group?.id ?? null,
            name: group?.name ?? '',
            externalName: group?.externalName ?? '',
            homeFolder: group?.homeFolder ?? '',
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
            return $localize`Visitors without an account can never have this right.`;
        if (this.catalog()?.disabledByServer.includes(right.id))
            return $localize`Switched off for the whole server: nobody gets it.`;
        if (this.catalog()?.disabledInTenant.includes(right.id))
            return $localize`Switched off in this space: nobody gets it here.`;
        return null;
    }

    protected change(changes: Partial<GroupDraft>): void {
        this.draft.update((draft) => draft && { ...draft, ...changes });
    }

    protected usePreset(builtIn: BuiltInGroup): void {
        const rights = (this.catalog()?.rights ?? [])
            .filter((right) => right.scope === 'tenant' && right.defaultGroups.includes(builtIn))
            .map((right) => right.id);
        this.change({ rights: new Set(rights) });
    }

    protected toggleRight(id: string, granted: boolean): void {
        this.draft.update((draft) => draft && { ...draft, rights: toggled(draft.rights, id, granted) });
    }

    protected toggleMember(id: number, member: boolean): void {
        this.draft.update((draft) => draft && { ...draft, memberIds: toggled(draft.memberIds, id, member) });
    }

    protected async save(event?: Event): Promise<void> {
        event?.preventDefault();
        const draft = this.draft();
        if (!draft) return;
        this.saving.set(true);
        this.errors.set({});
        this.problem.set(null);
        const input = {
            name: draft.name.trim(),
            externalName: draft.externalName.trim() || null,
            // Empty: none; the Guests group never shows it (guests have no home folder): kept as it is.
            homeFolder: draft.builtIn === 'guests' ? null : draft.homeFolder.trim(),
            rights: [...draft.rights],
            memberIds: draft.builtIn === 'guests' ? null : [...draft.memberIds],
        };
        try {
            const group =
                draft.id === null
                    ? await this.api.createGroup(input, this.space())
                    : await this.api.updateGroup(draft.id, input, this.space());
            this.sheet().close();
            this.toasts.show(
                draft.id === null
                    ? $localize`Group “${group.name}:name:” created.`
                    : $localize`Group “${group.name}:name:” saved.`,
            );
            this.saved.emit(group);
            // The signed-in account's own rights may have changed.
            if (this.space() === null) await this.config.load();
        } catch (error) {
            const errors = fieldErrors(error);
            if (errors) this.errors.set(errors);
            else this.problem.set((error as Error).message);
        } finally {
            this.saving.set(false);
        }
    }
}
