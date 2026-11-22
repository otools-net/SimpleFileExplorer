import { ChangeDetectionStrategy, Component, computed, ElementRef, inject } from '@angular/core';
import { icu } from '../core/i18n/icu';
import { FileDownloads } from '../files/downloads';
import { extractEachTitle } from '../files/extract.dialog';
import { FileActions } from '../files/file-actions';
import { FileTransfers } from '../files/file-transfers';
import { ItemMenu } from '../files/item-menu';
import { LibraryItem } from '../core/models';
import { MenuEntry } from '../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../menus/overflow-row';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from './dialogs.service';
import { QuickLinksService } from './quick-links/quick-links.service';
import { ListingStore } from './listing.store';

/**
 * The bar at the bottom while files or folders are selected: their count, select all, adding them to the quick links,
 * renaming the one selected, moving, copying, downloading and deleting them, the tags of the files selected.
 * One line: on a narrow screen the count shortens ("15 selected"), then the buttons leave from the right (Download
 * first, Quick links last) into its "⋯" menu; Clear, the count, Select all and Tags stay. The host is a full-width
 * strip at the bottom, the bar centred in it: the room the bar has is measured there (`overflowIn`).
 */
@Component({
    selector: 'sfe-selection-bar',
    imports: [IconComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'selection-dock', role: 'region', '[attr.aria-label]': 'label', '[hidden]': '!count()' },
    templateUrl: './selection-bar.component.html',
})
export class SelectionBarComponent {
    protected readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    protected readonly quickLinks = inject(QuickLinksService);

    protected readonly menu = inject(ItemMenu);
    private readonly fileActions = inject(FileActions);
    protected readonly transfers = inject(FileTransfers);
    private readonly downloads = inject(FileDownloads);

    protected readonly count = computed(() => this.store.selection().size);
    private readonly selected = computed(() => this.store.selectedItems());
    protected readonly files = computed(() => this.selected().filter((item) => item.kind !== 'directory').length);
    private readonly folders = computed(() => this.selected().length - this.files());
    protected readonly loaded = computed(() => this.store.items().length);
    protected readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    protected readonly label = $localize`Selected items`;
    /** The count when the bar is narrow: "15 selected". */
    protected readonly shortCountText = computed(() =>
        icu($localize`{count, plural, other {# selected}}`, { count: this.count() }),
    );
    protected readonly countText = computed(() => {
        const [folders, files] = [this.folders(), this.files()];
        if (folders && files)
            return icu(
                $localize`{folders, plural, one {# folder} other {# folders}} and \
{files, plural, one {# file} other {# files}} selected`,
                { folders, files },
            );
        if (folders)
            return icu($localize`{folders, plural, one {# folder selected} other {# folders selected}}`, { folders });
        if (files) return icu($localize`{files, plural, one {# file selected} other {# files selected}}`, { files });
        return icu($localize`{count, plural, one {# item selected} other {# items selected}}`, { count: this.count() });
    });
    protected readonly allTitle = computed(() =>
        this.store.hasMore()
            ? icu(
                  $localize`{count, plural, \
one {Select the # item loaded so far (Ctrl+A). Scroll down to load more.} \
other {Select the # items loaded so far (Ctrl+A). Scroll down to load more.}}`,
                  { count: this.loaded() },
              )
            : $localize`Select every file and folder listed (Ctrl+A).`,
    );
    protected readonly tagsTitle = computed(() =>
        this.folders()
            ? $localize`Add or remove tags on the selected files (the selected folders are left out).`
            : $localize`Add or remove tags on the selected files.`,
    );
    /** The hints of the buttons, the same in the "⋯" menu. */
    protected readonly hints = {
        quickLinks: $localize`Add the selected items to your quick links (or drag them onto the sidebar).`,
        renameTogether: $localize`Rename the selected items together: find and replace, or numbered names (F2).`,
        move: $localize`Move the selected items to another folder (Ctrl+X, then Ctrl+V there, \
or drag them onto a folder).`,
        copy: $localize`Copy the selected items into another folder (Ctrl+C, then Ctrl+V there, \
or drag them onto a folder with Ctrl).`,
        compress: $localize`Put the selected items in a zip file in this folder.`,
    };
    protected readonly renameTitle = computed(
        () => $localize`Give “${this.renamable()?.name ?? ''}:name:” another name (F2).`,
    );
    /** The only item selected, when it may be renamed. */
    protected readonly renamable = computed(() => {
        const selected = this.selected();
        return selected.length === 1 && this.menu.canRename(selected[0]) ? selected[0] : null;
    });

    /** Several selected that may be renamed (in a folder, not a search: names are compared with the folder's). */
    protected readonly renamableTogether = computed(
        () => !this.store.searching() && this.selected().filter((item) => this.menu.canRename(item)).length > 1,
    );

    /** Move to…, Copy to…: the folder picker lists the library and the connections. */
    protected readonly movable = computed(() => this.selected().some((item) => this.transfers.canMove(item)));
    protected readonly copyable = computed(() => this.selected().some((item) => this.transfers.canCopy(item)));
    /** Some of the selection may go in a zip (only those do), and the dialog is there. */
    protected readonly compressible = computed(
        () => Boolean(this.dialogs.compress()) && this.selected().some((item) => this.transfers.canCompress(item)),
    );

