import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { joinParts, plural } from '../../core/format';
import { IndexedFolder, RemovedFromIndex } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { SheetComponent } from '../../shared/sheet.component';
import { registerDialog } from '../dialogs.service';
import { ListingStore } from '../listing.store';

/**
 * "Remove from the index": takes a folder and its sub-folders out of the search index (the files stay on the disk),
 * after saying how much that is, and optionally keeps future scans out of it (Administration › Scanning › What to scan › Excluded folders).
 */
@Component({
    selector: 'sfe-remove-from-index',
    imports: [BusyDirective, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Remove from the index" [subtitle]="name()" variant="remove-index">
            @if (error()) {
                <p class="form-error">{{ error() }}</p>
            } @else if (folder(); as folder) {
                @if (folder.files || folder.directories) {
                    <p>The index holds <strong>{{ counts() }}</strong> of {{ name() }} and its sub-folders. Search will no longer find them; the files themselves stay on the disk, and browsing still shows them.</p>
                    @if (folder.taggedFiles) {
                        <p class="field-hint">{{ plural(folder.taggedFiles, 'file has', 'files have') }} tags. They are kept with the moved files (Duplicates &amp; moved files) unless “Keep the tags of moved files” is off: scanning the folder again gives the tags back.</p>
                    }
                } @else {
                    <p>Nothing of {{ name() }} is in the index.</p>
                }
                @if (folder.path) {
                    <label class="check">
                        <input type="checkbox" [checked]="exclude()" [disabled]="folder.excluded" (change)="exclude.set(checkedOf($event))">
                        <span>
                            <strong>Skip this folder in future scans</strong>
                            <span class="field-hint">{{ folder.excluded
                                ? 'Already skipped: it is in Administration › Scanning › What to scan › Excluded folders.'
                                : 'Adds it to Administration › Scanning › What to scan › Excluded folders, so that scans of the folders above it leave it out. Without it, the next scan of a parent folder indexes it again.' }}</span>
                        </span>
                    </label>
                }
            } @else {
                <p class="muted">Counting what the index holds…</p>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without changing anything (Esc)" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="button" class="button primary" [title]="'Remove ' + name() + ' from the search index'" [sfeBusy]="removing()"
                    [disabled]="removing() || !canRemove()" (click)="remove()">{{ actionLabel() }}</button>
        </sfe-sheet>
    `,
})
export class RemoveFromIndexDialog {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly store = inject(ListingStore);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly plural = plural;
    protected readonly path = signal('');
    protected readonly folder = signal<IndexedFolder | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly exclude = signal(true);
    protected readonly removing = signal(false);

    protected readonly name = computed(() => this.path() ? `“${this.path()}”` : 'the whole library');
    protected readonly counts = computed(() => {
        const folder = this.folder();
        return folder ? joinParts([plural(folder.directories, 'folder'), plural(folder.files, 'file')], ' and ') : '';
    });
    private readonly indexed = computed(() => Boolean(this.folder()?.files || this.folder()?.directories));
    private readonly excluding = computed(() => this.exclude() && !this.folder()?.excluded && this.path() !== '');
    protected readonly canRemove = computed(() => this.indexed() || this.excluding());
    protected readonly actionLabel = computed(() => !this.indexed() && this.excluding() ? 'Skip in future scans' : 'Remove from index');

    constructor() {
        registerDialog(dialogs => dialogs.removeFromIndex, this);
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
            this.error.set(`Could not read the index: ${(error as Error).message}`);
        }
    }

    protected async remove(): Promise<void> {
        try {
            const result = await whileBusy(this.removing, () => this.api.removeFromIndex(this.path(), this.excluding()));
            if (!('files' in result)) {
                this.toasts.error('A scan is running: try again when it has finished.');
                return;
            }
            this.sheet().close();
            this.toasts.show(this.message(result));
            // Search results may have lost files; a folder listing comes from the disk and is unchanged.
            if (this.store.route().q)
                void this.store.load();
        } catch (error) {
            this.toasts.error(`Could not remove the folder: ${(error as Error).message}`);
        }
    }

    private message(result: RemovedFromIndex): string {
        const name = result.path ? `“${result.path}”` : 'The library';
        return joinParts([
            result.files || result.directories ? `${name} removed from the index (${plural(result.files, 'file')})` : `${name} was not indexed`,
            result.excluded ? 'scans will skip it' : null,
        ], '; ') + '.';
    }

    protected checkedOf(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }
}
