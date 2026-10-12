import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, Injectable, signal } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { readPreference, writePreference } from '../../core/preferences';
import { IconComponent } from '../../shared/icon.component';
import { ListingStore } from '../listing.store';
import { explain } from './search-explain';

const PREFERENCE = 'sfe.searchExplain';

/**
 * What the search box holds, as typed (the route only changes once the search runs), and whether its explanation
 * is open (remembered per browser).
 */
@Injectable({ providedIn: 'root' })
export class SearchBoxState {
    private readonly config = inject(ConfigService);

    readonly text = signal('');
    readonly explainOpen = signal(readPreference(PREFERENCE) === 'open');
    /** The explain button shows while the box has a query and the feature is on (`config.searchExplain`). */
    readonly canExplain = computed(() => Boolean(this.config.config()?.searchExplain && this.text().trim()));

    setExplainOpen(open: boolean): void {
        this.explainOpen.set(open);
        writePreference(PREFERENCE, open ? 'open' : 'closed');
    }
}

/** The "explain" panel: the query in the search box put into words, updated as it is typed. */
@Component({
    selector: 'sfe-search-explain',
    imports: [IconComponent, NgTemplateOutlet],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'search-explain', role: 'note', 'aria-label': 'This search in words' },
    template: `
        <ng-template #segments let-list>@for (segment of list; track $index) {@switch (segment.kind) {
            @case ('text') {<ng-container>{{ segment.text }}</ng-container>}
            @case ('strong') {<strong>{{ segment.text }}</strong>}
            @case ('term') {<span class="q-term" [class]="'q-' + segment.type" [class.q-excluded]="segment.excluded" [attr.title]="segment.title">{{ segment.value }}</span>}
            @case ('op') {<span class="q-op" [class.q-not]="segment.negation">{{ segment.word }}</span>}
        }}</ng-template>

        <button type="button" class="icon-button ghost small search-explain-close" aria-label="Hide the explanation" title="Hide the explanation" (click)="search.setExplainOpen(false)">
            <svg sfeIcon="close"></svg>
        </button>
        @let words = explanation();
        <div class="search-explain-sentence"><ng-container *ngTemplateOutlet="segments; context: { $implicit: words.sentence }" /></div>
        @if (words.alternatives.length) {
            <ol class="search-explain-alternatives">
                @for (alternative of words.alternatives; track $index) {
                    <li><ng-container *ngTemplateOutlet="segments; context: { $implicit: alternative }" /></li>
                }
            </ol>
        }
        @if (words.notes.length) {
            <ul class="search-explain-notes">
                @for (note of words.notes; track $index) {
                    <li><ng-container *ngTemplateOutlet="segments; context: { $implicit: note }" /></li>
                }
            </ul>
        }
    `,
})
export class SearchExplainComponent {
    protected readonly search = inject(SearchBoxState);
    private readonly store = inject(ListingStore);

    protected readonly explanation = computed(() => explain(this.search.text(), this.store.route().path));
}
