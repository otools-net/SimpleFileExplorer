import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { CopyPathService } from '../../core/copy-path.service';
import { kindIcon } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { QuickLink } from '../library.models';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../../menus/context-menu';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { FolderMenu } from '../folder-menu';
import { ItemActions } from '../items/item-actions.service';
import { ListingStore } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { DRAG_ITEMS, DRAG_LINK, QuickLinksService } from './quick-links.service';
import { dragItemsData, parseDragItems } from '../../files/drag-items';

/**
 * The sidebar's quick links: folders open their listing, files open in their folder (media in the viewer). Folders
 * and files dragged from the page are added where they are dropped, links are dragged to reorder them; on phones
 * (no drag and drop) the Edit button shows buttons moving and removing them.
 */
@Component({
    selector: 'section[sfeQuickLinks]',
    imports: [IconComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'sidebar-section quick-links',
        '[attr.aria-label]': 'label',
        '[hidden]': '!links.enabled()',
        '[class.drop-ready]': 'links.dragging()',
        '[class.drop-over]': 'dropIndex() !== null',
        '[class.editing]': 'editing()',
        '(dragover)': 'onDragOver($event)',
        '(dragleave)': 'onDragLeave($event)',
        '(drop)': 'onDrop($event)',
        '(document:dragstart)': 'onPageDragStart($event)',
        '(document:dragend)': 'endDrag()',
        '(document:drop)': 'endDrag()',
    },
    templateUrl: './quick-links.component.html',
})
export class QuickLinksComponent implements OnInit {
    protected readonly links = inject(QuickLinksService);
    protected readonly store = inject(ListingStore);
    protected readonly config = inject(ConfigService);
    private readonly sidebar = inject(SidebarService);
    private readonly toasts = inject(ToastService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private readonly folders = inject(FolderMenu);
    private readonly actions = inject(ItemActions);
    private readonly copyPaths = inject(CopyPathService);
    private readonly dialogs = inject(Dialogs);

    protected readonly editing = signal(false);
    /**
     * Where a drop would go: before the link at this index (the list's length: at the end); null while nothing is over
     * it.
     */
    protected readonly dropIndex = signal<number | null>(null);
    /** The link being dragged to another place. */
    protected readonly moving = signal<string | null>(null);
    protected readonly emptyText = $localize`No quick links yet. Drag folders and files here, or use their pin button, \
to keep them at hand.`;
    protected readonly label = $localize`Quick links`;
    protected readonly doneTitle = $localize`Stop editing the quick links.`;
    protected readonly doneLabel = $localize`Done`;
    protected readonly editTitle = $localize`Edit the quick links: move and remove them.`;
    protected readonly editLabel = $localize`Edit the quick links`;
    private readonly route = computed(() => this.store.route());

    ngOnInit(): void {
        void this.links.load();
    }

    protected icon(link: QuickLink): string {
        return kindIcon[link.kind];
    }

    protected moveUpLabel(link: QuickLink): string {
        return $localize`Move ${link.name}:name: up`;
    }

    protected moveDownLabel(link: QuickLink): string {
        return $localize`Move ${link.name}:name: down`;
    }

    protected removeLabel(link: QuickLink): string {
        return $localize`Remove ${link.name}:name: from the quick links`;
    }

    /** Removing a link never touches the folder or file it leads to: the hint says so. */
    protected removeTitle(link: QuickLink): string {
        return link.kind === 'directory'
            ? $localize`Take “${link.name}:name:” out of the quick links. The folder stays where it is.`
            : $localize`Take “${link.name}:name:” out of the quick links. The file stays where it is.`;
    }

    protected isCurrent(link: QuickLink): boolean {
        const route = this.route();
        return link.kind === 'directory' && !route.q && route.path === link.path;
    }

    protected hint(link: QuickLink): string {
        const where = link.path ? `/${link.path}` : $localize`${link.name}:name: (the top of the library)`;
        return link.exists ? where : $localize`${where}:place:: not there any more (moved, renamed or deleted).`;
    }

    protected openFolder(event: MouseEvent, link: QuickLink): void {
        if (!this.plainClick(event)) return;
        if (!link.exists) {
            event.preventDefault();
            this.notFound(link);
            return;
        }
        this.sidebar.closeIfOverlay();
    }

    /** A plain click shows the file in its folder (media in the viewer); others (new tab...) follow the link. */
    protected openFile(event: MouseEvent, link: QuickLink): void {
        if (!this.plainClick(event)) return;
        event.preventDefault();
        if (!link.exists) {
            this.notFound(link);
            return;
        }
        this.sidebar.closeIfOverlay();
        this.store.reveal(link.path);
    }

    /**
     * The menu of a quick link: its folder's or file's entries (when it is still there), then moving it in the list
     * and taking it out.
     */
    protected menuTarget(link: QuickLink, index: number): MenuTarget {
        return {
            kind: link.kind === 'directory' ? 'folderLink' : 'fileLink',
            key: `quickLink:${link.path}`,
            name: link.name,
            entries: () => [...(link.exists ? this.targetEntries(link) : []), ...this.listEntries(link, index)],
        };
    }

    private targetEntries(link: QuickLink): MenuEntry[] {
        if (link.kind === 'directory')
            return this.folders
                .linkEntries(link.path)
                .filter((entry) => entry.id !== 'quickLink')
                .map((entry) =>
                    entry.id === 'open'
                        ? {
                              ...entry,
                              run: () => {
                                  this.sidebar.closeIfOverlay();
                                  entry.run?.();
                              },
                          }
                        : entry,
                );
        const item: LibraryItem = {
            name: link.name,
            path: link.path,
            kind: link.kind,
            extension: link.extension,
            size: null,
            modified: null,
        };
        const entries: MenuEntry[] = [
            {
                id: 'open',
                label: $localize`Show in its folder`,
                icon: 'folder',
                title: $localize`List the folder “${link.name}:name:” is in, and show it there.`,
                run: () => {
                    this.sidebar.closeIfOverlay();
                    this.store.reveal(link.path);
                },
            },
            {
                id: 'newTab',
                label: $localize`Open in new tab`,
                icon: 'external',
                title: $localize`Open “${link.name}:name:” in a tab of its own.`,
                href: this.actions.fileUrl(item),
                newTab: true,
            },
        ];
        if (this.copyPaths.enabled())
            entries.push({
                id: 'copyPath',
                label: $localize`Copy full path`,
                icon: 'copy',
                title: $localize`Copy where it is on the server.`,
                run: () => void this.copyPaths.copy(link.path),
            });
        if (this.dialogs.properties())
            entries.push({
                id: 'properties',
                label: $localize`Properties`,
                icon: 'info',
                title: $localize`Show its size, its dates and what the index knows of it.`,
                run: () => void this.dialogs.properties()?.open(item),
            });
        return entries;
    }

    private listEntries(link: QuickLink, index: number): MenuEntry[] {
        const entries: MenuEntry[] = [];
        if (index > 0)
            entries.push({
                id: 'moveUp',
                label: $localize`Move up`,
                icon: 'up',
                title: $localize`Move “${link.name}:name:” up in the quick links.`,
                run: () => void this.links.move(link.path, index - 1),
            });
        if (index < this.links.links().length - 1)
            entries.push({
                id: 'moveDown',
                label: $localize`Move down`,
                icon: 'down',
                title: $localize`Move “${link.name}:name:” down in the quick links.`,
                run: () => void this.links.move(link.path, index + 2),
            });
        entries.push({
            id: 'remove',
            label: $localize`Remove from quick links`,
            icon: 'close',
            title: this.removeTitle(link),
            run: () => void this.links.remove(link.path),
        });
        return entries;
    }

    /* ---------- Drag and drop ---------- */

    /** A drag started on the page: a folder or file (or the selection) can be dropped here. */
    protected onPageDragStart(event: DragEvent): void {
        // A finger drags nothing (a long press selects): Move to… and Copy to… do it there.
        if (
            typeof matchMedia === 'function' &&
            matchMedia('(hover: none) and (pointer: coarse)').matches &&
            !matchMedia('(any-pointer: fine)').matches
        ) {
            event.preventDefault();
            return;
        }
        if (!this.links.enabled() || !event.dataTransfer) return;
        const paths = this.links.dragPaths(event.target);
        if (!paths?.length) return;
        event.dataTransfer.setData(DRAG_ITEMS, dragItemsData(paths, this.config.place()?.id ?? null));
        // Every effect: dropping a file's link elsewhere (another tab, the desktop) keeps working, and on a folder of
        // the page it is moved or copied (OperationsPanelComponent).
        event.dataTransfer.effectAllowed = 'all';
        this.links.dragging.set(true);
    }

    protected startMove(event: DragEvent, link: QuickLink): void {
        if (this.editing() || !event.dataTransfer) return;
        event.dataTransfer.setData(DRAG_LINK, link.path);
        event.dataTransfer.effectAllowed = 'move';
        this.moving.set(link.path);
    }

    protected onDragOver(event: DragEvent): void {
        const types = event.dataTransfer?.types ?? [];
        const moving = types.includes(DRAG_LINK);
        if (!moving && (!types.includes(DRAG_ITEMS) || !this.links.addable())) return;
        event.preventDefault();
        event.dataTransfer!.dropEffect = moving ? 'move' : 'link';
        this.dropIndex.set(this.indexAt(event.clientY));
    }

    protected onDragLeave(event: DragEvent): void {
        if (!(event.relatedTarget instanceof Node) || !this.host.contains(event.relatedTarget))
            this.dropIndex.set(null);
    }

    protected onDrop(event: DragEvent): void {
        const index = this.dropIndex() ?? this.indexAt(event.clientY);
        const data = event.dataTransfer;
        this.endDrag();
        if (!data) return;
        const moved = data.getData(DRAG_LINK);
        const items = data.getData(DRAG_ITEMS);
        if (!moved && !items) return;
        event.preventDefault();
        if (moved) {
            void this.links.move(moved, index);
            return;
        }
        // Links are the library's: items of a connection (dragged from another tab) are not added.
        const dragged = parseDragItems(items);
        if (dragged.connection === null && dragged.paths.length) void this.links.add(dragged.paths, index);
    }

    protected endDrag(): void {
        this.links.dragging.set(false);
        this.moving.set(null);
        this.dropIndex.set(null);
    }

    /** The link a drop at `y` goes before: the first whose middle is below it (the list's length after the last). */
    private indexAt(y: number): number {
        const items = [...this.host.querySelectorAll<HTMLElement>('li.quick-link')];
        const index = items.findIndex((item) => {
            const box = item.getBoundingClientRect();
            return y < box.top + box.height / 2;
        });
        return index < 0 ? items.length : index;
    }

    private plainClick(event: MouseEvent): boolean {
        return event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
    }

    /** A gone target stays listed (it may come back): the user removes it. */
    private notFound(link: QuickLink): void {
        this.toasts.show(`“${link.name}” is not there any more (moved, renamed or deleted). Remove its link with ×.`);
    }
}
