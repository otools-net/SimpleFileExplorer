import { computed, inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { copyText } from '../core/copy-path.service';
import { LibraryItem } from '../core/models';
import { baseName } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { FileTransfers, pasteTitle } from '../files/file-transfers';
import { ItemCommands } from '../files/item-commands';
import { pickedFiles, UploadQueue } from '../files/uploads';
import { uploadConnection } from '../files/upload-places';
import { MenuEntry, MenuTarget } from '../menus/context-menu';
import { Dialogs } from './dialogs.service';
import { routeParams } from './library-route';
import { ListingStore } from './listing.store';
import { SearchScopeMenu } from './topbar/search-scope-menu';

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
    private readonly commands = inject(ItemCommands);
    private readonly toasts = inject(ToastService);
    private readonly router = inject(Router);
    private readonly scopeMenu = inject(SearchScopeMenu);

    /**
     * Folders and files are made in the folder browsed (not in a search, nor in a folder shown only on the way to
     * another).
     */
    readonly canCreateFolder = computed(
        () => !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFolders),
    );
    readonly canCreateFile = computed(
        () => !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFiles),
    );
    /**
     * Tagging the files, or (browsing) setting the folder's default tags: both in the same dialog (here, with folder
     * rules).
     */
    readonly canTag = computed(
        () =>
            this.store.can(Rights.assignTags) || (!this.store.searching() && this.store.can(Rights.folderDefaultTags)),
    );
    readonly canExclude = computed(
        () => this.store.can(Rights.excludeFolders) && Boolean(this.dialogs.folderExclusions()),
    );

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
        return page && !this.store.searching() && 'exclusions' in page ? (page.exclusions ?? null) : null;
    }

    openExclusions(): void {
        const page = this.store.listing();
        if (page) this.dialogs.folderExclusions()?.open(page.path, this.exclusions());
    }

    /** Uploads files picked, or a folder (`folder`), into the folder listed. */
    pickUploads(folder: boolean): void {
        const input = document.createElement('input');
        input.type = 'file';
        input.multiple = true;
        input.webkitdirectory = folder;
        const [target, connection] = [this.store.route().path, uploadConnection(this.config)];
        input.addEventListener('change', () => void this.uploads.add(pickedFiles(input), target, connection), {
            once: true,
        });
        input.click();
    }

    /** The menu of the listing's background: the folder listed, or the search. */
    target(): MenuTarget {
        const menu = this;
        return {
            kind: 'background',
            key: 'background',
            get name() {
                return menu.name();
            },
            entries: () => this.entries(),
        };
    }

    /** The menu's title (phones): the folder, or the search. */
    private name(): string {
        const { path, q } = this.store.route();
        return q ? $localize`Search “${q}:query:”` : this.folderItem().name;
    }

    /** A folder, as an item (Properties): the folder listed by default. */
    private folderItem(path = this.store.route().path): LibraryItem {
        return {
            name: baseName(path) || this.config.config()?.rootName || '',
            path,
            kind: 'directory',
            extension: null,
            size: null,
            modified: null,
        };
    }

    /**
     * The menu of a link to a folder that isn't listed (breadcrumbs, nearby folders, a search result's folder): only
     * its path is known, so no entry that needs the folder's own rules (the server still checks a paste).
     */
    linkTarget(path: string): MenuTarget {
        return {
            kind: 'folderLink',
            key: `folderLink:${path}`,
            name: this.folderItem(path).name,
            entries: () => this.linkEntries(path),
        };
    }

    /** Opening the folder a job puts things into (an upload, a move or copy): here, or in a new tab. */
    targetEntries(path: string): MenuEntry[] {
        return this.linkEntries(path)
            .filter((entry) => entry.id === 'open' || entry.id === 'newTab')
            .map((entry) => ({
                ...entry,
                id: entry.id === 'open' ? 'openTarget' : 'targetNewTab',
                label: entry.id === 'open' ? $localize`Open the folder` : $localize`Open the folder in new tab`,
            }));
    }

    linkEntries(path: string): MenuEntry[] {
        const name = this.folderItem(path).name;
        const params = this.store.browseParams(path);
        const entries: MenuEntry[] = [
            {
                id: 'open',
                label: $localize`Open`,
                icon: 'folder',
                title: $localize`Open the folder “${name}:name:”.`,
                run: () => void this.router.navigate(['/'], { queryParams: params }),
                group: 'open',
            },
            {
                id: 'newTab',
                label: $localize`Open in new tab`,
                icon: 'external',
                title: $localize`Open the folder “${name}:name:” in a tab of its own.`,
                href: this.store.pageUrl(params),
                newTab: true,
                group: 'open',
            },
        ];
        const clipboard = this.transfers.clipboard();
        if (clipboard && this.transfers.allows(clipboard.kind))
            entries.push({
                id: 'paste',
                label: $localize`Paste into “${name}:name:”`,
                icon: 'paste',
                title: pasteTitle(clipboard, name),
                run: () => this.transfers.paste(path),
                group: 'clipboard',
            });
        entries.push(...this.shared(path), ...this.properties(path));
        return entries;
    }

    entries(): MenuEntry[] {
        if (this.store.status() !== 'ready') return [];
        return this.store.route().q ? this.searchEntries() : this.folderEntries();
    }

    /** Seeing it again, elsewhere: Refresh (or Search again), Open in new tab. */
    private viewEntries(): MenuEntry[] {
        const searching = Boolean(this.store.route().q);
        return [
            {
                id: 'refresh',
                label: searching ? $localize`Search again` : $localize`Refresh`,
                icon: 'refresh',
                title: searching ? $localize`Run this search again.` : $localize`List this folder again, as it is now.`,
                run: () => void (searching ? this.store.load() : this.store.refresh()),
                group: 'view',
            },
            {
                id: 'newTab',
                label: $localize`Open in new tab`,
                icon: 'external',
                title: searching
                    ? $localize`Open this search in a tab of its own.`
                    : $localize`Open this folder in a tab of its own.`,
                href: this.store.pageUrl(routeParams(this.store.route())),
                newTab: true,
                group: 'view',
            },
        ];
    }

    private selectAllEntry(): MenuEntry[] {
        if (!this.store.items().length) return [];
        return [
            {
                id: 'selectAll',
                label: $localize`Select all`,
                icon: 'check',
                shortcut: 'Ctrl+A',
                title: this.store.route().q
                    ? $localize`Select every result loaded so far (Ctrl+A).`
                    : $localize`Select every item loaded so far (Ctrl+A).`,
                run: () => this.store.selectAll(),
                group: 'clipboard',
            },
        ];
    }

    private searchEntries(): MenuEntry[] {
        const entries = [...this.viewEntries(), ...this.selectAllEntry()];
        if (this.canTag())
            entries.push({
                id: 'tags',
                label: $localize`Tag the results…`,
                icon: 'tag',
                title: $localize`Add or remove tags on all the results of this search (asks first).`,
                run: () => this.openScopeTags(),
                group: 'organise',
            });
        const searchIn = this.scopeMenu.entry();
        if (searchIn) entries.push({ ...searchIn, group: 'organise' });
        const href = this.store.pageUrl(routeParams(this.store.route()));
        entries.push({
            id: 'copyLink',
            label: $localize`Copy link`,
            icon: 'copy',
            title: $localize`Copy the address of this search.`,
            run: () => void this.copyLink(href),
            group: 'save',
        });
        return entries;
    }

    private folderEntries(): MenuEntry[] {
        const path = this.store.route().path;
        const entries = [...this.viewEntries(), ...this.newEntries()];
        const clipboard = this.transfers.clipboard();
        if (clipboard && this.transfers.canInto(clipboard.kind))
            entries.push({
                id: 'paste',
                label: $localize`Paste`,
                icon: 'paste',
                shortcut: 'Ctrl+V',
                title: pasteTitle(clipboard),
                run: () => this.transfers.paste(path),
                group: 'clipboard',
            });
        entries.push(...this.selectAllEntry());
        if (this.canTag())
            entries.push({
                id: 'tags',
                label: $localize`Tags of this folder…`,
                icon: 'tag',
                title: $localize`Add or remove tags on all the files of this folder, or set the tags new files get \
(asks first).`,
                run: () => this.openScopeTags(),
                group: 'organise',
            });
        if (this.canExclude())
            entries.push({
                id: 'exclusions',
                label: $localize`Scans and previews…`,
                icon: 'scan',
                title: $localize`Keep this folder out of scans, trickplay or thumbnails, \
for everyone using this space.`,
                run: () => this.openExclusions(),
                group: 'organise',
            });
        entries.push(...this.shared(path), ...this.properties(path));
        return entries;
    }

    /** Making something in the folder listed: New folder…, New file…, uploads. */
    private newEntries(): MenuEntry[] {
        const entries: MenuEntry[] = [];
        if (this.canCreateFolder())
            entries.push({
                id: 'newFolder',
                label: $localize`New folder…`,
                icon: 'folder-plus',
                shortcut: 'Shift+N',
                title: $localize`Make a new folder in this folder (Shift+N).`,
                run: () => this.newFolder(),
                group: 'new',
            });
        if (this.canCreateFile())
            entries.push({
                id: 'newFile',
                label: $localize`New file…`,
                icon: 'file-plus',
                title: $localize`Make a new empty file in this folder.`,
                run: () => this.newFile(),
                group: 'new',
            });
        if (this.uploads.canUploadInto())
            entries.push(
                {
                    id: 'upload',
                    label: $localize`Upload files…`,
                    icon: 'upload',
                    title: $localize`Upload files into this folder (or drop them on the page).`,
                    run: () => this.pickUploads(false),
                    group: 'new',
                },
                {
                    id: 'uploadFolder',
                    label: $localize`Upload a folder…`,
                    icon: 'folder-up',
                    title: $localize`Upload a folder and everything in it into this folder.`,
                    run: () => this.pickUploads(true),
                    group: 'new',
                },
            );
        return entries;
    }

    /** What a folder shares with listed items (`ItemCommands`): its quick link, Folder access…, Copy full path. */
    private shared(path: string): MenuEntry[] {
        const item = this.folderItem(path);
        const scope = { item, targets: [item] };
        return (['quickLink', 'folderAccess', 'copyPath'] as const).flatMap((id) => {
            const entry = this.commands.entry(id, scope);
            return entry ? [{ ...entry, group: id === 'copyPath' ? 'save' : 'organise' }] : [];
        });
    }

    /** A folder's properties: its dates and what it holds (no key: Alt+Enter is the selected item's). */
    private properties(path: string): MenuEntry[] {
        if (!this.config.can(Rights.browse) || !this.dialogs.properties()) return [];
        return [
            {
                id: 'properties',
                label: $localize`Properties`,
                icon: 'info',
                title: $localize`Show its dates and what it holds.`,
                run: () => void this.dialogs.properties()?.open(this.folderItem(path)),
                group: 'properties',
            },
        ];
    }

    private async copyLink(href: string): Promise<void> {
        try {
            await copyText(new URL(href, document.baseURI).href);
            this.toasts.show($localize`Link copied.`);
        } catch {
            this.toasts.show($localize`Could not copy the link.`, { level: 'error' });
        }
    }
}
