import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { DeletedItem, isFileConflict, LibraryItem } from '../core/models';
import { PlaceFiles } from '../core/place-files';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { currentPosition } from '../library/items/listing-position';
import { ListingStore } from '../library/listing.store';
import { FileActions } from './file-actions';
import { deletedToast, deleteQuestion, moreNames, notDeleted, putBack, trashUnavailable } from './file-dialog-texts';

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
    templateUrl: './delete.dialog.html',
})
export class DeleteDialog {
    private readonly api = inject(Api);
    private readonly files = inject(PlaceFiles);
    protected readonly place = inject(ConfigService).place;
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly actions = inject(FileActions);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly items = signal<LibraryItem[]>([]);
    protected readonly permanent = signal(false);
    protected readonly deleting = signal(false);
    /** Those that couldn't go to the trash, offered for a permanent delete. */
    protected readonly unavailable = signal<LibraryItem[]>([]);

    private readonly shown = computed(() => (this.unavailable().length ? this.unavailable() : this.items()));
    protected readonly names = computed(() => {
        const items = this.shown();
        const names = items.slice(0, NAMES_SHOWN).map((item) => item.name);
        return items.length > NAMES_SHOWN ? [...names, moreNames(items.length - NAMES_SHOWN)] : names;
    });
    protected readonly heading = computed(() =>
        this.permanent() ? $localize`Delete permanently` : $localize`:button and dialog title:Delete`,
    );
    protected readonly confirmTitle = computed(() =>
        this.permanent() ? $localize`Delete them for good.` : $localize`Move them to the trash.`,
    );
    protected readonly question = computed(() => {
        const place = this.place();
        return deleteQuestion(this.items(), place ? place.name || '' : null, this.permanent());
    });
    /** On a connection: what becomes of them there (no trash of the app, no permanent choice). */
    protected readonly placeNote = computed(() => {
        const place = this.place();
        if (!place) return null;
        return place.ownTrash
            ? $localize`The connection keeps deleted files in its own trash for a while.`
            : $localize`This can't be undone.`;
    });
    protected readonly unavailableText = computed(() => trashUnavailable(this.unavailable().length, this.canForce()));
    protected readonly canForce = computed(() =>
        this.unavailable().every((item) => this.actions.canDeletePermanently(item)),
    );

    constructor() {
        registerDialog((dialogs) => dialogs.delete, this);
    }

    open(items: LibraryItem[], permanent: boolean): void {
        this.items.set(items);
        this.permanent.set(permanent && !this.place());
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
            results = (
                await whileBusy(this.deleting, () =>
                    this.files.delete(
                        items.map((item) => item.path),
                        permanent,
                    ),
                )
            ).items;
        } catch (error) {
            this.toasts.error(`Could not delete: ${(error as Error).message}`);
            return;
        }
        const done = results.filter((result) => result.status === 'done');
        this.store.applyRemoved(done.map((result) => result.path));
        const trashed = done.map((result) => result.trashId).filter((id): id is number => typeof id === 'number');
        if (done.length)
            this.toasts.show(
                deletedToast(
                    done.length,
                    this.place() ? 'connection' : permanent || !trashed.length ? 'permanent' : 'trash',
                ),
                {
                    timeout: trashed.length ? 10000 : 4500,
                    action: trashed.length
                        ? { label: 'Undo', title: 'Put them back where they were', run: () => void this.undo(trashed) }
                        : undefined,
                },
            );

        const unavailable = results
            .filter((result) => result.status === 'trashUnavailable')
            .map((result) => result.path);
        const failed = results.filter((result) => result.status !== 'done' && result.status !== 'trashUnavailable');
        if (failed.length)
            this.toasts.error(
                failed.length === 1
                    ? (failed[0].message ?? `“${baseOf(failed[0].path)}” could not be deleted.`)
                    : notDeleted(failed.length, failed[0].message ?? baseOf(failed[0].path)),
                8000,
            );
        if (unavailable.length) {
            this.unavailable.set(items.filter((item) => unavailable.includes(item.path)));
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
                if (isFileConflict(result)) this.toasts.error(result.detail ?? 'It could not be restored.');
                else restored++;
            } catch (error) {
                this.toasts.error(`Could not restore: ${(error as Error).message}`);
            }
        }
        if (restored) {
            void this.store.load({ ...currentPosition(), loaded: this.store.items().length + restored });
            this.toasts.show(putBack(restored));
        }
    }
}

function baseOf(path: string): string {
    return path.split('/').pop() ?? path;
}
