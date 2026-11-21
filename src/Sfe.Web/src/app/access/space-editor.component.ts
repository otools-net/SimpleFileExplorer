import { ChangeDetectionStrategy, Component, computed, inject, output, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { FolderPicker } from '../core/models';
import { RightDefinition, TenantDetails } from './access.models';
import { ToastService } from '../core/toast.service';
import { checkedOf, valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { SheetComponent } from '../shared/sheet.component';
import { changingRights, fieldErrors, fileChangingRights, rightMark, toggled } from './access-parts';

/** A space being created (`id` null) or changed. */
interface SpaceDraft {
    id: number | null;
    name: string;
    slug: string;
    root: string;
    guestAccess: boolean;
    disabled: boolean;
    isDefault: boolean;
    disabledRights: ReadonlySet<string>;
}

/**
 * Creates a space on a folder of the server (picked among the folders the configuration allows,
 * Sfe:Tenants:AllowedRoots: its index is created at once), or renames, disables a space and switches rights off in it
 * (server administrators).
 */
@Component({
    selector: 'sfe-space-editor',
    imports: [IconComponent, SheetComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './space-editor.component.html',
})
export class SpaceEditorComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly sheet = viewChild.required<SheetComponent>('sheet');

    readonly saved = output<TenantDetails>();

    protected readonly draft = signal<SpaceDraft | null>(null);
    protected readonly picker = signal<FolderPicker | null>(null);
    private readonly catalog = signal<RightDefinition[]>([]);
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);
    protected readonly saving = signal(false);

    protected readonly rights = computed(() => this.catalog().filter((right) => right.scope === 'tenant'));
    protected readonly creating = computed(() => this.draft()?.id === null);
    protected readonly heading = computed(() =>
        this.creating() ? $localize`New space` : $localize`Edit the space “${this.draft()?.name ?? ''}:name:”`,
    );
    protected readonly createTitle = $localize`Create the space and index its folder`;
    protected readonly saveTitle = $localize`Save the space`;

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;
    protected readonly rightMark = rightMark;
    protected readonly fileChangingRights = fileChangingRights;
    protected readonly changingRights = changingRights;

    open(space: TenantDetails | null): void {
        this.draft.set({
            id: space?.id ?? null,
            name: space?.name ?? '',
            slug: space?.slug ?? '',
            root: space?.root ?? '',
            guestAccess: space?.guestAccess ?? false,
            disabled: space?.disabled ?? false,
            isDefault: space?.isDefault ?? false,
            disabledRights: new Set(space?.disabledRights ?? []),
        });
        this.errors.set({});
        this.problem.set(null);
        this.picker.set(null);
        if (space) void this.loadRights();
        else void this.browse(null);
        this.sheet().open();
    }

    protected change(changes: Partial<SpaceDraft>): void {
        this.draft.update((draft) => draft && { ...draft, ...changes });
    }

    protected toggleRight(id: string, disabled: boolean): void {
        this.draft.update(
            (draft) => draft && { ...draft, disabledRights: toggled(draft.disabledRights, id, disabled) },
        );
    }

    /** Ticks `ids` too, keeping the rights already ticked. */
    protected switchOff(ids: string[]): void {
        this.draft.update((draft) => draft && { ...draft, disabledRights: new Set([...draft.disabledRights, ...ids]) });
    }

    /** Opens a folder of the picker (the allowed roots for null); a folder opened is the one chosen. */
    protected async browse(path: string | null): Promise<void> {
        try {
            const picker = await this.api.tenantFolders(path);
            this.picker.set(picker);
            if (picker.path) this.change({ root: picker.path });
        } catch (error) {
            this.problem.set($localize`Could not list the folders: ${(error as Error).message}:reason:`);
        }
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
            slug: draft.slug.trim() || null,
            root: draft.root || null,
            guestAccess: draft.guestAccess,
            disabled: draft.disabled,
            disabledRights: [...draft.disabledRights],
        };
        try {
            const space =
                draft.id === null ? await this.api.createTenant(input) : await this.api.updateTenant(draft.id, input);
            this.sheet().close();
            this.toasts.show(draft.id === null ? `Space ${space.name} created.` : `Space ${space.name} saved.`);
            this.saved.emit(space);
            // The ⋮ menu's spaces and this space's rights may have changed.
            await this.config.load();
        } catch (error) {
            const errors = fieldErrors(error);
            if (errors) this.errors.set(errors);
            else this.problem.set((error as Error).message);
        } finally {
            this.saving.set(false);
        }
    }

    private async loadRights(): Promise<void> {
        try {
            this.catalog.set((await this.api.rights()).rights);
        } catch (error) {
            this.problem.set($localize`Could not load the rights: ${(error as Error).message}:reason:`);
        }
    }
}
