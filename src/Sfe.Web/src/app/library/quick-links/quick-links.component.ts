import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { kindIcon } from '../../core/format';
import { QuickLink } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ListingStore } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { DRAG_ITEMS, DRAG_LINK, QuickLinksService } from './quick-links.service';

/**
 * The sidebar's quick links: folders open their listing, files open in their folder (media in the viewer). Folders
 * and files dragged from the page are added where they are dropped, links are dragged to reorder them; on phones
 * (no drag and drop) the Edit button shows buttons moving and removing them.
 */
@Component({
    selector: 'section[sfeQuickLinks]',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'sidebar-section quick-links',
        'aria-label': 'Quick links',
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
    template: `
        <div class="sidebar-heading">
            <h2>Quick links</h2>
            @if (links.links().length) {
                <button type="button" class="icon-button ghost small quick-links-edit" [class.active]="editing()" [attr.aria-pressed]="editing()"
                        [title]="editing() ? 'Done' : 'Edit: move and remove the links'" [attr.aria-label]="editing() ? 'Done' : 'Edit the quick links'"
                        (click)="editing.set(!editing())">
                    <svg [sfeIcon]="editing() ? 'check' : 'edit'"></svg>
                </button>
            }
        </div>
        <ul class="quick-link-list">
            @for (link of links.links(); track link.path; let i = $index, first = $first, last = $last) {
                <li class="quick-link" [class.missing]="!link.exists" [class.current]="isCurrent(link)" [class.dragged]="moving() === link.path"
                    [class.drop-before]="dropIndex() === i" [attr.data-quick-link]="link.path" [attr.data-kind]="link.kind" [attr.draggable]="!editing()"
                    (dragstart)="startMove($event, link)" (dragend)="endDrag()">
                    @if (link.kind === 'directory') {
                        <a class="quick-link-target" routerLink="/" [queryParams]="store.browseParams(link.path)" draggable="false"
                           [attr.aria-current]="isCurrent(link) ? 'page' : null" [title]="hint(link)" (click)="openFolder($event, link)">
                            <svg [sfeIcon]="icon(link)"></svg><span class="quick-link-name">{{ link.name }}</span>
                        </a>
                    } @else {
                        <a class="quick-link-target" [href]="config.links.file(link.path)" target="_blank" rel="noopener" draggable="false"
                           [title]="hint(link)" (click)="openFile($event, link)">
                            <svg [sfeIcon]="icon(link)"></svg><span class="quick-link-name">{{ link.name }}</span>
                        </a>
                    }
                    <span class="quick-link-actions">
                        @if (editing()) {
                            <button type="button" class="icon-button ghost small" [disabled]="first" [title]="'Move “' + link.name + '” up'"
                                    [attr.aria-label]="'Move ' + link.name + ' up'" (click)="links.move(link.path, i - 1)">
                                <svg sfeIcon="up"></svg>
                            </button>
                            <button type="button" class="icon-button ghost small" [disabled]="last" [title]="'Move “' + link.name + '” down'"
                                    [attr.aria-label]="'Move ' + link.name + ' down'" (click)="links.move(link.path, i + 2)">
                                <svg sfeIcon="down"></svg>
                            </button>
                        }
                        <button type="button" class="icon-button ghost small quick-link-remove" [title]="'Remove “' + link.name + '” from the quick links (the ' + kindWord(link) + ' stays)'"
                                [attr.aria-label]="'Remove ' + link.name + ' from the quick links'" (click)="links.remove(link.path)">
                            <svg sfeIcon="close"></svg>
                        </button>
                    </span>
                </li>
            } @empty {
                @if (links.loaded()) {
                    <li class="sidebar-empty">{{ emptyText }}</li>
                }
            }
            @if (links.links().length && dropIndex() === links.links().length) {
                <li class="quick-link-drop-end" aria-hidden="true"></li>
            }
        </ul>
    `,
})
export class QuickLinksComponent implements OnInit {
    protected readonly links = inject(QuickLinksService);
    protected readonly store = inject(ListingStore);
    protected readonly config = inject(ConfigService);
    private readonly sidebar = inject(SidebarService);
    private readonly toasts = inject(ToastService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    protected readonly editing = signal(false);
    /** Where a drop would go: before the link at this index (the list's length: at the end); null while nothing is over it. */
    protected readonly dropIndex = signal<number | null>(null);
    /** The link being dragged to another place. */
    protected readonly moving = signal<string | null>(null);
    protected readonly emptyText = 'Drag folders and files here, or use their pin button, to keep them at hand.';
    private readonly route = computed(() => this.store.route());

    ngOnInit(): void {
        void this.links.load();
    }

    protected icon(link: QuickLink): string {
        return kindIcon[link.kind];
    }

    protected kindWord(link: QuickLink): string {
        return link.kind === 'directory' ? 'folder' : 'file';
    }

    protected isCurrent(link: QuickLink): boolean {
        const route = this.route();
        return link.kind === 'directory' && !route.q && route.path === link.path;
    }

    protected hint(link: QuickLink): string {
        const where = link.path ? `/${link.path}` : `${link.name} (the library's folder)`;
        return link.exists ? where : `${where}: not found any more (moved, renamed or deleted)`;
    }

    protected openFolder(event: MouseEvent, link: QuickLink): void {
        if (!this.plainClick(event))
            return;
        if (!link.exists) {
            event.preventDefault();
            this.notFound(link);
            return;
        }
        this.sidebar.closeIfOverlay();
    }

    /** A plain click shows the file in its folder (media in the viewer); others (new tab...) follow the link. */
    protected openFile(event: MouseEvent, link: QuickLink): void {
        if (!this.plainClick(event))
            return;
        event.preventDefault();
        if (!link.exists) {
            this.notFound(link);
            return;
        }
        this.sidebar.closeIfOverlay();
        this.store.reveal(link.path);
    }

    /* ---------- Drag and drop ---------- */

    /** A drag started on the page: a folder or file (or the selection) can be dropped here. */
    protected onPageDragStart(event: DragEvent): void {
        if (!this.links.enabled() || !event.dataTransfer)
            return;
        const paths = this.links.dragPaths(event.target);
        if (!paths?.length)
            return;
        event.dataTransfer.setData(DRAG_ITEMS, JSON.stringify(paths));
        // Copy and link: dropping a file's link elsewhere (another tab, the desktop) keeps working.
        event.dataTransfer.effectAllowed = 'copyLink';
        this.links.dragging.set(true);
    }

    protected startMove(event: DragEvent, link: QuickLink): void {
        if (this.editing() || !event.dataTransfer)
            return;
        event.dataTransfer.setData(DRAG_LINK, link.path);
        event.dataTransfer.effectAllowed = 'move';
        this.moving.set(link.path);
    }

    protected onDragOver(event: DragEvent): void {
        const types = event.dataTransfer?.types ?? [];
        const moving = types.includes(DRAG_LINK);
        if (!moving && !types.includes(DRAG_ITEMS))
            return;
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
        if (!data)
            return;
        const moved = data.getData(DRAG_LINK);
        const items = data.getData(DRAG_ITEMS);
        if (!moved && !items)
            return;
        event.preventDefault();
        if (moved) {
            void this.links.move(moved, index);
            return;
        }
        const paths = parsePaths(items);
        if (paths.length)
            void this.links.add(paths, index);
    }

    protected endDrag(): void {
        this.links.dragging.set(false);
        this.moving.set(null);
        this.dropIndex.set(null);
    }

    /** The link a drop at `y` goes before: the first whose middle is below it (the list's length after the last). */
    private indexAt(y: number): number {
        const items = [...this.host.querySelectorAll<HTMLElement>('li.quick-link')];
        const index = items.findIndex(item => {
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

function parsePaths(json: string): string[] {
    try {
        const value: unknown = JSON.parse(json);
        return Array.isArray(value) ? value.filter((path): path is string => typeof path === 'string') : [];
    } catch {
        return [];
    }
}
