import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { highlightParts } from '../core/format';

/** `text` with the matches of `pattern` (the searched words) in <mark>. Never parses HTML. */
@Component({
    selector: 'sfe-highlight',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `@for (part of parts(); track $index) {@if (part.mark) {<mark>{{ part.text }}</mark>} @else {<ng-container>{{ part.text }}</ng-container>}}`,
})
export class HighlightComponent {
    readonly text = input.required<string>();
    readonly pattern = input<RegExp | null>(null);

    protected readonly parts = computed(() => highlightParts(this.text(), this.pattern()));
}
