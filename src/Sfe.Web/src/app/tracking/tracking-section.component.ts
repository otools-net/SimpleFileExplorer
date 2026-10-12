import { afterNextRender, ChangeDetectionStrategy, Component, computed, inject, input, viewChild } from '@angular/core';
import { ListingStore } from '../library/listing.store';
import { DuplicatesPanelComponent } from './duplicates-panel.component';
import { MissingPanelComponent } from './missing-panel.component';
import { SimilarPanelComponent } from './similar-panel.component';

export type TrackingTab = 'duplicates' | 'similar' | 'missing';

/**
 * "Duplicates & moves" on the administration page: groups of identical indexed files, of similar videos, and the
 * tagged files scans no longer find (waiting to be relinked to where they went), one per tab. The three load
 * together, so that each tab shows its count (`badges`) before it is opened.
 */
@Component({
    selector: 'sfe-tracking-section',
    imports: [DuplicatesPanelComponent, SimilarPanelComponent, MissingPanelComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-duplicates-panel id="tracking-panel-duplicates" [hidden]="tab() !== 'duplicates'" [folder]="folder" (openFolder)="openFolder($event)" />
        <sfe-similar-panel id="tracking-panel-similar" [hidden]="tab() !== 'similar'" [folder]="folder" [isOpen]="true" (openFolder)="openFolder($event)" />
        <sfe-missing-panel id="tracking-panel-missing" [hidden]="tab() !== 'missing'" (openFolder)="openFolder($event)" />
    `,
})
export class TrackingSectionComponent {
    readonly tab = input.required<string>();

    private readonly store = inject(ListingStore);
    private readonly duplicates = viewChild.required(DuplicatesPanelComponent);
    private readonly similar = viewChild.required(SimilarPanelComponent);
    private readonly missing = viewChild.required(MissingPanelComponent);

    /** The folder browsed last, for "only in this folder". */
    protected readonly folder = this.store.route().path;

    /** The count shown on each tab. */
    readonly badges = computed((): Record<TrackingTab, string> => ({
        duplicates: this.duplicates().badge(),
        similar: this.similar().badge(),
        missing: this.missing().badge(),
    }));

    constructor() {
        // Once the panels have the folder.
        afterNextRender(() => {
            void this.duplicates().load();
            void this.similar().load();
            void this.missing().load();
        });
    }

    /** Browses a folder of the library (the listing and the tag sidebar load again there). */
    protected openFolder(path: string): void {
        this.store.navigate({ path, q: '' });
    }
}
