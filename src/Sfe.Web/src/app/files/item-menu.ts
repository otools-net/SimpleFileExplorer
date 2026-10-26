import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, Injectable, input, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { CopyPathService } from '../core/copy-path.service';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { formatOf, isViewable } from '../formats/formats';
import { ContextMenu, MenuEntry, MenuTarget } from '../menus/context-menu';
import { IconComponent } from '../shared/icon.component';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ItemActions } from '../library/items/item-actions.service';
import { QuickLinksService } from '../library/quick-links/quick-links.service';
import { FileDownloads } from './downloads';
import { FileActions } from './file-actions';
import { stemOf } from './extract.dialog';
import { FileTransfers } from './file-transfers';

/** One entry of an item's menu: always an action. */
export interface ItemMenuEntry extends MenuEntry {
    id: 'open' | 'newTab' | 'openFolder' | 'openAsText' | 'editText' | 'editPicture' | 'rename' | 'delete' | 'cut' | 'copy' | 'paste' | 'moveTo' | 'copyTo' | 'extractBeside' | 'extractEach' | 'extract' | 'compress' | 'download' | 'tags' | 'quickLink' | 'copyPath' | 'exclusions' | 'properties';
    run: () => void;
}

/**
 * The menu of a listed file or folder, as in an OS file browser (shown by `ContextMenu`): opened by a right click
 * (the browser's own menu with Shift) or the item's ⋮ button; a long press on phones selects the item instead. When
 * the item is part of the selection, its entries act on all of it.
 */
@Injectable({ providedIn: 'root' })
export class ItemMenu {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly actions = inject(ItemActions);
    private readonly copyPaths = inject(CopyPathService);
    private readonly files = inject(FileActions);
    private readonly transfers = inject(FileTransfers);
    private readonly downloads = inject(FileDownloads);
    private readonly config = inject(ConfigService);
    private readonly quickLinks = inject(QuickLinksService);

    private readonly menu = inject(ContextMenu);
    private readonly router = inject(Router);

    constructor() {
        // Another listing: the item whose menu is open may not be there any more.
        let generation = untracked(() => this.store.generation());
        effect(() => {
            const now = this.store.generation();
            if (now === generation)
                return;
            generation = now;
            untracked(() => {
                if (this.menu.open()?.target.item)
                    this.menu.close();
            });
        });
    }

    /** The menu of `item`, for `ContextMenu`. */
    target(item: LibraryItem): MenuTarget {
        return {
            kind: item.kind === 'directory' ? 'folder' : 'file',
            key: `item:${item.path}`,
            name: item.name,
            item,
            entries: () => this.hasActions(item) ? this.entries(item) : [],
            touch: () => this.store.select(item, !this.store.isSelected(item.path)),
        };
    }

    /**
     * Everyone who browses has the menu, for Properties at least (the browser's own menu with Shift). The item's card
     * only has its tags and ⋮ buttons: quick links and copying the path are here too.
     */
    hasActions(item: LibraryItem): boolean {
        return this.canSeeProperties() || this.transfers.canExtract(item) || this.transfers.canCompress(item) || this.canOpenAsText(item) || this.canEditText(item) || this.canEditPicture(item) || this.quickLinks.enabled() || this.copyPaths.enabled() || this.canRename(item) || this.files.canDelete(item) || this.transfers.canMove(item) || this.transfers.canCopy(item) || this.canExclude(item);
    }

    /** Properties of what is listed: whoever browses. */
    canSeeProperties(): boolean {
        return this.config.can(Rights.browse);
    }

    /** Several items selected that may be renamed: the batch rename opens for them. False when fewer than two. */
    renameSelection(): boolean {
        const items = this.store.selectedItems().filter(item => this.canRename(item));
        if (items.length < 2 || this.store.searching())
            return false;
        this.dialogs.batchRename()?.open(items);
        return true;
    }

    /** May rename `item`: in the folder listed (its list of names changes) and on the item itself. */
    canRename(item: LibraryItem): boolean {
        return this.files.canRename(item);
    }

    /** A file without a viewer of its own may be opened as text (the server refuses what isn't). */
    canOpenAsText(item: LibraryItem): boolean {
        return item.kind === 'file' && !formatOf(item) && this.store.can(Rights.viewFiles, item) && Boolean(this.dialogs.viewer());
    }

    /**
     * A text file that something could be done with once changed (save over it, a copy, a download): the server tells
     * exactly when it is opened (`access`), the viewer then offers what applies.
     */
    canEditText(item: LibraryItem): boolean {
        const format = formatOf(item);
        return Boolean(format?.edits && this.dialogs.viewer() && this.store.can(Rights.viewFiles, item)
            && (this.store.can(Rights.createFiles, item) || this.store.can(Rights.download, item)));
    }

    /** A picture the editor can crop (the right, and a server with the editor). */
    canEditPicture(item: LibraryItem): boolean {
        return item.kind === 'image' && this.store.can(Rights.editImages, item) && Boolean(this.config.config()?.editor) && Boolean(this.dialogs.imageEditor());
    }

