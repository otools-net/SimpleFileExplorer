import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { kindIcon } from '../../core/format';
import { parentPath } from '../../core/paths';
import { HomeFolderService } from '../../library/home-folder/home-folder.service';
import { QuickLink } from '../../library/library.models';
import { ListingStore } from '../../library/listing.store';
import { QuickLinksService } from '../../library/quick-links/quick-links.service';
import { IconComponent } from '../../shared/icon.component';

/**
 * The dashboard's Home and quick links: the account's home folder, then its quick links, as in the sidebar. A folder
 * opens its listing, a file the folder holding it; those not there any more are faded.
 */
@Component({
    selector: 'sfe-places-widget',
    imports: [RouterLink, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'places-widget' },
    template: `
        <ul class="quick-link-list">
            @if (home(); as home) {
                <li class="quick-link" [class.missing]="!home.exists" data-home-folder>
                    <a class="quick-link-target" routerLink="/" [queryParams]="store.libraryParams(home.path!)"
                       [title]="homeHint()">
                        <svg sfeIcon="home"></svg><span class="quick-link-name" i18n>Home</span>
                    </a>
                </li>
            }
            @for (link of links.links(); track link.path) {
                <li class="quick-link" [class.missing]="!link.exists" [attr.data-quick-link]="link.path">
                    <a class="quick-link-target" routerLink="/" [queryParams]="store.libraryParams(folderOf(link))"
                       [title]="hint(link)">
                        <svg [sfeIcon]="icon(link)"></svg><span class="quick-link-name">{{ link.name }}</span>
                    </a>
                </li>
            }
        </ul>
        @if (links.loaded() && !links.links().length) {
            <p class="dashboard-card-note" i18n>No quick links yet. Pin a folder or file to see it here.</p>
        }
    `,
})
export class PlacesWidgetComponent implements OnInit {
    protected readonly homes = inject(HomeFolderService);
    protected readonly links = inject(QuickLinksService);
    protected readonly store = inject(ListingStore);

    protected readonly home = computed(() => {
        const home = this.homes.enabled() ? this.homes.home() : null;
        return home?.path != null ? home : null;
    });

    ngOnInit(): void {
        void this.homes.load();
        void this.links.load();
    }

    protected homeHint(): string {
        const home = this.home()!;
        const where = home.path ? `/${home.path}` : $localize`${home.name}:name: (the top of the library)`;
        return home.exists
            ? $localize`Open your home folder, ${where}:place:.`
            : $localize`Your home folder, ${where}:place:, is not there: not made yet, moved or deleted.`;
    }

    /** The folder a link opens: itself, or the one holding the file. */
    protected folderOf(link: QuickLink): string {
        return link.kind === 'directory' ? link.path : parentPath(link.path);
    }

    protected icon(link: QuickLink): string {
        return kindIcon[link.kind];
    }

    protected hint(link: QuickLink): string {
        const where = link.path ? `/${link.path}` : $localize`${link.name}:name: (the top of the library)`;
        if (!link.exists) return $localize`${where}:place:: not there any more (moved, renamed or deleted).`;
        return link.kind === 'directory' ? where : $localize`Open the folder holding ${where}:place:.`;
    }
}
