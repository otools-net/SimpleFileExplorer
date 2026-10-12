import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AccountsPageComponent } from './accounts-page.component';
import { SpacesPageComponent } from './spaces-page.component';

/** Administration › Spaces & accounts: the spaces (each its library, groups, accounts, visitors) and the accounts. */
@Component({
    selector: 'sfe-access-section',
    imports: [SpacesPageComponent, AccountsPageComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @switch (tab()) {
            @case ('accounts') {
                <sfe-accounts-page />
            }
            @default {
                <sfe-spaces-page />
            }
        }
    `,
})
export class AccessSectionComponent {
    /** `spaces` or `accounts`. */
    readonly tab = input.required<string>();
}