    /**
     * May keep a listed folder out of scans, trickplay or thumbnails (for the whole space): while browsing, where the
     * listing tells what it is kept out of now.
     */
    canExclude(item: LibraryItem): boolean {
        return item.kind === 'directory' && !this.store.searching() && this.store.can(Rights.excludeFolders, item) && Boolean(this.dialogs.folderExclusions());
    }

    /** The entries of `item`'s menu; on the selection when `item` is part of it, unless `single` (the viewer). */
    entries(item: LibraryItem, single = false): ItemMenuEntry[] {
        const targets = () => single ? [item] : this.targets(item);
        const entries: ItemMenuEntry[] = this.openings(item, targets());
        if (this.canOpenAsText(item))
            entries.push({ id: 'openAsText', label: 'Open as text', icon: 'file', title: 'Show its content as text, in the viewer', run: () => this.dialogs.viewer()?.open(item, { asText: true }) });
        if (this.canEditText(item))
            entries.push({ id: 'editText', label: 'Edit', icon: 'edit', title: 'Open it in the viewer, ready to be changed', run: () => this.dialogs.viewer()?.open(item, { edit: true }) });
        if (this.canEditPicture(item))
            entries.push({ id: 'editPicture', label: 'Crop…', icon: 'crop', title: 'Crop the picture, then save it over the original or as a new file', run: () => void this.dialogs.imageEditor()?.open(item) });
        if (this.canRename(item)) {
            const several = targets().filter(target => this.canRename(target)).length > 1 && !this.store.searching();
            entries.push({
                id: 'rename', label: 'Rename…', icon: 'edit', shortcut: 'F2',
                title: several ? 'Rename the selected items together: find and replace, or numbered names (F2)' : `Give ${item.name} another name (F2)`,
                run: () => several ? this.renameSelection() : this.dialogs.name()?.rename(item),
            });
        }
        if (this.files.canDelete(item))
            entries.push({
                id: 'delete',
                label: this.files.alwaysPermanent ? 'Delete permanently…' : 'Delete…',
                icon: 'trash',
                shortcut: 'Del',
                title: this.files.alwaysPermanent ? `Delete ${item.name} for good (Delete)` : `Move ${item.name} to the trash (Delete; Shift+Delete: for good)`,
                run: () => this.files.delete(targets()),
            });
        const canMove = this.transfers.canMove(item);
        const canCopy = this.transfers.canCopy(item);
        if (canMove)
            entries.push({ id: 'cut', label: 'Cut', icon: 'cut', shortcut: 'Ctrl+X', title: 'Keep it to move into a folder with Paste (Ctrl+X)', run: () => this.transfers.keep('move', targets()) });
        if (canCopy)
            entries.push({ id: 'copy', label: 'Copy', icon: 'copy', shortcut: 'Ctrl+C', title: 'Keep it to copy into a folder with Paste (Ctrl+C)', run: () => this.transfers.keep('copy', targets()) });
        const clipboard = this.transfers.clipboard();
        if (clipboard && item.kind === 'directory' && this.transfers.canInto(clipboard.kind, item))
            entries.push({
                id: 'paste',
                label: `Paste into ${item.name}`,
                icon: 'paste',
                title: `${clipboard.kind === 'move' ? 'Move' : 'Copy'} ${clipboard.items.length === 1 ? clipboard.items[0].name : `the ${clipboard.items.length} items kept`} into this folder`,
                run: () => this.transfers.paste(item.path),
            });
        if (canMove)
            entries.push({ id: 'moveTo', label: 'Move to…', icon: 'move', title: 'Move it to another folder, picked next', run: () => this.transfers.pick('move', targets()) });
        if (canCopy)
            entries.push({ id: 'copyTo', label: 'Copy to…', icon: 'copy', title: 'Copy it into another folder, picked next', run: () => this.transfers.pick('copy', targets()) });
        if (targets().length === 1 && this.transfers.canExtractBeside(item))
            entries.push({
                id: 'extractBeside', label: `Extract to “${stemOf(item.name)}”`, icon: 'extract',
                title: `Extract it here, into a new folder named after it (or after its one folder); in the background`,
                run: () => void this.transfers.extract({ archive: item.path, target: this.store.route().path, newFolder: true }),
            });
        if (targets().length > 1 && this.transfers.canExtractEach(targets()))
            entries.push({
                id: 'extractEach', label: `Extract each here`, icon: 'extract',
                title: `Extract the ${targets().length} archives here, each into a new folder named after it; in the background`,
                run: () => void this.transfers.extractEach(targets()),
            });
        if (targets().length === 1 && this.transfers.canExtract(item) && this.dialogs.extract())
            entries.push({
                id: 'extract', label: 'Extract…', icon: 'extract',
                title: 'Extract it into a folder picked next, with its password if it has one',
                run: () => this.dialogs.extract()?.open({ path: item.path, name: item.name }),
            });
        if (this.transfers.canCompress(item) && this.dialogs.compress())
            entries.push({
                id: 'compress', label: 'Compress…', icon: 'compress',
                title: 'Put it in a zip in this folder: how hard to compress, a password if wanted',
                run: () => this.dialogs.compress()?.open(targets().filter(target => this.transfers.canCompress(target))),
            });
        if (this.downloads.canDownload(item)) {
            const selected = targets();
            const zip = this.downloads.isZip(selected);
            entries.push({
                id: 'download',
                label: zip ? 'Download as zip' : 'Download',
                icon: 'download',
                title: zip ? (selected.length > 1 ? 'Save the selected items to this device, in one zip file' : 'Save this folder, with everything in it, to this device as a zip file') : 'Save this file to this device',
                run: () => void this.downloads.download(selected),
            });
        }
        if (item.kind === 'directory' ? this.store.can(Rights.assignTags, item) : this.store.can(Rights.viewTags, item))
            entries.push({
                id: 'tags',
                label: item.kind === 'directory' ? 'Tag its files…' : 'Tags…',
                icon: 'tag',
                title: item.kind === 'directory' ? 'Add or remove tags on all the files of this folder (asks first)' : 'The tags of this file',
                run: () => item.kind === 'directory'
                    ? this.dialogs.scopeTags()?.open({ path: item.path, recursive: false, query: null })
                    : this.actions.openTags(item),
            });
        if (this.quickLinks.enabled()) {
            const pinned = this.quickLinks.paths().has(item.path);
            entries.push({
                id: 'quickLink',
                label: pinned ? 'Remove from quick links' : 'Add to quick links',
                icon: 'pin',
                title: pinned ? `Take ${item.name} out of your quick links` : `Show ${item.name} in your quick links, in the sidebar`,
                run: () => void this.quickLinks.toggle(item.path),
            });
        }
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => void this.copyPaths.copy(item.path) });
        if (this.canExclude(item))
            entries.push({
                id: 'exclusions', label: 'Scans and previews…', icon: 'scan',
                title: 'Keep this folder out of scans, trickplay or thumbnails, for the whole space',
                run: () => this.dialogs.folderExclusions()?.open(item.path, item.excluded),
            });
        if (this.canSeeProperties())
            entries.push({
                id: 'properties', label: 'Properties', icon: 'info', shortcut: 'Alt+Enter',
                title: item.kind === 'directory' ? 'Its dates, and what it holds (Alt+Enter)' : 'Its size, dates and what the index knows of it; its checksum (Alt+Enter)',
                run: () => void this.dialogs.properties()?.open(item),
            });
        return entries;
    }

    /**
     * Open, Open in new tab (a real link) and, in search results, Open containing folder: not for a selection of
     * several items (one tab each would be stopped as popups).
     */
    private openings(item: LibraryItem, targets: LibraryItem[]): ItemMenuEntry[] {
        if (targets.length > 1)
            return [];
        const entries: ItemMenuEntry[] = [];
        if (item.kind === 'directory') {
            const params = this.store.browseParams(item.path);
            entries.push(
                { id: 'open', label: 'Open', icon: 'folder', title: `List ${item.name}`, run: () => void this.router.navigate(['/'], { queryParams: params }) },
                { id: 'newTab', label: 'Open in new tab', icon: 'external', title: `List ${item.name} in a tab of its own`, href: this.store.pageUrl(params), newTab: true, run: () => undefined },
            );
        } else {
            if (isViewable(item) && this.dialogs.viewer())
                entries.push({ id: 'open', label: 'Open', icon: 'file', title: `Show ${item.name} in the viewer`, run: () => this.actions.view(item) });
            entries.push({ id: 'newTab', label: 'Open in new tab', icon: 'external', title: `Open ${item.name} in a tab of its own`, href: this.actions.fileUrl(item), newTab: true, run: () => this.store.reportView(item) });
        }
        if (this.store.searching() && item.directory !== undefined && item.directory !== null)
            entries.push({
                id: 'openFolder', label: 'Open containing folder', icon: 'folder-up',
                title: `List the folder ${item.name} is in, and show it there`,
                run: () => this.store.reveal(item.path),
            });
        return entries;
    }

    /** The selection when `item` is part of it (the menu acts on all of it, as in a file browser), else `item`. */
    private targets(item: LibraryItem): LibraryItem[] {
        return this.store.isSelected(item.path) ? this.store.selectedItems() : [item];
    }
}

/** The ⋮ button of a listed item opening its menu; only on items with one (see `ItemMenu.hasActions`). */
@Component({
    selector: 'button[sfeItemMenu]',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        type: 'button',
        class: 'icon-button small item-menu-button',
        title: 'More actions (right click)',
        'aria-haspopup': 'menu',
        '[attr.aria-label]': '"More actions for " + sfeItemMenu().name',
        '[hidden]': '!menu.hasActions(sfeItemMenu())',
        '(click)': 'toggle($event)',
    },
    template: `<svg sfeIcon="more"></svg>`,
})
export class ItemMenuButtonComponent {
    readonly sfeItemMenu = input.required<LibraryItem>();
    protected readonly menu = inject(ItemMenu);
    private readonly contextMenu = inject(ContextMenu);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    protected toggle(event: MouseEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.contextMenu.toggle(this.menu.target(this.sfeItemMenu()), this.host);
    }
}
