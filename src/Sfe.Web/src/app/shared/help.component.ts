import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { IconComponent } from './icon.component';

let nextId = 0;

/**
 * A "?" button opening a longer explanation (the content: what something means, examples) under what it explains. The
 * explanation opens in the flow of the page rather than over it, so it never hides the field it is about. The host has
 * `display: contents`: in a wrapping flex row (`.help-row`) the explanation takes a line of its own below the row.
 * Never put it inside a `<label>`: a click on the explanation would toggle the label's checkbox.
 */
@Component({
    selector: 'sfe-help',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <button type="button" class="help-button" [class.open]="open()" [attr.aria-expanded]="open()" [attr.aria-controls]="id"
                [attr.aria-label]="'About ' + about()" [title]="open() ? 'Hide the explanation' : 'What is this? ' + about()" (click)="open.set(!open())">
            <svg sfeIcon="help"></svg>
        </button>
        @if (open()) {
            <div class="help-panel" role="note" [id]="id">
                <ng-content />
                <button type="button" class="link-button help-close" title="Hide this explanation" (click)="open.set(false)">Got it</button>
            </div>
        }
    `,
})
export class HelpComponent {
    /** What it explains, for the button's title: "Groups", "Guest accounts"... */
    readonly about = input.required<string>();

    protected readonly open = signal(false);
    protected readonly id = `help-${++nextId}`;
}
