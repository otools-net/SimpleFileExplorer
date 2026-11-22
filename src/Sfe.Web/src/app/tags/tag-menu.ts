import { inject, Injectable } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { LibraryItem } from '../core/models';
import { TagSummary } from '../core/tag.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { routeParams } from '../library/library-route';
import { ListingStore } from '../library/listing.store';
import { MenuEntry, MenuTarget } from '../menus/context-menu';
import { tagQuery } from './tags';

/**
 * The menu of a tag chip that searches when clicked (on a listed file, in the sidebar, in the viewer's bar): search
 * for it (here or in a new tab), add it to the search or leave its files out, take it off the file, edit the tag.
 */
@Injectable({ providedIn: 'root' })
export class TagMenu {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    /** The menu of `tag`, shown on `file` when given (it can be taken off there). */
    target(tag: TagSummary, file?: LibraryItem | null): MenuTarget {
        return {
            kind: 'tag',
            key: `tag:${tag.id}:${file?.path ?? ''}`,
            name: tag.displayName || tag.name,
            entries: () => this.entries(tag, file ?? null),
        };
    }

    entries(tag: TagSummary, file: LibraryItem | null): MenuEntry[] {
        const name = tag.displayName || tag.name;
        const term = tagQuery(tag);
        const q = this.store.route().q.trim();
        const entries: MenuEntry[] = [
            {
                id: 'search',
                label: $localize`Search this tag`,
                icon: 'search',
                title: $localize`Find the files tagged “${name}:tag:”.`,
                run: () => this.store.navigate({ q: term }),
            },
            {
                id: 'newTab',
                label: $localize`Search in new tab`,
                icon: 'external',
                title: $localize`Find the files tagged “${name}:tag:”, in a tab of its own.`,
                newTab: true,
                href: this.store.pageUrl(routeParams({ ...this.store.route(), q: term })),
            },
        ];
        if (q && !q.includes(term))
            entries.push({
                id: 'addToSearch',
                label: $localize`Add to the search`,
                icon: 'plus',
                title: $localize`Keep only the results also tagged “${name}:tag:”.`,
                run: () => this.store.navigate({ q: `${q} ${term}` }),
            });
        if (!q.includes(`-${term}`))
            entries.push({
                id: 'exclude',
                label: q ? $localize`Leave out of the search` : $localize`Search without this tag`,
                icon: 'close',
                title: q
                    ? $localize`Keep only the results not tagged “${name}:tag:”.`
                    : $localize`Find the files not tagged “${name}:tag:”.`,
                run: () => this.store.navigate({ q: q && q !== term ? `${q} -${term}` : `-${term}` }),
            });
        if (file && file.kind !== 'directory' && this.store.can(Rights.assignTags, file))
            entries.push({
                id: 'remove',
                label: $localize`Remove from this file`,
                icon: 'close',
                title: $localize`Take “${name}:tag:” off “${file.name}:file:”.`,
                run: () => void this.removeFrom(tag, file),
            });
        if (this.config.can(Rights.manageTags) && this.dialogs.tagEditor())
            entries.push({
                id: 'edit',
                label: $localize`Edit the tag…`,
                icon: 'edit',
                title: $localize`Change its name, colour, parents and more.`,
                run: () => void this.dialogs.tagEditor()?.open(tag.id),
            });
        return entries;
    }

    private async removeFrom(tag: TagSummary, file: LibraryItem): Promise<void> {
        try {
            const result = await this.api.changeFileTags(
                { paths: [file.path], remove: [tag.id] },
                this.config.place()?.id,
            );
            this.store.applyFileTags(result.files);
        } catch (error) {
            this.toasts.error(`Could not remove the tag: ${(error as Error).message}`);
        }
    }
}
