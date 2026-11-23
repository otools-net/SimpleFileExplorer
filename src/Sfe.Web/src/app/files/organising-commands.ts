import { inject, Injectable } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { CopyPathService } from '../core/copy-path.service';
import { Rights } from '../core/rights';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { QuickLinksService } from '../library/quick-links/quick-links.service';
import { ItemCommand, ItemCommandId, ItemMenuEntry, ItemScope, namesOf, refused } from './item-scope';

/**
 * The commands about listed items rather than their content (Tags…, quick links, Scans and previews…, Copy full path,
 * Properties), for `ItemCommands`. Quick links, copying paths and the scans of a folder are hidden when the session
 * has none (an account's own, a server setting, an administrator's right), not greyed out.
 */
@Injectable({ providedIn: 'root' })
export class OrganisingCommands {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly quickLinks = inject(QuickLinksService);
    private readonly copyPaths = inject(CopyPathService);
    private readonly config = inject(ConfigService);

    readonly commands: Partial<Record<ItemCommandId, ItemCommand>> = {
        tags: (scope) => this.tags(scope),
        quickLink: (scope) => this.quickLink(scope),
        exclusions: (scope) => this.exclusions(scope),
        copyPath: (scope) => this.copyPath(scope),
        properties: (scope) => this.properties(scope),
    };

    /** Properties of what is listed: whoever browses. */
    canSeeProperties(): boolean {
        return this.config.can(Rights.browse);
    }

    /** A file's tags; a folder's: all its files (asks first); several items: the files among them. */
    private tags(scope: ItemScope): ItemMenuEntry | null {
        const { item } = scope;
        if (scope.targets.length > 1) return this.selectionTags(scope);
        const folder = item.kind === 'directory';
        const entry: ItemMenuEntry = {
            id: 'tags',
            label: folder ? $localize`Tag its files…` : $localize`Tags…`,
            icon: 'tag',
            title: folder
                ? $localize`Add or remove tags on all the files of this folder (asks first).`
                : $localize`See or change the tags of this file.`,
            run: () =>
                folder
                    ? this.dialogs.scopeTags()?.open({ path: item.path, recursive: false, query: null })
                    : this.dialogs.fileTags()?.open(item),
        };
        const allowed = this.store.can(folder ? Rights.assignTags : Rights.viewTags, item);
        return refused(entry, allowed, 'tags', [item.name]);
    }

    private selectionTags(scope: ItemScope): ItemMenuEntry | null {
        const files = scope.targets.filter((target) => target.kind !== 'directory');
        if (!files.length) return null;
        const entry: ItemMenuEntry = {
            id: 'tags',
            label: $localize`Tags…`,
            icon: 'tag',
            title:
                files.length < scope.targets.length
                    ? $localize`Add or remove tags on the selected files (the selected folders are left out).`
                    : $localize`Add or remove tags on the selected files.`,
            run: () =>
                files.length === 1 ? this.dialogs.fileTags()?.open(files[0]) : this.dialogs.filesTags()?.open(files),
        };
        return refused(entry, this.store.can(Rights.assignTags), 'tags', namesOf(scope));
    }

    /** One item: in or out of the quick links; several: all of them in. */
    private quickLink(scope: ItemScope): ItemMenuEntry | null {
        if (!this.quickLinks.addable()) return null;
        const { item } = scope;
        if (scope.targets.length > 1)
            return {
                id: 'quickLink',
                label: $localize`Add to quick links`,
                icon: 'pin',
                title: $localize`Add the selected items to your quick links (or drag them onto the sidebar).`,
                run: () => void this.quickLinks.add(scope.targets.map((target) => target.path)),
            };
        const pinned = this.quickLinks.paths().has(item.path);
        return {
            id: 'quickLink',
            label: pinned ? $localize`Remove from quick links` : $localize`Add to quick links`,
            icon: 'pin',
            title: pinned
                ? $localize`Take “${item.name}:name:” out of your quick links.`
                : $localize`Show “${item.name}:name:” in your quick links, in the sidebar.`,
            run: () => void this.quickLinks.toggle(item.path),
        };
    }

    /** A folder kept out of scans, trickplay or thumbnails (for the whole space), while browsing. */
    private exclusions({ item, targets }: ItemScope): ItemMenuEntry | null {
        const applies =
            targets.length === 1 &&
            item.kind === 'directory' &&
            !this.store.searching() &&
            this.store.can(Rights.excludeFolders, item) &&
            Boolean(this.dialogs.folderExclusions());
        if (!applies) return null;
        return {
            id: 'exclusions',
            label: $localize`Scans and previews…`,
            icon: 'scan',
            title: $localize`Keep this folder out of scans, trickplay or thumbnails, \
for everyone using this space.`,
            run: () => this.dialogs.folderExclusions()?.open(item.path, item.excluded),
        };
    }

    private copyPath({ item }: ItemScope): ItemMenuEntry | null {
        if (!this.copyPaths.enabled()) return null;
        return {
            id: 'copyPath',
            label: $localize`Copy full path`,
            icon: 'copy',
            title:
                item.kind === 'directory'
                    ? $localize`Copy where this folder is on the server.`
                    : $localize`Copy where it is on the server.`,
            run: () => void this.copyPaths.copy(item.path),
        };
    }

    private properties({ item }: ItemScope): ItemMenuEntry | null {
        if (!this.canSeeProperties()) return null;
        return {
            id: 'properties',
            label: $localize`Properties`,
            icon: 'info',
            shortcut: 'Alt+Enter',
            title:
                item.kind === 'directory'
                    ? $localize`Show its dates and what it holds (Alt+Enter).`
                    : $localize`Show its size, its dates, what the index knows of it and its checksum (Alt+Enter).`,
            run: () => void this.dialogs.properties()?.open(item),
        };
    }
}
