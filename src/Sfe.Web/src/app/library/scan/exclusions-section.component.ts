import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { FolderPicker } from '../../core/models';
import { ExcludedFolder, ExclusionKind } from '../library.models';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { FolderPickerComponent, FolderSource } from '../../shared/folder-picker.component';
import { IconComponent } from '../../shared/icon.component';

const KINDS: { kind: ExclusionKind; label: string; title: string }[] = [
    {
        kind: 'scan',
        label: $localize`Scans`,
        title: $localize`Scans of the folders above leave it out; what the index has of it stays as it is.`,
    },
    { kind: 'trickplay', label: $localize`Trickplay`, title: $localize`Its videos get no new trickplay.` },
    {
        kind: 'thumbnails',
        label: $localize`Thumbnails`,
        title: $localize`Its photos and videos get no new thumbnails.`,
    },
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
    templateUrl: './exclusions-section.component.html',
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
    protected readonly draft = signal<Record<ExclusionKind, boolean>>({
        scan: true,
        trickplay: false,
        thumbnails: false,
    });
    protected readonly saving = signal(false);
    protected readonly problem = signal<string | null>(null);

    protected openTitle(path: string): string {
        return $localize`Open “${path}:folder:”.`;
    }

    /** The label of a folder's box: what it is kept out of, and which folder. */
    protected boxLabel(kind: string, path: string): string {
        return $localize`${kind}:kind: of “${path}:folder:”`;
    }

    protected removeTitle(path: string): string {
        return $localize`Let scans, trickplay and thumbnails into “${path}:folder:” again.`;
    }

    /** The library's folders, as listings show them (folder rules apply). */
    protected readonly source: FolderSource = async (path) => {
        const folder = path ?? '';
        const listing = await this.api.browse({
            path: folder,
            offset: 0,
            limit: PICKER_LIMIT,
            sort: 'name',
            desc: false,
        });
        const folders = listing.items
            .filter((item) => item.kind === 'directory')
            .map((item) => ({ name: item.name, path: item.path }));
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
            this.toasts.error($localize`Could not list the excluded folders: ${(error as Error).message}:reason:`);
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
        this.draft.update((draft) => ({ ...draft, [kind]: value }));
    }

    protected anyDraft(): boolean {
        return Object.values(this.draft()).some(Boolean);
    }

    protected async add(event: Event): Promise<void> {
        event.preventDefault();
        const path = this.picked();
        if (!path) return;
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
        if (!(await this.save(folder, { [kind]: box.checked }))) box.checked = !box.checked;
    }

    protected async remove(folder: ExcludedFolder): Promise<void> {
        await this.save(
            folder,
            Object.fromEntries(KINDS.filter((entry) => folder[entry.kind]).map((entry) => [entry.kind, false])),
        );
    }

    /** False when the server refused it (said in a toast). */
    private async save(folder: ExcludedFolder, changes: Partial<Record<ExclusionKind, boolean>>): Promise<boolean> {
        this.busy.set(folder.path);
        try {
            const state = await this.api.setFolderExclusions(folder.path, changes);
            const updated: ExcludedFolder = {
                ...folder,
                scan: state.scan === folder.path,
                trickplay: state.trickplay === folder.path,
                thumbnails: state.thumbnails === folder.path,
            };
            this.folders.update((folders) =>
                updated.scan || updated.trickplay || updated.thumbnails
                    ? folders.map((f) => (f.path === folder.path ? updated : f))
                    : folders.filter((f) => f.path !== folder.path),
            );
            return true;
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error(
                $localize`Could not change what “${folder.path}:folder:” is kept out of: ${reason}:reason:`,
            );


            return false;
        } finally {
            this.busy.set(null);
        }
    }

    protected checkedOf(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }
}
