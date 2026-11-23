import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { formatOf, isContainer, isViewable } from '../formats/formats';
import { Dialogs } from '../library/dialogs.service';
import { ItemActions } from '../library/items/item-actions.service';
import { ListingStore } from '../library/listing.store';
import { ItemCommand, ItemCommandId, ItemMenuEntry, ItemScope, refused } from './item-scope';

/**
 * The commands opening one listed item (Open, Open in new tab, Open containing folder, Open as…) or editing it (Edit,
 * Crop…), for `ItemCommands`. None for a selection of several: one tab each would be stopped as popups.
 */
@Injectable({ providedIn: 'root' })
export class OpeningCommands {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly actions = inject(ItemActions);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);

    readonly commands: Partial<Record<ItemCommandId, ItemCommand>> = {
        open: (scope) => this.single(scope) && this.open(scope),
        newTab: (scope) => this.single(scope) && this.newTab(scope),
        openFolder: (scope) => this.single(scope) && this.openFolder(scope),
        openAsText: (scope) => this.single(scope) && this.openAsText(scope),
        openAsArchive: (scope) => this.single(scope) && this.openAsArchive(scope),
        editText: (scope) => this.single(scope) && this.editText(scope),
        editPicture: (scope) => this.single(scope) && this.editPicture(scope),
    };

    /** Acts on one item only. */
    private single(scope: ItemScope): null | true {
        return scope.targets.length === 1 || null;
    }

    private open({ item }: ItemScope): ItemMenuEntry | null {
        if (item.kind === 'directory') {
            const params = this.store.browseParams(item.path);
            return {
                id: 'open',
                label: $localize`Open`,
                icon: 'folder',
                title: $localize`Open the folder “${item.name}:name:”.`,
                run: () => void this.router.navigate(['/'], { queryParams: params }),
            };
        }
        if (!isViewable(item) || !this.dialogs.viewer()) return null;
        return {
            id: 'open',
            label: $localize`Open`,
            icon: 'file',
            title: $localize`Show “${item.name}:name:” in the viewer.`,
            run: () => this.actions.view(item),
        };
    }

    private newTab({ item }: ItemScope): ItemMenuEntry {
        const folder = item.kind === 'directory';
        return {
            id: 'newTab',
            label: $localize`Open in new tab`,
            icon: 'external',
            title: folder
                ? $localize`Open the folder “${item.name}:name:” in a tab of its own.`
                : $localize`Open “${item.name}:name:” in a tab of its own.`,
            href: folder ? this.store.pageUrl(this.store.browseParams(item.path)) : this.actions.fileUrl(item),
            newTab: true,
            run: () => (folder ? undefined : this.store.reportView(item)),
        };
    }

    /** In search results: the folder the item is in. */
    private openFolder({ item }: ItemScope): ItemMenuEntry | null {
        if (!this.store.searching() || item.directory === undefined || item.directory === null) return null;
        return {
            id: 'openFolder',
            label: $localize`Open containing folder`,
            icon: 'folder-up',
            title: $localize`Open the folder “${item.name}:name:” is in, and show it there.`,
            run: () => this.store.reveal(item.path),
        };
    }

    /** A file without a viewer of its own may be opened as text (the server refuses what isn't). */
    private openAsText({ item }: ItemScope): ItemMenuEntry | null {
        if (item.kind !== 'file' || formatOf(item) || !this.dialogs.viewer()) return null;
        const entry: ItemMenuEntry = {
            id: 'openAsText',
            label: $localize`Open as text`,
            icon: 'file',
            title: $localize`Show its content as text, in the viewer.`,
            run: () => this.dialogs.viewer()?.open(item, { asText: true }),
        };
        return refused(entry, this.store.can(Rights.viewFiles, item), 'view', [item.name]);
    }

    /** A comic book, an EPUB, an office document…: a zip underneath, its files explored in the viewer. */
    private openAsArchive({ item }: ItemScope): ItemMenuEntry | null {
        if (!isContainer(item) || !this.dialogs.viewer()) return null;
        const entry: ItemMenuEntry = {
            id: 'openAsArchive',
            label: $localize`Open as archive`,
            icon: 'extract',
            title: $localize`Show the files and folders inside it, in the viewer.`,
            run: () => this.dialogs.viewer()?.open(item, { asArchive: true }),
        };
        return refused(entry, this.store.can(Rights.viewFiles, item), 'view', [item.name]);
    }

    /**
     * A text file that something could be done with once changed (save over it, a copy, a download): the server tells
     * exactly when it is opened (`access`), the viewer then offers what applies.
     */
    private editText({ item }: ItemScope): ItemMenuEntry | null {
        if (!formatOf(item)?.edits || !this.dialogs.viewer()) return null;
        const allowed =
            this.store.can(Rights.viewFiles, item) &&
            (this.store.can(Rights.createFiles, item) || this.store.can(Rights.download, item));
        const entry: ItemMenuEntry = {
            id: 'editText',
            label: $localize`Edit`,
            icon: 'edit',
            title: $localize`Open it in the viewer, ready to be changed.`,
            run: () => this.dialogs.viewer()?.open(item, { edit: true }),
        };
        return refused(entry, allowed, 'edit', [item.name]);
    }

    /** A picture the editor can crop (a server with the editor). */
    private editPicture({ item }: ItemScope): ItemMenuEntry | null {
        if (item.kind !== 'image' || !this.config.config()?.editor || !this.dialogs.imageEditor()) return null;
        const entry: ItemMenuEntry = {
            id: 'editPicture',
            label: $localize`Crop…`,
            icon: 'crop',
            title: $localize`Crop the picture, then save it over the original or as a new file.`,
            run: () => void this.dialogs.imageEditor()?.open(item),
        };
        return refused(entry, this.store.can(Rights.editImages, item), 'edit', [item.name]);
    }
}
