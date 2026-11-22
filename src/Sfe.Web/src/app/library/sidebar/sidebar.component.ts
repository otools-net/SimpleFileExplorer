import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { icu } from '../../core/i18n/icu';
import { TagSummary, TagUsage } from '../../core/tag.models';
import { TrashCount } from '../library.models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { ContextMenuDirective } from '../../menus/context-menu';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { TagMenu } from '../../tags/tag-menu';
import { ChangeFeed } from '../change-feed.service';
import { Dialogs } from '../dialogs.service';
import { QuickLinksComponent } from '../quick-links/quick-links.component';
import { ConnectionLinksComponent } from './connection-links.component';
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
    { key: 'popular', title: $localize`Most popular`, empty: $localize`No file is tagged yet.` },
    {
        key: 'searched',
        title: $localize`Last searched`,
        empty: $localize`Search with t:name to see the tags here.`,
        history: true,
        folded: true,
    },
    {
        key: 'viewed',
        title: $localize`Last viewed`,
        empty: $localize`Open a tagged file to see its tags here.`,
        history: true,
    },
    { key: 'lucky', title: $localize`I feel lucky`, empty: $localize`No other tags.`, folded: true },
];

/**
 * Collapsible sidebar: the user's quick links, then the tags: most used tags, last searched tags, tags of the last
 * viewed files and random others, each list folding under its heading; at the bottom, Manage tags and the trash.
 */
@Component({
    selector: 'sfe-sidebar',
    imports: [
        IconComponent,
        TagChipComponent,
        QuickLinksComponent,
        ConnectionLinksComponent,
        ContextMenuDirective,
        RouterLink,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'sidebar',
        id: 'sidebar',
        '[attr.aria-label]': 'label',
        role: 'complementary',
        '(document:keydown.escape)': 'closeDrawer()',
        // A press, not a click: iOS sends no click for a tap on what isn't clickable (the page's text, an empty area).
        '(document:pointerdown)': 'closeOnOutsideClick($event)',
    },
    templateUrl: './sidebar.component.html',
})
export class SidebarComponent implements OnInit {
    protected readonly sidebar = inject(SidebarService);
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    protected readonly config = inject(ConfigService);
    protected readonly tagMenu = inject(TagMenu);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    protected readonly sections = SECTIONS;
    protected readonly label = $localize`Quick links and tags`;
    /** The rights of Administration › File management › Trash. */
    protected readonly trashRights = [Rights.deleteFiles, Rights.deletePermanently];
    private readonly folded = signal(readFolded());

    constructor() {
        // Something came into or left the trash (told by the change feed): its count is read again.
        const feed = inject(ChangeFeed);
        let seen = feed.trashChanges();
        effect(() => {
            const changes = feed.trashChanges();
            if (changes !== seen && this.config.canAny(this.trashRights)) this.sidebar.refreshTrash();
            seen = changes;
        });
    }

    ngOnInit(): void {
        void this.sidebar.reload();
        if (this.config.canAny(this.trashRights)) void this.sidebar.reloadTrash();
    }

    protected trashTitle(trash: TrashCount | null): string {
        if (!trash) return $localize`Open the trash.`;
        if (!trash.count) return $localize`Open the trash. It's empty.`;
        const count = trash.count;
        return trash.more
            ? icu($localize`Open the trash. It holds more than {count, plural, one {# item} other {# items}}.`, {
                  count,
              })
            : icu($localize`Open the trash. It holds {count, plural, one {# item} other {# items}}.`, { count });
    }

    protected isFolded(section: Section): boolean {
        return this.folded()[section.key] ?? section.folded ?? false;
    }

    protected toggleFolded(section: Section): void {
        this.folded.update((folded) => ({ ...folded, [section.key]: !this.isFolded(section) }));
        writePreference(FOLDED_PREFERENCE, JSON.stringify(this.folded()));
    }

    protected foldTitle(section: Section, open: boolean): string {
        return open
            ? $localize`Fold the list “${section.title}:list:”.`
            : $localize`Show the list “${section.title}:list:”.`;
    }

    protected countTitle(usage: TagUsage): string {
        const count = usage.fileCount;
        return icu($localize`{count, plural, one {# file has this tag} other {# files have this tag}}.`, { count });
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
        if (this.sidebar.narrow && this.sidebar.open()) this.sidebar.setOpen(false, { remember: false });
    }

    /** Tapping the page next to the drawer closes it. */
    protected closeOnOutsideClick(event: PointerEvent): void {
        const target = event.target as Element;
        if (
            this.sidebar.narrow &&
            this.sidebar.open() &&
            !this.host.contains(target) &&
            !target.closest?.('.sidebar-toggle')
        )
            this.sidebar.setOpen(false, { remember: false });
    }
}

function readFolded(): Partial<Record<Section['key'], boolean>> {
    try {
        const value: unknown = JSON.parse(readPreference(FOLDED_PREFERENCE, '{}'));
        return value && typeof value === 'object' ? (value as Partial<Record<Section['key'], boolean>>) : {};
    } catch {
        return {};
    }
}
