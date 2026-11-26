import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { isFileConflict } from '../core/models';
import { TrashItem, TrashSize } from './file.models';
import {
    describeTrashed,
    emptyQuestion,
    notRestored,
    openFolderHint,
    openFolderInTabHint,
    purgedText,
    purgeHint,
    purgeQuestion,
    restoredText,
    restoreHint,
    trashIntro,
    trashSizeText,
} from './trash-texts';
import { parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { ListingStore } from '../library/listing.store';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../menus/context-menu';

/**
 * Administration › File management › Trash: what was deleted to the trash (that the session may see), newest first,
 * to put back where it was or delete for good; emptying it; how long things stay.
 */
@Component({
    selector: 'sfe-trash-section',
    imports: [BusyDirective, IconComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel trash-section' },
    templateUrl: './trash-section.component.html',
})
export class TrashSectionComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly store = inject(ListingStore);
    private readonly router = inject(Router);

    protected readonly items = signal<TrashItem[]>([]);
    protected readonly next = signal<number | null>(null);
    protected readonly retention = signal<number | null>(null);
    /** What the whole trash holds; null when the session doesn't see all of it. */
    private readonly size = signal<TrashSize | null>(null);
    protected readonly loading = signal(true);
    protected readonly loadingMore = signal(false);
    protected readonly emptying = signal(false);
    /** `{id}:{action}` of the entry being restored or deleted. */
    protected readonly busy = signal<string | null>(null);

    protected readonly canRestore = computed(() => this.config.can(Rights.deleteFiles));
    protected readonly canPurge = computed(() => this.config.can(Rights.deletePermanently));
    protected readonly rootName = computed(() => this.config.config()?.rootName ?? $localize`The library`);
    protected readonly intro = computed(() => trashIntro(this.retention()));
    protected readonly sizeText = computed(() => {
        const size = this.size();
        return size && this.items().length ? trashSizeText(size) : null;
    });
    protected readonly restoreHint = restoreHint();
    protected readonly purgeHint = purgeHint();

    async ngOnInit(): Promise<void> {
        await this.load();
    }

    private async load(): Promise<void> {
        this.loading.set(true);
        try {
            const page = await this.api.trash();
            this.items.set(page.items);
            this.next.set(page.hasMore ? page.next : null);
            this.retention.set(page.retentionDays);
            this.size.set(page.size ?? null);
        } catch (error) {
            this.toasts.error($localize`Could not read the trash: ${(error as Error).message}:reason:`);
        } finally {
            this.loading.set(false);
        }
    }

    protected async more(): Promise<void> {
        const next = this.next();
        if (next === null) return;
        try {
            const page = await whileBusy(this.loadingMore, () => this.api.trash(next));
            this.items.update((items) => [...items, ...page.items]);
            this.next.set(page.hasMore ? page.next : null);
        } catch (error) {
            this.toasts.error($localize`Could not read more of the trash: ${(error as Error).message}:reason:`);
        }
    }

    protected describe(item: TrashItem): string {
        return describeTrashed(item);
    }

    protected openHint(item: TrashItem): string {
        return openFolderHint(this.folderOf(item));
    }

    /** The menu of an entry (a right click): its buttons, and the folder it was deleted from (here or in a new tab). */
    protected rowMenu(item: TrashItem): MenuTarget {
        return { kind: 'trash', key: `trash:${item.id}`, name: item.name, entries: () => this.rowEntries(item) };
    }

    private rowEntries(item: TrashItem): MenuEntry[] {
        const entries: MenuEntry[] = [];
        if (this.canRestore() && this.busy() === null)
            entries.push({
                id: 'restore',
                label: $localize`Restore`,
                icon: 'undo',
                title: this.restoreHint,
                run: () => void this.restore(item),
            });
        if (this.canPurge() && this.busy() === null)
            entries.push({
                id: 'purge',
                label: $localize`Delete for good…`,
                icon: 'trash',
                title: this.purgeHint,
                run: () => void this.purge(item),
            });
        const folder = this.folderOf(item);
        const params = { path: folder || null };
        entries.push(
            {
                id: 'openFolder',
                label: $localize`Open its folder`,
                icon: 'folder',
                title: openFolderHint(folder),
                run: () => void this.router.navigate(['/'], { queryParams: params }),
            },
            {
                id: 'folderNewTab',
                label: $localize`Open its folder in new tab`,
                icon: 'external',
                title: openFolderInTabHint(folder),
                href: this.store.pageUrl(params),
                newTab: true,
            },
        );
        return entries;
    }

    protected folderOf(item: TrashItem): string {
        return parentPath(item.path);
    }

    protected async restore(item: TrashItem): Promise<void> {
        this.busy.set(`${item.id}:restore`);
        try {
            const result = await this.api.restoreFromTrash(item.id);
            if (isFileConflict(result)) {
                this.toasts.error(notRestored(item.name, result.detail));
                return;
            }
            this.remove(item.id);
            this.toasts.success(restoredText(item.name, result.path === item.path ? null : result.path));
        } catch (error) {
            this.toasts.error(notRestored(item.name, (error as Error).message));
        } finally {
            this.busy.set(null);
        }
    }

    protected async purge(item: TrashItem): Promise<void> {
        if (!confirm(purgeQuestion(item.name))) return;
        this.busy.set(`${item.id}:purge`);
        try {
            await this.api.purgeFromTrash(item.id);
            this.remove(item.id);
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error($localize`Could not delete “${item.name}:name:” for good: ${reason}:reason:`);
        } finally {
            this.busy.set(null);
        }
    }

    protected async empty(): Promise<void> {
        if (!confirm(emptyQuestion())) return;
        try {
            const result = await whileBusy(this.emptying, () => this.api.emptyTrash());
            this.toasts.show(purgedText(result.purged));
            await this.load();
        } catch (error) {
            this.toasts.error($localize`Could not empty the trash: ${(error as Error).message}:reason:`);
        }
    }

    /** An entry restored or deleted for good leaves the list and the trash's total. */
    private remove(id: number): void {
        const gone = this.items().find((item) => item.id === id);
        this.items.update((items) => items.filter((item) => item.id !== id));
        const size = this.size();
        if (gone && size)
            this.size.set(
                gone.size === null
                    ? { ...size, unknown: Math.max(0, size.unknown - 1) }
                    : { ...size, bytes: Math.max(0, size.bytes - gone.size) },
            );
    }
}
