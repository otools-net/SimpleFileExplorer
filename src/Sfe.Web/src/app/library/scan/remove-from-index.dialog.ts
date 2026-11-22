import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { icu } from '../../core/i18n/icu';
import { IndexedFolder, RemovedFromIndex } from '../library.models';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { SheetComponent } from '../../shared/sheet.component';
import { registerDialog } from '../dialogs.service';
import { ListingStore } from '../listing.store';

/**
 * "Remove from the index": takes a folder and its sub-folders out of the search index (the files stay on the disk),
 * after saying how much that is, and optionally keeps future scans out of it (Administration › Scanning › What to scan
 * › Excluded folders).
 */
@Component({
    selector: 'sfe-remove-from-index',
    imports: [BusyDirective, DecimalPipe, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './remove-from-index.dialog.html',
})
export class RemoveFromIndexDialog {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly store = inject(ListingStore);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly path = signal('');
    protected readonly folder = signal<IndexedFolder | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly exclude = signal(true);
    protected readonly removing = signal(false);

    protected readonly name = computed(() =>
        this.path() ? `“${this.path()}”` : $localize`:the folder a text is about:the whole library`,
    );
    protected readonly counts = computed(() => {
        const folder = this.folder();
        if (!folder) return '';
        return icu(
            $localize`{folders, plural, one {# folder} other {# folders}} and \
{files, plural, one {# file} other {# files}}`,
            { folders: folder.directories, files: folder.files },
        );
    });
    private readonly indexed = computed(() => Boolean(this.folder()?.files || this.folder()?.directories));
    private readonly excluding = computed(() => this.exclude() && !this.folder()?.excluded && this.path() !== '');
    protected readonly canRemove = computed(() => this.indexed() || this.excluding());
    protected readonly actionLabel = computed(() =>
        !this.indexed() && this.excluding() ? $localize`Skip in future scans` : $localize`Remove from index`,
    );
    protected readonly removeTitle = computed(() => $localize`Remove ${this.name()}:folder: from the search index.`);

    constructor() {
        registerDialog((dialogs) => dialogs.removeFromIndex, this);
    }

    /** Opens on `path` (the folder browsed by default) and counts what the index holds of it. */
    async open(path = this.store.route().path): Promise<void> {
        this.path.set(path);
        this.folder.set(null);
        this.error.set(null);
        this.exclude.set(true);
        this.sheet().open();
        try {
            this.folder.set(await this.api.indexedFolder(path));
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }

    protected async remove(): Promise<void> {
        try {
            const result = await whileBusy(this.removing, () =>
                this.api.removeFromIndex(this.path(), this.excluding()),
            );
            if (!('files' in result)) {
                this.toasts.error('A scan is running: try again when it has finished.');
                return;
            }
            this.sheet().close();
            this.toasts.show(this.message(result));
            // Search results may have lost files; a folder listing comes from the disk and is unchanged.
            if (this.store.route().q) void this.store.load();
        } catch (error) {
            this.toasts.error(`Could not remove the folder: ${(error as Error).message}`);
        }
    }

    private message(result: RemovedFromIndex): string {
        const name = result.path ? `“${result.path}”` : $localize`:start of a sentence:The library`;
        const values = { name, count: result.files, skipped: result.excluded ? 'yes' : 'no' };
        return result.files || result.directories
            ? icu(
                  $localize`{skipped, select, \
yes {{count, plural, one {{name} removed from the index (# file); scans will skip it.} \
other {{name} removed from the index (# files); scans will skip it.}}} \
other {{count, plural, one {{name} removed from the index (# file).} \
other {{name} removed from the index (# files).}}}}`,
                  values,
              )
            : icu(
                  $localize`{skipped, select, yes {{name} wasn't indexed; scans will skip it.} \
other {{name} wasn't indexed.}}`,
                  values,
              );
    }

    protected checkedOf(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }
}
