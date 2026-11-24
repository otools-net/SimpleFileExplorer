import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { Api, API_BASE } from '../core/api.service';
import { markSaved } from '../core/config.service';
import { isFileConflict, LibraryItem } from '../core/models';
import { ToastService } from '../core/toast.service';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { BusyDirective } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { FileVersions, TrashItem } from './file.models';
import {
    noVersions,
    notRestoredText,
    restoredCopyText,
    restoredOverText,
    restoreOverQuestion,
    versionDetails,
    versionHints,
    versionsIntro,
    versionTitle,
} from './version-texts';

/**
 * Previous versions of a library file (its menu, or Properties; agents/docs/features/previous-versions.md): the copies
 * the trash keeps of its content before it was saved over, newest first, each opened in a new tab, restored as a copy
 * next to it or over it.
 */
@Component({
    selector: 'sfe-versions-dialog',
    imports: [SheetComponent, BusyDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './versions.dialog.html',
})
export class VersionsDialog {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly store = inject(ListingStore);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly intro = versionsIntro();
    protected readonly empty = noVersions();
    protected readonly hints = versionHints();
    protected readonly item = signal<LibraryItem | null>(null);
    protected readonly versions = signal<FileVersions | null>(null);
    protected readonly error = signal<string | null>(null);
    /** `{id}:copy` or `{id}:over` while that restore runs. */
    protected readonly busy = signal<string | null>(null);

    /** Answers of an earlier file are dropped. */
    private generation = 0;

    constructor() {
        registerDialog((dialogs) => dialogs.versions, this);
    }

    open(item: LibraryItem): void {
        this.item.set(item);
        this.versions.set(null);
        this.error.set(null);
        this.sheet().open();
        void this.load();
    }

    protected title(version: TrashItem): string {
        return versionTitle(version);
    }

    protected details(version: TrashItem): string {
        return versionDetails(version);
    }

    protected contentUrl(version: TrashItem): string {
        return `${API_BASE}files/versions/${version.id}/content`;
    }

    protected async restoreCopy(version: TrashItem): Promise<void> {
        await this.restore(version, 'copy', async () => {
            const result = await this.api.restoreVersionCopy(version.id);
            if (isFileConflict(result)) return result.detail ?? null;
            this.toasts.show(restoredCopyText(result.name));
            return undefined;
        });
    }

    protected async restoreOver(version: TrashItem): Promise<void> {
        const item = this.item();
        if (!item || !confirm(restoreOverQuestion(item.name, versionTitle(version)))) return;
        await this.restore(version, 'over', async () => {
            const result = await this.api.restoreVersion(version.id);
            if (!('item' in result)) return result.detail ?? null;
            // Its pictures are fetched again where they show.
            if (result.item.modified) markSaved(result.item.path, result.item.modified);
            this.toasts.show(restoredOverText(item.name));
            return undefined;
        });
    }

    /** Runs a restore (`run` gives the reason it was refused, undefined when done), then lists again. */
    private async restore(
        version: TrashItem,
        how: 'copy' | 'over',
        run: () => Promise<string | null | undefined>,
    ): Promise<void> {
        this.busy.set(`${version.id}:${how}`);
        try {
            const refused = await run();
            if (refused !== undefined) {
                this.toasts.error(notRestoredText(refused));
                return;
            }
            void this.store.refresh();
            await this.load();
        } catch (error) {
            this.toasts.error(notRestoredText((error as Error).message));
        } finally {
            this.busy.set(null);
        }
    }

    private async load(): Promise<void> {
        const item = this.item();
        if (!item) return;
        const generation = ++this.generation;
        this.error.set(null);
        try {
            const versions = await this.api.fileVersions(item.path);
            if (generation === this.generation) this.versions.set(versions);
        } catch (error) {
            if (generation === this.generation) this.error.set((error as Error).message);
        }
    }
}
