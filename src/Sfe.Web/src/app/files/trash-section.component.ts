import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate, formatSize, joinParts, plural } from '../core/format';
import { isFileConflict, TrashItem } from '../core/models';
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
    template: `
        <p class="settings-intro">{{ intro() }}</p>
        @if (canPurge()) {
            <div class="settings-action">
                <button type="button" class="button small danger" title="Delete for good everything of the trash you may (asks first)"
                        [sfeBusy]="emptying()" [disabled]="emptying() || !items().length" (click)="empty()">Empty the trash</button>
            </div>
        }
        <ul class="tracking-missing-list">
            @if (loading()) {
                <li class="tag-loading"><span class="spinner"></span></li>
            } @else if (!items().length) {
                <li class="muted trash-empty">The trash is empty.</li>
            }
            @for (item of items(); track item.id) {
                <li class="tracking-missing" [attr.data-id]="item.id" [sfeContextMenu]="rowMenu(item)">
                    <div class="tracking-missing-head">
                        <span class="tracking-thumb"><svg [sfeIcon]="item.isFolder ? 'folder' : 'file'"></svg></span>
                        <span class="tracking-file">
                            <strong class="tracking-name" [title]="item.path">{{ item.name }}</strong>
                            @if (item.previousVersion) {
                                <span class="trash-version" title="The file as it was before it was saved over in the text editor: restored, it comes back next to it">previous version</span>
                            }
                            <span class="tracking-was">{{ describe(item) }}</span>
                            <span class="tracking-was">From
                                <a routerLink="/" [queryParams]="{ path: folderOf(item) || null }" [title]="'Open ' + (folderOf(item) || 'the library')">{{ folderOf(item) || rootName() }}</a>
                            </span>
                        </span>
                        <span class="tracking-actions">
                            @if (canRestore()) {
                                <button type="button" class="button small primary" title="Put it back where it was (a number is added to its name if that is taken)"
                                        [sfeBusy]="busy() === item.id + ':restore'" [disabled]="busy() !== null" (click)="restore(item)">Restore</button>
                            }
                            @if (canPurge()) {
                                <button type="button" class="button small" title="Delete it for good (asks first)"
                                        [sfeBusy]="busy() === item.id + ':purge'" [disabled]="busy() !== null" (click)="purge(item)">
                                    <svg sfeIcon="trash"></svg>Delete
                                </button>
                            }
                        </span>
                    </div>
                </li>
            }
        </ul>
        <button type="button" class="button" title="Show the next page" [hidden]="!next()" [sfeBusy]="loadingMore()" [disabled]="loadingMore()" (click)="more()">Load more</button>
    `,
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
    protected readonly loading = signal(true);
    protected readonly loadingMore = signal(false);
    protected readonly emptying = signal(false);
    /** `{id}:{action}` of the entry being restored or deleted. */
    protected readonly busy = signal<string | null>(null);

    protected readonly canRestore = computed(() => this.config.can(Rights.deleteFiles));
    protected readonly canPurge = computed(() => this.config.can(Rights.deletePermanently));
    protected readonly rootName = computed(() => this.config.config()?.rootName ?? 'the library');
    protected readonly intro = computed(() => {
        const days = this.retention();
        const keeps = days === null ? '' : days === 0
            ? ' It keeps them until they are restored or deleted for good.'
            : ` Each is deleted for good ${plural(days, 'day')} after it was deleted.`;
        return 'You see what you deleted, and what was deleted in folders where you may delete. '
            + 'Restoring puts an item back where it was, with its tags and folder access.' + keeps;
    });

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
        } catch (error) {
            this.toasts.error(`Could not read the trash: ${(error as Error).message}`);
        } finally {
            this.loading.set(false);
        }
    }

    protected async more(): Promise<void> {
        const next = this.next();
        if (next === null)
            return;
        try {
            const page = await whileBusy(this.loadingMore, () => this.api.trash(next));
            this.items.update(items => [...items, ...page.items]);
            this.next.set(page.hasMore ? page.next : null);
        } catch (error) {
            this.toasts.error(`Could not read the trash: ${(error as Error).message}`);
        }
    }

    protected describe(item: TrashItem): string {
        return joinParts([
            item.isFolder && item.files !== null ? plural(item.files, 'file') : null,
            item.size !== null ? formatSize(item.size) : null,
            item.previousVersion ? `saved over ${formatDate(item.deletedAt)}` : `deleted ${formatDate(item.deletedAt)}`,
            item.deletedBy ? `by ${item.deletedBy}` : null,
        ]);
    }

    /** The menu of an entry (a right click): its buttons, and the folder it was deleted from (here or in a new tab). */
    protected rowMenu(item: TrashItem): MenuTarget {
        return { kind: 'trash', key: `trash:${item.id}`, name: item.name, entries: () => this.rowEntries(item) };
    }

    private rowEntries(item: TrashItem): MenuEntry[] {
        const entries: MenuEntry[] = [];
        if (this.canRestore() && this.busy() === null)
            entries.push({ id: 'restore', label: 'Restore', icon: 'undo', title: 'Put it back where it was (a number is added to its name if that is taken)', run: () => void this.restore(item) });
        if (this.canPurge() && this.busy() === null)
            entries.push({ id: 'purge', label: 'Delete for good…', icon: 'trash', title: 'Delete it for good (asks first)', run: () => void this.purge(item) });
        const folder = this.folderOf(item);
        const params = { path: folder || null };
        const name = folder || this.rootName();
        entries.push(
            { id: 'openFolder', label: 'Open its folder', icon: 'folder', title: `List ${name}, where it was`, run: () => void this.router.navigate(['/'], { queryParams: params }) },
            { id: 'folderNewTab', label: 'Open its folder in new tab', icon: 'external', title: `List ${name} in a tab of its own`, href: this.store.pageUrl(params), newTab: true },
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
                this.toasts.error(result.detail ?? `“${item.name}” could not be restored.`);
                return;
            }
            this.remove(item.id);
            this.toasts.show(result.path === item.path ? `“${item.name}” is back.` : `“${item.name}” is back, as “${result.path}”.`);
        } catch (error) {
            this.toasts.error((error as Error).message);
        } finally {
            this.busy.set(null);
        }
    }

    protected async purge(item: TrashItem): Promise<void> {
        if (!confirm(`Delete “${item.name}” for good? This can't be undone.`))
            return;
        this.busy.set(`${item.id}:purge`);
        try {
            await this.api.purgeFromTrash(item.id);
            this.remove(item.id);
        } catch (error) {
            this.toasts.error((error as Error).message);
        } finally {
            this.busy.set(null);
        }
    }

    protected async empty(): Promise<void> {
        if (!confirm('Delete for good everything of the trash you may? This can\'t be undone.'))
            return;
        try {
            const result = await whileBusy(this.emptying, () => this.api.emptyTrash());
            this.toasts.show(`${plural(result.purged, 'item')} deleted for good.`);
            await this.load();
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }

    private remove(id: number): void {
        this.items.update(items => items.filter(item => item.id !== id));
    }
}
