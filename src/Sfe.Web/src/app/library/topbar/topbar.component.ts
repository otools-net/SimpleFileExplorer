import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/icon.component';
import { ListingStore, View, VIEWS } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { MainMenuComponent } from './main-menu.component';
import { Popovers } from './popovers.service';
import { SearchBoxComponent } from './search-box.component';

const VIEW_BUTTONS: { view: View; icon: string; label: string; title: string }[] = [
    { view: 'grid', icon: 'grid', label: 'Grid', title: 'Grid' },
    { view: 'list', icon: 'list', label: 'List', title: 'List' },
    { view: 'single', icon: 'rows', label: 'One per row', title: 'One per row (large previews)' },
];

/** The bar at the top: sidebar toggle, library root, search, layout switch and the ⋮ menu. */
@Component({
    selector: 'header[sfeTopbar]',
    imports: [IconComponent, RouterLink, SearchBoxComponent, MainMenuComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'topbar', '(document:click)': 'closePopovers($event)' },
    template: `
        <button type="button" class="icon-button ghost sidebar-toggle" aria-controls="sidebar" aria-label="Tags sidebar"
                [attr.aria-expanded]="sidebar.open()" [title]="sidebar.open() ? 'Hide tags' : 'Show tags'" (click)="sidebar.toggle()">
            <svg sfeIcon="sidebar"></svg>
        </button>
        <a class="brand" routerLink="/" [queryParams]="store.browseParams('')" title="Library root">
            <svg sfeIcon="folder"></svg>
            <span>SFE</span>
        </a>

        <form sfeSearchBox></form>

        <div class="actions">
            <div class="segmented" role="group" aria-label="Layout">
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
    readonly searchBox = viewChild.required(SearchBoxComponent);

    protected switchView(view: View): void {
        // On narrow screens only the active button is shown: pressing it moves on to the next layout.
        if (view === this.store.view())
            view = VIEWS[(VIEWS.indexOf(view) + 1) % VIEWS.length];
        this.store.setView(view);
    }

    /** A click outside the popovers and the buttons opening them closes them. */
    protected closePopovers(event: MouseEvent): void {
        if (!(event.target as Element).closest?.('.popover, [aria-expanded]'))
            this.popovers.close();
    }
}
