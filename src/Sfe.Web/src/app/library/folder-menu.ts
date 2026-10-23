import { computed, inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { copyText, CopyPathService } from '../core/copy-path.service';
import { LibraryItem } from '../core/models';
import { baseName } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { FileTransfers } from '../files/file-transfers';
import { pickedFiles, UploadQueue } from '../files/uploads';
import { MenuEntry, MenuTarget } from '../menus/context-menu';
import { Dialogs } from './dialogs.service';
import { routeParams } from './library-route';
import { ListingStore } from './listing.store';
import { QuickLinksService } from './quick-links/quick-links.service';

/**
 * What can be done to the folder listed, or the search: the buttons of the location header and the menu of the
 * listing's background (a right click on nothing that has a menu of its own: the page's menu).
 */
@Injectable({ providedIn: 'root' })
export class FolderMenu {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly config = inject(ConfigService);
    private readonly uploads = inject(UploadQueue);
    private readonly transfers = inject(FileTransfers);
    private readonly quickLinks = inject(QuickLinksService);
    private readonly copyPaths = inject(CopyPathService);
    private readonly toasts = inject(ToastService);
    private readonly router = inject(Router);

    /** Folders and files are made in the folder browsed (not in a search, nor in a folder shown only on the way to another). */
    readonly canCreateFolder = computed(() =>
        !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFolders));
    readonly canCreateFile = computed(() =>
        !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFiles));
    /** Tagging the files, or (browsing) setting the folder's default tags: both in the same dialog (here, with folder rules). */
    readonly canTag = computed(() => this.store.can(Rights.assignTags) || !this.store.searching() && this.store.can(Rights.folderDefaultTags));
    readonly canExclude = computed(() => this.store.can(Rights.excludeFolders) && Boolean(this.dialogs.folderExclusions()));

    newFolder(): void {
        this.dialogs.name()?.newFolder(this.store.route().path);
    }

    newFile(): void {
        this.dialogs.name()?.newFile(this.store.route().path);
    }

    /** The tags of every file of the folder browsed, or of every match of the search. */
    openScopeTags(): void {
        const { path, q } = this.store.route();
        this.dialogs.scopeTags()?.open({ path, recursive: false, query: q || null });
    }

    /** What the folder browsed is kept out of (scans, trickplay, thumbnails); null when nothing (or searching). */
    exclusions() {
        const page = this.store.listing();
        return page && !this.store.searching() && 'exclusions' in page ? page.exclusions ?? null : null;
    }

    openExclusions(): void {
        const page = this.store.listing();
        if (page)
            this.dialogs.folderExclusions()?.open(page.path, this.exclusions());
    }

    /** Uploads files picked, or a folder (`folder`), into the folder listed. */
    pickUploads(folder: boolean): void {
        const input = document.createElement('input');
        input.type = 'file';
        input.multiple = true;
        input.webkitdirectory = folder;
        const target = this.store.route().path;
        input.addEventListener('change', () => void this.uploads.add(pickedFiles(input), target), { once: true });
        input.click();
    }

    /** The menu of the listing's background: the folder listed, or the search. */
    target(): MenuTarget {
        const menu = this;
        return {
            kind: 'background',
            key: 'background',
            get name() { return menu.name(); },
            entries: () => this.entries(),
        };
    }

    /** The menu's title (phones): the folder, or the search. */
    private name(): string {
        const { path, q } = this.store.route();
        return q ? `Search “${q}”` : this.folderItem().name;
    }

    /** A folder, as an item (Properties): the folder listed by default. */
    private folderItem(path = this.store.route().path): LibraryItem {
        return { name: baseName(path) || this.config.config()?.rootName || '', path, kind: 'directory', extension: null, size: null, modified: null };
    }

    /**
     * The menu of a link to a folder that isn't listed (breadcrumbs, nearby folders, a search result's folder): only
     * its path is known, so no entry that needs the folder's own rules (the server still checks a paste).
     */
    linkTarget(path: string): MenuTarget {
        return { kind: 'folderLink', key: `folderLink:${path}`, name: this.folderItem(path).name, entries: () => this.linkEntries(path) };
    }

    linkEntries(path: string): MenuEntry[] {
        const name = this.folderItem(path).name;
        const params = this.store.browseParams(path);
        const entries: MenuEntry[] = [
            { id: 'open', label: 'Open', icon: 'folder', title: `List ${name}`, run: () => void this.router.navigate(['/'], { queryParams: params }) },
            { id: 'newTab', label: 'Open in new tab', icon: 'external', title: `List ${name} in a tab of its own`, href: this.store.pageUrl(params), newTab: true },
        ];
        const clipboard = this.transfers.clipboard();
        if (clipboard && this.transfers.allows(clipboard.kind))
            entries.push({
                id: 'paste', label: `Paste into ${name}`, icon: 'paste',
                title: `${clipboard.kind === 'move' ? 'Move' : 'Copy'} ${clipboard.items.length === 1 ? clipboard.items[0].name : `the ${clipboard.items.length} items kept`} into this folder`,
                run: () => this.transfers.paste(path),
            });
        if (this.quickLinks.enabled()) {
            const pinned = this.quickLinks.paths().has(path);
            entries.push({
                id: 'quickLink', label: pinned ? 'Remove from quick links' : 'Add to quick links', icon: 'pin',
                title: pinned ? `Take ${name} out of your quick links` : `Show ${name} in your quick links, in the sidebar`,
                run: () => void this.quickLinks.toggle(path),
            });
        }
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where this folder is on the server', run: () => void this.copyPaths.copy(path) });
        if (this.config.can(Rights.browse) && this.dialogs.properties())
            entries.push({ id: 'properties', label: 'Properties', icon: 'info', title: 'Its dates, and what it holds', run: () => void this.dialogs.properties()?.open(this.folderItem(path)) });
        return entries;
    }

    entries(): MenuEntry[] {
        if (this.store.status() !== 'ready')
            return [];
        const { path, q } = this.store.route();
        const href = this.store.pageUrl(routeParams(this.store.route()));
        const entries: MenuEntry[] = [];
        if (q) {
            if (this.store.items().length)
                entries.push({ id: 'selectAll', label: 'Select all', icon: 'check', shortcut: 'Ctrl+A', title: 'Select every result loaded (Ctrl+A)', run: () => this.store.selectAll() });
            if (this.canTag())
                entries.push({ id: 'tags', label: 'Tag the results…', icon: 'tag', title: 'Add or remove tags on all the results of this search (asks first)', run: () => this.openScopeTags() });
            entries.push(
                { id: 'refresh', label: 'Search again', icon: 'refresh', title: 'Run this search again', run: () => void this.store.load() },
                { id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'This search in a tab of its own', href, newTab: true },
                { id: 'copyLink', label: 'Copy link', icon: 'copy', title: 'Copy the address of this search', run: () => void this.copyLink(href) },
            );
            return entries;
        }
        if (this.canCreateFolder())
            entries.push({ id: 'newFolder', label: 'New folder…', icon: 'folder-plus', shortcut: 'Shift+N', title: 'New folder in this folder (Shift+N)', run: () => this.newFolder() });
        if (this.canCreateFile())
            entries.push({ id: 'newFile', label: 'New file…', icon: 'file-plus', title: 'New empty file in this folder', run: () => this.newFile() });
        if (this.uploads.canUploadInto())
            entries.push(
                { id: 'upload', label: 'Upload files…', icon: 'upload', title: 'Upload files into this folder (or drop them on the page)', run: () => this.pickUploads(false) },
                { id: 'uploadFolder', label: 'Upload a folder…', icon: 'folder-up', title: 'Upload a folder, with everything in it, into this folder', run: () => this.pickUploads(true) },
            );
        const clipboard = this.transfers.clipboard();
        if (clipboard && this.transfers.canInto(clipboard.kind))
            entries.push({
                id: 'paste', label: 'Paste', icon: 'paste', shortcut: 'Ctrl+V',
                title: `${clipboard.kind === 'move' ? 'Move' : 'Copy'} ${clipboard.items.length === 1 ? clipboard.items[0].name : `the ${clipboard.items.length} items kept`} into this folder (Ctrl+V)`,
                run: () => this.transfers.paste(path),
            });
        if (this.store.items().length)
            entries.push({ id: 'selectAll', label: 'Select all', icon: 'check', shortcut: 'Ctrl+A', title: 'Select every item loaded (Ctrl+A)', run: () => this.store.selectAll() });
        entries.push({ id: 'refresh', label: 'Refresh', icon: 'refresh', title: 'List this folder again, as it is now', run: () => void this.store.refresh() });
        if (this.canTag())
            entries.push({ id: 'tags', label: 'Tags of this folder…', icon: 'tag', title: 'Add or remove tags on all the files of this folder, or set the tags new files get (asks first)', run: () => this.openScopeTags() });
        if (this.canExclude())
            entries.push({ id: 'exclusions', label: 'Scans and previews…', icon: 'scan', title: 'Keep this folder out of scans, trickplay or thumbnails, for the whole space', run: () => this.openExclusions() });
        if (this.quickLinks.enabled()) {
            const pinned = this.quickLinks.paths().has(path);
            entries.push({
                id: 'quickLink', label: pinned ? 'Remove from quick links' : 'Add to quick links', icon: 'pin',
                title: pinned ? 'Take this folder out of your quick links' : 'Show this folder in your quick links, in the sidebar',
                run: () => void this.quickLinks.toggle(path),
            });
        }
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where this folder is on the server', run: () => void this.copyPaths.copy(path) });
        entries.push({ id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'This folder in a tab of its own', href, newTab: true });
        if (this.config.can(Rights.browse) && this.dialogs.properties())
            entries.push({ id: 'properties', label: 'Properties', icon: 'info', title: 'Its dates, and what it holds', run: () => void this.dialogs.properties()?.open(this.folderItem()) });
        return entries;
    }

    private async copyLink(href: string): Promise<void> {
        try {
            await copyText(new URL(href, document.baseURI).href);
            this.toasts.show('Link copied.');
        } catch {
            this.toasts.show('The link could not be copied.', { error: true });
        }
    }
}
