import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ListingStore } from '../listing.store';
import { FolderNavComponent } from './folder-nav.component';
import { LocationLinks } from './location-links.service';

/**
 * A slim bar under the top bar with the folder buttons and the folder's name, shown once the location header has
 * scrolled away. It lies over the listing (the host is a sticky element without height), so showing it moves
 * nothing; clicking the name goes back to the top.
 */
@Component({
    selector: 'sfe-location-strip',
    imports: [FolderNavComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'location-strip' },
    template: `
        <div class="location-strip-bar" [class.shown]="links.headHidden()" [attr.inert]="links.headHidden() ? null : ''">
            @if (links.folderNav(); as nav) {
                <nav [sfeFolderNav]="nav"></nav>
            }
            <button type="button" class="location-strip-title" [title]="'Back to the top: ' + fullName()" (click)="toTop()">
                @if (parent(); as parent) {
                    <span class="parent">{{ parent }}</span>
                    <span class="sep" aria-hidden="true">/</span>
                }
                <span class="current">{{ current() }}</span>
                @if (store.route().q) {
                    <span class="search-label">Search “{{ store.route().q }}”</span>
                }
            </button>
        </div>
    `,
})
export class LocationStripComponent {
    protected readonly store = inject(ListingStore);
    protected readonly links = inject(LocationLinks);

    protected readonly current = computed(() => this.links.crumbs().at(-1)!.name);
    /** The folder above the current one, so that a name used in many places still says which. */
    protected readonly parent = computed(() => this.links.crumbs().at(-2)?.name ?? null);
    protected readonly fullName = computed(() => this.links.crumbs().map(crumb => crumb.name).join(' / '));

    protected toTop(): void {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
}
