import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/icon.component';
import { ListingStore, View, VIEWS } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { MainMenuComponent } from './main-menu.component';
import { Popovers } from './popovers.service';
import { SearchBoxComponent } from './search-box.component';

const VIEW_BUTTONS: { view: View; icon: string; label: string; title: string }[] = [
    { view: 'grid', icon: 'grid', label: $localize`Grid`, title: $localize`Show the items as a grid of tiles.` },
    { view: 'list', icon: 'list', label: $localize`List`, title: $localize`Show the items as a list, one per line.` },
    {
        view: 'single',
        icon: 'rows',
        label: $localize`One per row`,
        title: $localize`Show one item per row, with large previews.`,
    },
];

/** The bar at the top: sidebar toggle, library root, search, layout switch and the ⋮ menu. */
@Component({
    selector: 'header[sfeTopbar]',
    imports: [IconComponent, RouterLink, SearchBoxComponent, MainMenuComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    // A press, not a click: iOS sends no click for a tap on what isn't clickable (the page's text, an empty area).
    host: { class: 'topbar', '(document:pointerdown)': 'closePopovers($event)' },
    template: `
        <button
            type="button"
            class="icon-button ghost sidebar-toggle"
            aria-controls="sidebar"
            aria-label="Sidebar"
            i18n-aria-label="the panel of quick links and tags"
            [attr.aria-expanded]="sidebar.open()"
            [title]="sidebar.open() ? hideSidebar : showSidebar"
            (click)="sidebar.toggle()"
        >
            <svg sfeIcon="sidebar"></svg>
        </button>
        <a class="brand" routerLink="/" [queryParams]="store.libraryParams('')" title="Go to the top of the library."
           i18n-title>
            <svg sfeIcon="folder"></svg>
            <span translate="no">SFE</span>
        </a>

        <form sfeSearchBox></form>

        <div class="actions">
            <div class="segmented" role="group" aria-label="Layout" i18n-aria-label>
                @for (button of views; track button.view) {
                    <button type="button" class="icon-button" [attr.aria-pressed]="store.view() === button.view"
                            [attr.aria-label]="button.label" [title]="button.title" (click)="switchView(button.view)">
                        <svg [sfeIcon]="button.icon"></svg>
                    </button>
                }
            </div>
            <sfe-main-menu />
        </div>
    `,
})
export class TopbarComponent {
    protected readonly store = inject(ListingStore);
    protected readonly sidebar = inject(SidebarService);
    private readonly popovers = inject(Popovers);
    protected readonly views = VIEW_BUTTONS;
    protected readonly hideSidebar = $localize`Hide the sidebar: quick links and tags.`;
    protected readonly showSidebar = $localize`Show the sidebar: quick links and tags.`;
    readonly searchBox = viewChild.required(SearchBoxComponent);

    protected switchView(view: View): void {
        // On narrow screens only the active button is shown: pressing it moves on to the next layout.
        if (view === this.store.view()) view = VIEWS[(VIEWS.indexOf(view) + 1) % VIEWS.length];
        this.store.setView(view);
    }

    /** A click outside the popovers and the buttons opening them closes them. */
    protected closePopovers(event: PointerEvent): void {
        if (!(event.target as Element).closest?.('.popover, [aria-expanded]')) this.popovers.close();
    }
}
