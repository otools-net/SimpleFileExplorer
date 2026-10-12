import { ChangeDetectionStrategy, Component, ElementRef, inject, OnInit } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { plural } from '../../core/format';
import { TagSummary, TagUsage } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { tagQuery } from '../../tags/tags';
import { SidebarService } from './sidebar.service';

interface Section {
    key: 'popular' | 'searched' | 'viewed' | 'lucky';
    title: string;
    empty: string;
    history?: boolean;
}

const SECTIONS: Section[] = [
    { key: 'popular', title: 'Most popular', empty: 'No file is tagged yet.' },
    { key: 'searched', title: 'Last searched', empty: 'Search with t:name to see the tags here.', history: true },
    { key: 'viewed', title: 'Last viewed', empty: 'Open a tagged file to see its tags here.', history: true },
    { key: 'lucky', title: 'I feel lucky', empty: 'No other tags.' },
];

/** Collapsible tag sidebar: most used tags, last searched tags, tags of the last viewed files and random others. */
@Component({
    selector: 'sfe-sidebar',
    imports: [IconComponent, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'sidebar',
        id: 'sidebar',
        'aria-label': 'Tags',
        role: 'complementary',
        '(document:keydown.escape)': 'closeDrawer()',
        '(document:click)': 'closeOnOutsideClick($event)',
    },
    template: `
        @for (section of sections; track section.key) {
            @let usages = sidebar.data()?.[section.key] ?? [];
            <section class="sidebar-section" [hidden]="section.history && sidebar.data()?.historyEnabled === false">
                <div class="sidebar-heading">
                    <h2>{{ section.title }}</h2>
                    @if (section.key === 'lucky') {
                        <button type="button" class="icon-button ghost small" title="Other random tags" aria-label="Other random tags" (click)="sidebar.reload()">
                            <svg sfeIcon="refresh"></svg>
                        </button>
                    }
                </div>
                <ul class="sidebar-tags">
                    @for (usage of usages; track usage.tag.id) {
                        <li>
                            <sfe-tag-chip [tag]="usage.tag" [title]="'Find files tagged ' + usage.tag.displayName" [clickable]="true" (pick)="search($event)" />
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
        @if (config.can('tags.manage')) {
            <button type="button" class="button small sidebar-manage" title="Create, edit and delete tags" (click)="manage()">
                <svg sfeIcon="tag"></svg>Manage tags
            </button>
        }
    `,
})
export class SidebarComponent implements OnInit {
    protected readonly sidebar = inject(SidebarService);
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    protected readonly config = inject(ConfigService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    protected readonly sections = SECTIONS;

    ngOnInit(): void {
        void this.sidebar.reload();
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
