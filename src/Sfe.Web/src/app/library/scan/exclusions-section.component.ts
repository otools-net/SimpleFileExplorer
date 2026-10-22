import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ExcludedFolder, ExclusionKind, FolderPicker } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { FolderPickerComponent, FolderSource } from '../../shared/folder-picker.component';
import { IconComponent } from '../../shared/icon.component';

const KINDS: { kind: ExclusionKind; label: string; title: string }[] = [
    { kind: 'scan', label: 'Scans', title: 'Scans of the folders above leave it out; what the index has of it stays as it is' },
    { kind: 'trickplay', label: 'Trickplay', title: 'Its videos get no new trickplay' },
    { kind: 'thumbnails', label: 'Thumbnails', title: 'Its photos and videos get no new thumbnails' },
];

/** Sub-folders listed per folder in the picker: a folder with more says so. */
const PICKER_LIMIT = 200;

/**
 * Administration › Scanning › Excluded folders (right `index.exclude`): every folder kept out of scans, trickplay or
 * thumbnails, a box per kind saved at once, Remove, and Add folder… with a picker of the library's folders. The same
 * lists as the folder menu's **Scans and previews…** and the settings tabs' **Excluded folders**.
 */
@Component({
    selector: 'sfe-exclusions-section',
    imports: [BusyDirective, FolderPickerComponent, IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel exclusions-section' },
    template: `
        <div class="exclusions-head">
            <p class="settings-intro">
                Folders kept out of scans, trickplay or thumbnails for the whole space, with their sub-folders. What the index
                already has of a folder kept out of scans stays as it is; trickplay and thumbnails already made are still shown.
            </p>
            @if (!adding()) {
                <button type="button" class="button small" title="Keep another folder out of scans, trickplay or thumbnails" (click)="startAdding()">
                    <svg sfeIcon="plus"></svg>Add folder…
                </button>
            }
        </div>

        @if (adding()) {
            <form class="access-form exclusions-form" novalidate (submit)="add($event)">
                <div class="field">
                    <span class="field-label">Folder</span>
                    <sfe-folder-picker [source]="source" placeholder="Pick a folder of the library below"
                                       (picked)="picked.set($event)" (failed)="problem.set($event)" />
                    <span class="field-hint">Click a folder to choose it; click again inside it to go deeper.</span>
                </div>
                <fieldset class="exclusions-kinds">
                    <legend class="field-label">Keep it out of</legend>
                    @for (entry of kinds; track entry.kind) {
                        <label class="check" [title]="entry.title">
                            <input type="checkbox" [attr.data-kind]="entry.kind" [checked]="draft()[entry.kind]" (change)="setDraft(entry.kind, checkedOf($event))">
                            <span>{{ entry.label }}</span>
                        </label>
                    }
                </fieldset>
                @if (problem(); as problem) {
                    <p class="form-error" role="alert">{{ problem }}</p>
                }
                <div class="exclusions-form-actions">
                    <button type="button" class="button" title="Close without adding anything" (click)="adding.set(false)">Cancel</button>
                    <button type="submit" class="button primary" title="Keep the folder out of what is ticked" [sfeBusy]="saving()"
                            [disabled]="saving() || !picked() || !anyDraft()">Add</button>
                </div>
            </form>
        }

        @if (loading()) {
            <p class="tag-loading"><span class="spinner"></span></p>
        } @else if (!folders().length) {
            <p class="muted exclusions-empty">No folder is excluded: scans, trickplay and thumbnails cover the whole library.</p>
        } @else {
            <div class="exclusions-table">
                <table>
                    <thead>
                        <tr>
                            <th scope="col">Folder</th>
                            @for (entry of kinds; track entry.kind) {
                                <th scope="col" [title]="entry.title">{{ entry.label }}</th>
                            }
                            <th scope="col"><span class="visually-hidden">Actions</span></th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (folder of folders(); track folder.path) {
                            <tr [attr.data-path]="folder.path" [class.missing]="!folder.exists">
                                <td>
                                    @if (folder.exists) {
                                        <a routerLink="/" [queryParams]="{ path: folder.path }" [title]="'Open ' + folder.path">{{ folder.path }}</a>
                                    } @else {
                                        <span>{{ folder.path }}</span>
                                        <span class="exclusion-chip" title="Not on the disk any more (renamed or deleted outside SFE): it can only be removed">folder not found</span>
                                    }
                                </td>
                                @for (entry of kinds; track entry.kind) {
                                    <td>
                                        <input type="checkbox" [attr.data-kind]="entry.kind" [attr.aria-label]="entry.label + ': ' + folder.path"
                                               [title]="entry.title" [checked]="folder[entry.kind]"
                                               [disabled]="busy() === folder.path || (!folder.exists && !folder[entry.kind])"
                                               (change)="toggle(folder, entry.kind, $event)">
                                    </td>
                                }
                                <td class="exclusions-actions">
                                    <button type="button" class="button small" [title]="'Let scans, trickplay and thumbnails into ' + folder.path + ' again'"
                                            [sfeBusy]="busy() === folder.path" [disabled]="busy() !== null" (click)="remove(folder)">Remove</button>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    `,
})
export class ExclusionsSectionComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    protected readonly kinds = KINDS;
    protected readonly folders = signal<ExcludedFolder[]>([]);
    protected readonly loading = signal(true);
    /** The folder being saved. */
    protected readonly busy = signal<string | null>(null);

    protected readonly adding = signal(false);
    protected readonly picked = signal<string | null>(null);
    protected readonly draft = signal<Record<ExclusionKind, boolean>>({ scan: true, trickplay: false, thumbnails: false });
    protected readonly saving = signal(false);
    protected readonly problem = signal<string | null>(null);

    /** The library's folders, as listings show them (folder rules apply). */
    protected readonly source: FolderSource = async path => {
        const folder = path ?? '';
        const listing = await this.api.browse({ path: folder, offset: 0, limit: PICKER_LIMIT, sort: 'name', desc: false });
        const folders = listing.items.filter(item => item.kind === 'directory').map(item => ({ name: item.name, path: item.path }));
        const slash = folder.lastIndexOf('/');
        const picker: FolderPicker = {
            path: folder || null,
            parent: folder ? (slash >= 0 ? folder.slice(0, slash) : '') : null,
            folders,
            truncated: listing.directoryCount > folders.length,
        };
        return picker;
    };

    async ngOnInit(): Promise<void> {
        await this.load();
    }

    private async load(): Promise<void> {
        try {
            this.folders.set(await this.api.folderExclusions());
        } catch (error) {
            this.toasts.error(`Could not list the excluded folders: ${(error as Error).message}`);
        } finally {
            this.loading.set(false);
        }
    }

    protected startAdding(): void {
        this.picked.set(null);
        this.draft.set({ scan: true, trickplay: false, thumbnails: false });
        this.problem.set(null);
        this.adding.set(true);
    }

    protected setDraft(kind: ExclusionKind, value: boolean): void {
        this.draft.update(draft => ({ ...draft, [kind]: value }));
    }

    protected anyDraft(): boolean {
        return Object.values(this.draft()).some(Boolean);
    }

    protected async add(event: Event): Promise<void> {
        event.preventDefault();
        const path = this.picked();
        if (!path)
            return;
        const changes = Object.fromEntries(Object.entries(this.draft()).filter(([, value]) => value));
        this.problem.set(null);
        try {
            await whileBusy(this.saving, () => this.api.setFolderExclusions(path, changes));
            this.adding.set(false);
            await this.load();
        } catch (error) {
            this.problem.set((error as Error).message);
        }
    }

    protected async toggle(folder: ExcludedFolder, kind: ExclusionKind, event: Event): Promise<void> {
        const box = event.target as HTMLInputElement;
        if (!await this.save(folder, { [kind]: box.checked }))
            box.checked = !box.checked;
    }

    protected async remove(folder: ExcludedFolder): Promise<void> {
        await this.save(folder, Object.fromEntries(KINDS.filter(entry => folder[entry.kind]).map(entry => [entry.kind, false])));
    }

    /** False when the server refused it (said in a toast). */
    private async save(folder: ExcludedFolder, changes: Partial<Record<ExclusionKind, boolean>>): Promise<boolean> {
        this.busy.set(folder.path);
        try {
            const state = await this.api.setFolderExclusions(folder.path, changes);
            const updated: ExcludedFolder = { ...folder, scan: state.scan === folder.path, trickplay: state.trickplay === folder.path, thumbnails: state.thumbnails === folder.path };
            this.folders.update(folders => updated.scan || updated.trickplay || updated.thumbnails
                ? folders.map(f => f.path === folder.path ? updated : f)
                : folders.filter(f => f.path !== folder.path));
            return true;
        } catch (error) {
            this.toasts.error(`Could not change “${folder.path}”: ${(error as Error).message}`);
            return false;
        } finally {
            this.busy.set(null);
        }
    }

    protected checkedOf(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }
}