    /** Extract: one archive selected, its dialog; several (all archives), each beside it; null otherwise. */
    protected readonly extractable = computed(() => {
        const selected = this.selected();
        if (selected.length === 1 && this.transfers.canExtract(selected[0]) && this.dialogs.extract())
            return {
                label: $localize`Extract…`,
                title: $localize`Extract “${selected[0].name}:name:” into a folder picked next, \
with its password if it has one.`,
            };
        if (this.transfers.canExtractEach(selected))
            return { label: $localize`Extract`, title: extractEachTitle(selected.length) };
        return null;
    });

    /** Some of the selection may be deleted (only those are, the confirmation names them). */
    protected readonly deletable = computed(() => this.selected().some((item) => this.fileActions.canDelete(item)));
    protected readonly deleteTitle = computed(() =>
        this.fileActions.alwaysPermanent
            ? $localize`Delete the selected items for good (Delete).`
            : $localize`Move the selected items to the trash (Delete; Shift+Delete deletes them for good).`,
    );

    /** Some of the selection may be downloaded (only those are). */
    protected readonly downloadable = computed(() => this.selected().some((item) => this.downloads.canDownload(item)));
    protected readonly downloadTitle = computed(() =>
        this.downloads.isZip(this.selected())
            ? $localize`Save the selected items to this device, in one zip file (folders with everything in them).`
            : $localize`Save the selected file to this device.`,
    );

    /** What the buttons that may leave the bar are in its "⋯" menu: the same actions, titles and keys. */
    protected readonly more = {
        quickLinks: (): MenuEntry => ({
            id: 'quickLinks',
            label: $localize`Add to quick links`,
            icon: 'pin',
            title: this.hints.quickLinks,
            run: () => this.pinSelection(),
        }),
        rename: (): MenuEntry => ({
            id: 'rename',
            label: $localize`Rename…`,
            icon: 'edit',
            shortcut: 'F2',
            title: this.renameTitle(),
            run: () => this.renameOne(),
        }),
        renameTogether: (): MenuEntry => ({
            id: 'rename',
            label: $localize`Rename…`,
            icon: 'edit',
            shortcut: 'F2',
            title: this.hints.renameTogether,
            run: () => this.menu.renameSelection(),
        }),
        delete: (): MenuEntry => ({
            id: 'delete',
            label: this.fileActions.alwaysPermanent ? $localize`Delete permanently…` : $localize`Delete…`,
            icon: 'trash',
            shortcut: 'Del',
            title: this.deleteTitle(),
            run: () => this.deleteSelection(),
        }),
        move: (): MenuEntry => ({
            id: 'move',
            label: $localize`Move to…`,
            icon: 'move',
            title: this.hints.move,
            run: () => this.transfers.pick('move', this.store.selectedItems()),
        }),
        copy: (): MenuEntry => ({
            id: 'copy',
            label: $localize`Copy to…`,
            icon: 'copy',
            title: this.hints.copy,
            run: () => this.transfers.pick('copy', this.store.selectedItems()),
        }),
        download: (): MenuEntry => ({
            id: 'download',
            label: $localize`Download`,
            icon: 'download',
            title: this.downloadTitle(),
            run: () => this.downloadSelection(),
        }),
        extract: (): MenuEntry => ({
            id: 'extract',
            label: this.extractable()?.label ?? $localize`Extract`,
            icon: 'extract',
            title: this.extractable()?.title ?? '',
            run: () => this.extractSelection(),
        }),
        compress: (): MenuEntry => ({
            id: 'compress',
            label: $localize`Compress…`,
            icon: 'compress',
            title: this.hints.compress,
            run: () => this.compressSelection(),
        }),
    };

    protected extractSelection(): void {
        const selected = this.selected();
        if (selected.length === 1) this.dialogs.extract()?.open({ path: selected[0].path, name: selected[0].name });
        else if (this.transfers.canExtractEach(selected)) {
            void this.transfers.extractEach(selected);
            this.store.clearSelection();
        }
    }

    protected compressSelection(): void {
        this.dialogs.compress()?.open(this.store.selectedItems().filter((item) => this.transfers.canCompress(item)));
    }

    private renameOne(): void {
        const item = this.renamable();
        if (item) this.rename(item);
    }

    protected downloadSelection(): void {
        void this.downloads.download(this.store.selectedItems());
    }

    protected deleteSelection(): void {
        this.fileActions.delete(this.store.selectedItems());
    }

    protected pinSelection(): void {
        void this.quickLinks.add(this.store.selectedItems().map((item) => item.path));
    }

    protected rename(item: LibraryItem): void {
        this.dialogs.name()?.rename(item);
    }

    protected tagSelection(): void {
        const items = this.store.selectedFiles();
        if (items.length === 1) this.dialogs.fileTags()?.open(items[0]);
        else if (items.length) this.dialogs.filesTags()?.open(items);
    }
}
