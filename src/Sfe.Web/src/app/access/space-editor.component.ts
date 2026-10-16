import { ChangeDetectionStrategy, Component, computed, inject, output, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { FolderPicker, RightDefinition, TenantDetails } from '../core/models';
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
 * Creates a space on a folder of the server (picked among the folders the configuration allows, Sfe:Tenants:AllowedRoots:
 * its index is created at once), or renames, disables a space and switches rights off in it (server administrators).
 */
@Component({
    selector: 'sfe-space-editor',
    imports: [IconComponent, SheetComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet [heading]="draft()?.id === null ? 'New space' : 'Space ' + (draft()?.name ?? '')" [wide]="true">
            @if (draft(); as draft) {
                <form class="access-form" novalidate (submit)="save($event)">
                    @if (draft.id === null) {
                        <p class="access-note">
                            A space is a separate home for one library: its own folder, index, tags and settings, and its own groups and
                            accounts. It starts with three groups, Administrators, Users and Guests; you add accounts on its page afterwards.
                        </p>
                    }
                    <div class="field">
                        <label class="field-label" for="space-name">Name</label>
                        <input id="space-name" type="text" autocomplete="off" placeholder="e.g. Family, Work, Holiday photos"
                               [value]="draft.name" (input)="change({ name: valueOf($event) })">
                        <span class="field-hint">Shown in the ⋮ menu and on the sign-in page.</span>
                        @if (errors()['name']; as error) {
                            <span class="field-error">{{ error }}</span>
                        }
                    </div>
                    @if (draft.id === null) {
                        <div class="field">
                            <div class="help-row">
                                <span class="field-label">Library folder</span>
                                <sfe-help about="The library folder">
                                    <p>The folder whose files this space shows, with everything below it. Scans index it, and the space keeps its own
                                       tags, settings, duplicates and background tasks for it.</p>
                                    <p>Example: a <em>Family</em> space on <code>D:\\Media\\Family</code> and a <em>Work</em> space on
                                       <code>D:\\Media\\Work</code>: each sees only its own folder.</p>
                                    <p>Only folders inside those the server's configuration allows can be picked (<code>Sfe:Tenants:AllowedRoots</code>,
                                       else the first space's folder). The folder can't be changed once the space exists.</p>
                                </sfe-help>
                            </div>
                            <div class="folder-picker">
                                <p class="folder-picker-path">
                                    <svg sfeIcon="folder"></svg><span>{{ draft.root || 'Pick a folder below' }}</span>
                                </p>
                                @if (picker(); as picker) {
                                    <ul>
                                        @if (picker.path) {
                                            <li><button type="button" class="link-button" title="The folder above" (click)="browse(picker.parent)">‥</button></li>
                                        }
                                        @for (folder of picker.folders; track folder.path) {
                                            <li>
                                                <button type="button" class="link-button" [title]="'Use ' + folder.path + ', or open it'" (click)="browse(folder.path)">{{ folder.name }}</button>
                                            </li>
                                        } @empty {
                                            <li class="muted">No folders here.</li>
                                        }
                                    </ul>
                                }
                            </div>
                            <span class="field-hint">Click a folder to choose it; click again inside it to go deeper.</span>
                            @if (errors()['root']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <div class="field">
                            <label class="field-label" for="space-slug">Short name <span class="field-hint">(optional)</span></label>
                            <input id="space-slug" type="text" autocomplete="off" spellcheck="false" [value]="draft.slug" placeholder="Made from the name"
                                   (input)="change({ slug: valueOf($event) })" aria-describedby="space-slug-hint">
                            <span class="field-hint" id="space-slug-hint">Lowercase letters, digits and '-': names the space's index and cache folders on the server. It can't change later.</span>
                            @if (errors()['slug']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <label class="check">
                            <input type="checkbox" [checked]="draft.guestAccess" (change)="change({ guestAccess: checkedOf($event) })">
                            <span>Let visitors in without an account<span class="field-hint">With the rights of its Guests group. Can be changed later on the space's page.</span></span>
                        </label>
                    } @else {
                        @if (!draft.isDefault) {
                            <label class="check">
                                <input type="checkbox" [checked]="draft.disabled" (change)="change({ disabled: checkedOf($event) })">
                                <span>Disabled<span class="field-hint">Nobody can open it and its background tasks stop. Its index, tags, groups and settings are kept.</span></span>
                            </label>
                        }
                        <div class="access-subhead help-row">
                            <h3>Rights switched off in this space</h3>
                            <sfe-help about="Switched-off rights">
                                <p>A right ticked here is taken from everyone in this space, whatever their groups say; only server administrators
                                   can tick it, the space's own administrators can't give it back.</p>
                                <p>Example: tick <em>Delete duplicates</em> on a space whose files must never be deleted from the web page.</p>
                                <p><strong>Files read-only</strong> ticks every right that creates, changes or deletes files (marked
                                   <em>Changes files</em>): people can still tag and use the space. <strong>Read-only</strong> ticks every right
                                   that changes anything (marked <em>Changes things</em> too): the space can only be looked at.</p>
                            </sfe-help>
                        </div>
                        <div class="access-presets">
                            <span class="field-hint">Switch off at once</span>
                            <button type="button" class="button small" title="Tick every right that creates, changes or deletes files in this space"
                                    (click)="switchOff(fileChangingRights(rights()))">Files read-only</button>
                            <button type="button" class="button small" title="Tick every right that changes something in this space"
                                    (click)="switchOff(changingRights(rights()))">Read-only</button>
                        </div>
                        <fieldset class="access-choices rights">
                            <legend class="visually-hidden">Rights switched off in this space</legend>
                            @for (right of rights(); track right.id) {
                                @let mark = rightMark(right);
                                <label class="check" [title]="right.description">
                                    <input type="checkbox" [checked]="draft.disabledRights.has(right.id)" (change)="toggleRight(right.id, checkedOf($event))">
                                    <span>
                                        <span class="right-title">{{ right.title }}
                                            @if (mark) {
                                                <span class="access-badge right-mark" [class.danger]="mark.kind === 'files'" [title]="mark.hint">{{ mark.label }}</span>
                                            }
                                        </span>
                                        <span class="field-hint">{{ right.area }}</span>
                                    </span>
                                </label>
                            }
                        </fieldset>
                    }
                    @if (problem(); as problem) {
                        <p class="form-error" role="alert">{{ problem }}</p>
                    }
                    <button type="submit" hidden aria-hidden="true" tabindex="-1"></button>
                </form>
            }
            <button sheetFooter type="button" class="button" title="Close without saving (Esc)" (click)="sheet.close()">Cancel</button>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button primary" [title]="draft()?.id === null ? 'Create the space and index its folder' : 'Save the space'"
                    [disabled]="saving()" (click)="save()">{{ draft()?.id === null ? 'Create' : 'Save' }}</button>
        </sfe-sheet>
    `,
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

    protected readonly rights = computed(() => this.catalog().filter(right => right.scope === 'tenant'));

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
        if (space)
            void this.loadRights();
        else
            void this.browse(null);
        this.sheet().open();
    }

    protected change(changes: Partial<SpaceDraft>): void {
        this.draft.update(draft => draft && { ...draft, ...changes });
    }

    protected toggleRight(id: string, disabled: boolean): void {
        this.draft.update(draft => draft && { ...draft, disabledRights: toggled(draft.disabledRights, id, disabled) });
    }

    /** Ticks `ids` too, keeping the rights already ticked. */
    protected switchOff(ids: string[]): void {
        this.draft.update(draft => draft && { ...draft, disabledRights: new Set([...draft.disabledRights, ...ids]) });
    }

    /** Opens a folder of the picker (the allowed roots for null); a folder opened is the one chosen. */
    protected async browse(path: string | null): Promise<void> {
        try {
            const picker = await this.api.tenantFolders(path);
            this.picker.set(picker);
            if (picker.path)
                this.change({ root: picker.path });
        } catch (error) {
            this.problem.set(`Could not list the folders: ${(error as Error).message}`);
        }
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
            slug: draft.slug.trim() || null,
            root: draft.root || null,
            guestAccess: draft.guestAccess,
            disabled: draft.disabled,
            disabledRights: [...draft.disabledRights],
        };
        try {
            const space = draft.id === null ? await this.api.createTenant(input) : await this.api.updateTenant(draft.id, input);
            this.sheet().close();
            this.toasts.show(draft.id === null ? `Space ${space.name} created.` : `Space ${space.name} saved.`);
            this.saved.emit(space);
            // The ⋮ menu's spaces and this space's rights may have changed.
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

    private async loadRights(): Promise<void> {
        try {
            this.catalog.set((await this.api.rights()).rights);
        } catch (error) {
            this.problem.set(`Could not load the rights: ${(error as Error).message}`);
        }
    }
}
