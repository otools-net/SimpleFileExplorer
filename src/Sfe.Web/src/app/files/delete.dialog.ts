import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { formatSize, joinParts, plural } from '../core/format';
import { DeletedItem, isFileConflict, LibraryItem } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { currentPosition } from '../library/items/listing-position';
import { ListingStore } from '../library/listing.store';
import { FileActions } from './file-actions';

/** Names listed in the confirmation, the rest counted. */
const NAMES_SHOWN = 5;

/**
 * Asks before deleting files and folders (Delete; Shift+Delete or the space's setting: for good), then deletes them:
 * the listing loses them at once, a toast offers to undo a delete to the trash, and what could not be deleted is
 * said. Items that can't go to the trash (a folder with links on another drive) can then be deleted for good, when the
 * session may.
 */
@Component({
    selector: 'sfe-delete-dialog',
    imports: [BusyDirective, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet [heading]="permanent() ? 'Delete permanently' : 'Delete'" variant="delete-dialog">
            @if (unavailable().length) {
                <p>{{ unavailableText() }}</p>
            } @else {
                <p>{{ question() }}</p>
                @if (permanent()) {
                    <p class="form-error">This can't be undone: they don't go to the trash.</p>
                }
            }
            <ul class="delete-names">
                @for (name of names(); track $index) {
                    <li>{{ name }}</li>
                }
            </ul>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without deleting anything (Esc)" (click)="sheet.close()">Cancel</button>
            @if (unavailable().length) {
                @if (canForce()) {
                    <button sheetFooter type="button" class="button danger" title="Delete them for good, without the trash" [sfeBusy]="deleting()"
                            [disabled]="deleting()" (click)="force()">Delete permanently</button>
                }
            } @else {
                <button sheetFooter type="button" class="button danger" [title]="permanent() ? 'Delete them for good' : 'Move them to the trash'"
                        [sfeBusy]="deleting()" [disabled]="deleting()" (click)="confirm()">{{ permanent() ? 'Delete permanently' : 'Delete' }}</button>
            }
        </sfe-sheet>
    `,
})
export class DeleteDialog {
    private readonly api = inject(Api);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly actions = inject(FileActions);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly items = signal<LibraryItem[]>([]);
    protected readonly permanent = signal(false);
    protected readonly deleting = signal(false);
    /** Those that couldn't go to the trash, offered for a permanent delete. */
    protected readonly unavailable = signal<LibraryItem[]>([]);

    private readonly shown = computed(() => this.unavailable().length ? this.unavailable() : this.items());
    protected readonly names = computed(() => {
        const items = this.shown();
        const names = items.slice(0, NAMES_SHOWN).map(item => item.name);
        return items.length > NAMES_SHOWN ? [...names, `and ${plural(items.length - NAMES_SHOWN, 'more')}`] : names;
    });
    protected readonly question = computed(() => {
        const items = this.items();
        const folders = items.filter(item => item.kind === 'directory').length;
        const files = items.length - folders;
        const what = joinParts([folders ? plural(folders, 'folder') : null, files ? plural(files, 'file') : null], ' and ');
        const bytes = items.reduce((sum, item) => sum + (item.size ?? 0), 0);
        const size = files ? ` (${formatSize(bytes)}${folders ? ', and what the folders hold' : ''})` : folders ? ' and what they hold' : '';
        return this.permanent() ? `Delete ${what}${size} for good?` : `Move ${what}${size} to the trash?`;
    });
    protected readonly unavailableText = computed(() =>
        `${plural(this.unavailable().length, 'item')} can't go to the trash: on another drive than the trash, holding links.`
        + (this.canForce() ? ' Delete them permanently instead?' : ''));
    protected readonly canForce = computed(() => this.unavailable().every(item => this.actions.canDeletePermanently(item)));

    constructor() {
        registerDialog(dialogs => dialogs.delete, this);
    }

    open(items: LibraryItem[], permanent: boolean): void {
        this.items.set(items);
        this.permanent.set(permanent);
        this.unavailable.set([]);
        this.sheet().open();
    }

    protected async confirm(): Promise<void> {
        await this.run(this.items(), this.permanent());
    }

    protected async force(): Promise<void> {
        await this.run(this.unavailable(), true);
    }

    private async run(items: LibraryItem[], permanent: boolean): Promise<void> {
        let results: DeletedItem[];
        try {
            results = (await whileBusy(this.deleting, () => this.api.deleteItems(items.map(item => item.path), permanent))).items;
        } catch (error) {
            this.toasts.error(`Could not delete: ${(error as Error).message}`);
            return;
        }
        const done = results.filter(result => result.status === 'done');
        this.store.applyRemoved(done.map(result => result.path));
        const trashed = done.map(result => result.trashId).filter((id): id is number => typeof id === 'number');
        if (done.length)
            this.toasts.show(permanent || !trashed.length ? `${plural(done.length, 'item')} deleted for good.` : `${plural(done.length, 'item')} moved to the trash.`, {
                timeout: trashed.length ? 10000 : 4500,
                action: trashed.length ? { label: 'Undo', title: 'Put them back where they were', run: () => void this.undo(trashed) } : undefined,
            });

        const unavailable = results.filter(result => result.status === 'trashUnavailable').map(result => result.path);
        const failed = results.filter(result => result.status !== 'done' && result.status !== 'trashUnavailable');
        if (failed.length)
            this.toasts.error(failed.length === 1
                ? failed[0].message ?? `“${baseOf(failed[0].path)}” could not be deleted.`
                : `${plural(failed.length, 'item')} could not be deleted: ${failed[0].message ?? baseOf(failed[0].path)}`, 8000);
        if (unavailable.length) {
            this.unavailable.set(items.filter(item => unavailable.includes(item.path)));
            this.permanent.set(true);
            return;
        }
        this.sheet().close();
    }

    /** Puts trashed items back, then lists the folder again where it was scrolled. */
    private async undo(ids: number[]): Promise<void> {
        let restored = 0;
        for (const id of ids) {
            try {
                const result = await this.api.restoreFromTrash(id);
                if (isFileConflict(result))
                    this.toasts.error(result.detail ?? 'It could not be restored.');
                else
                    restored++;
            } catch (error) {
                this.toasts.error(`Could not restore: ${(error as Error).message}`);
            }
        }
        if (restored) {
            void this.store.load({ ...currentPosition(), loaded: this.store.items().length + restored });
            this.toasts.show(`${plural(restored, 'item')} put back.`);
        }
    }
}

function baseOf(path: string): string {
    return path.split('/').pop() ?? path;
}
