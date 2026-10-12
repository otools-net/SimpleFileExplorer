import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from './icon.component';

/** A big icon, a title and a line of text where there is nothing to list; an action can be projected below. */
@Component({
    selector: 'sfe-empty-state',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'empty' },
    template: `
        <svg [sfeIcon]="icon()"></svg>
        <h2>{{ heading() }}</h2>
        @if (text()) {
            <p>{{ text() }}</p>
        }
        <ng-content />
    `,
})
export class EmptyStateComponent {
    readonly icon = input.required<string>();
    readonly heading = input.required<string>();
    readonly text = input<string | null>(null);
}
