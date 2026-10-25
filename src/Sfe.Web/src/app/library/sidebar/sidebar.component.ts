import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { plural } from '../../core/format';
import { TagSummary, TagUsage, TrashCount } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { ContextMenuDirective } from '../../menus/context-menu';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { TagMenu } from '../../tags/tag-menu';
import { ChangeFeed } from '../change-feed.service';
import { Dialogs } from '../dialogs.service';
import { QuickLinksComponent } from '../quick-links/quick-links.component';
import { ListingStore } from '../listing.store';
import { tagQuery } from '../../tags/tags';
import { SidebarService } from './sidebar.service';

interface Section {
    key: 'popular' | 'searched' | 'viewed' | 'lucky';
    title: string;
    empty: string;
    history?: boolean;
    /** Folded until the user opens it: the lists that help least. */
    folded?: boolean;
}

/** The sections this browser's user folded or opened: `{ key: folded }`, the others as `SECTIONS` says. */
const FOLDED_PREFERENCE = 'sfe.sidebar.sections';

const SECTIONS: Section[] = [
    { key: 'popular', title: 'Most popular', empty: 'No file is tagged yet.' },
    { key: 'searched', title: 'Last searched', empty: 'Search with t:name to see the tags here.', history: true, folded: true },
    { key: 'viewed', title: 'Last viewed', empty: 'Open a tagged file to see its tags here.', history: true },
    { key: 'lucky', title: 'I feel lucky', empty: 'No other tags.', folded: true },
];

/**
 * Collapsible sidebar: the user's quick links, then the tags: most used tags, last searched tags, tags of the last
 * viewed files and random others, each list folding under its heading; at the bottom, Manage tags and the trash.
 */
@Component({
    selector: 'sfe-sidebar',
    imports: [IconComponent, TagChipComponent, QuickLinksComponent, ContextMenuDirective, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'sidebar',
        id: 'sidebar',
        'aria-label': 'Quick links and tags',
        role: 'complementary',
        '(document:keydown.escape)': 'closeDrawer()',
        '(document:click)': 'closeOnOutsideClick($event)',
    },
    template: `
        <section sfeQuickLinks></section>
        @for (section of sections; track section.key) {
            @let usages = sidebar.data()?.[section.key] ?? [];
            @let open = !isFolded(section);
            <section class="sidebar-section" [hidden]="section.history && sidebar.data()?.historyEnabled === false">
                <div class="sidebar-heading">
                    <h2>
                        <button type="button" class="sidebar-fold" [attr.aria-expanded]="open" [attr.aria-controls]="'sidebar-tags-' + section.key"
                                [title]="(open ? 'Fold ' : 'Show ') + section.title" (click)="toggleFolded(section)">
                            <svg sfeIcon="down"></svg>{{ section.title }}
                        </button>
                    </h2>
                    @if (section.key === 'lucky' && open) {
                        <button type="button" class="icon-button ghost small" title="Other random tags" aria-label="Other random tags" (click)="sidebar.reload()">
                            <svg sfeIcon="refresh"></svg>
                        </button>
                    }
                </div>
                <ul class="sidebar-tags" [id]="'sidebar-tags-' + section.key" [hidden]="!open">
                    @for (usage of usages; track usage.tag.id) {
                        <li>
                            <sfe-tag-chip [tag]="usage.tag" [title]="'Find files tagged ' + usage.tag.displayName" [clickable]="true" (pick)="search($event)"
                                          [sfeContextMenu]="tagMenu.target(usage.tag)" />
                            <span class="tag-count" [title]="countTitle(usage)">{{ usage.fileCount.toLocaleString() }}</span>
                        </li>
                    } @empty {
                        @if (sidebar.data()) {
                            <li class="sidebar-empty">{{ section.empty }}</li>
                        }
                    }
                </ul>
            </section>
        }
        <div class="sidebar-footer">
            @if (config.can('tags.manage')) {
                <button type="button" class="button small sidebar-manage" title="Create, edit and delete tags" (click)="manage()">
                    <svg sfeIcon="tag"></svg>Manage tags
                </button>
            }
            @if (config.canAny(trashRights)) {
                @let trash = sidebar.trash();
                <a class="button small sidebar-trash" [class.full]="!!trash?.count" routerLink="/admin" [queryParams]="{ section: 'files', page: 'trash' }"
                   [state]="{ sfeFromLibrary: true }" [title]="trashTitle(trash)" [attr.aria-label]="trashTitle(trash)" (click)="sidebar.closeIfOverlay()">
                    <svg [sfeIcon]="trash?.count ? 'trash-full' : 'trash'"></svg>Trash
                    @if (trash?.count) {
                        <span class="sidebar-badge" aria-hidden="true">{{ trash!.count }}{{ trash!.more ? '+' : '' }}</span>
                    }
                </a>
            }
        </div>
    `,
})
export class SidebarComponent implements OnInit {
    protected readonly sidebar = inject(SidebarService);
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    protected readonly config = inject(ConfigService);
    protected readonly tagMenu = inject(TagMenu);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    protected readonly sections = SECTIONS;
    /** The rights of Administration › File management › Trash. */
    protected readonly trashRights = [Rights.deleteFiles, Rights.deletePermanently];
    private readonly folded = signal(readFolded());

    constructor() {
        // Something came into or left the trash (told by the change feed): its count is read again.
        const feed = inject(ChangeFeed);
        let seen = feed.trashChanges();
        effect(() => {
            const changes = feed.trashChanges();
            if (changes !== seen && this.config.canAny(this.trashRights))
                this.sidebar.refreshTrash();
            seen = changes;
        });
    }

    ngOnInit(): void {
        void this.sidebar.reload();
        if (this.config.canAny(this.trashRights))
            void this.sidebar.reloadTrash();
    }

    protected trashTitle(trash: TrashCount | null): string {
        if (!trash)
            return 'Trash';
        if (!trash.count)
            return 'Trash: empty';
        return trash.more ? `Trash: more than ${trash.count} items` : `Trash: ${plural(trash.count, 'item')}`;
    }

    protected isFolded(section: Section): boolean {
        return this.folded()[section.key] ?? section.folded ?? false;
    }

    protected toggleFolded(section: Section): void {
        this.folded.update(folded => ({ ...folded, [section.key]: !this.isFolded(section) }));
        writePreference(FOLDED_PREFERENCE, JSON.stringify(this.folded()));
    }

    protected countTitle(usage: TagUsage): string {
        return plural(usage.fileCount, 'file');
    }

    protected search(tag: TagSummary): void {
        this.sidebar.closeIfOverlay();
        this.store.navigate({ q: tagQuery(tag) });
    }

    protected manage(): void {
        this.sidebar.closeIfOverlay();
        this.dialogs.tagManager()?.open();
    }

    protected closeDrawer(): void {
        if (this.sidebar.narrow && this.sidebar.open())
            this.sidebar.setOpen(false, { remember: false });
    }

    /** Tapping the page next to the drawer closes it. */
    protected closeOnOutsideClick(event: MouseEvent): void {
        const target = event.target as Element;
        if (this.sidebar.narrow && this.sidebar.open() && !this.host.contains(target) && !target.closest?.('.sidebar-toggle'))
            this.sidebar.setOpen(false, { remember: false });
    }
}

function readFolded(): Partial<Record<Section['key'], boolean>> {
    try {
        const value: unknown = JSON.parse(readPreference(FOLDED_PREFERENCE, '{}'));
        return value && typeof value === 'object' ? value as Partial<Record<Section['key'], boolean>> : {};
    } catch {
        return {};
    }
}
