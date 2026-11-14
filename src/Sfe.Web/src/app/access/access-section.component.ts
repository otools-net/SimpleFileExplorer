import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AccountsPageComponent } from './accounts-page.component';
import { SpacesPageComponent } from './spaces-page.component';
import { AllTokensComponent } from '../tokens/all-tokens.component';

/** Administration › Spaces & accounts: the spaces (each its library, groups, accounts, visitors), accounts, tokens. */
@Component({
    selector: 'sfe-access-section',
    imports: [SpacesPageComponent, AccountsPageComponent, AllTokensComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @switch (tab()) {
            @case ('accounts') {
                <sfe-accounts-page />
            }
            @case ('tokens') {
                <sfe-all-tokens />
            }
            @default {
                <sfe-spaces-page />
            }
        }
    `,
})
export class AccessSectionComponent {
    /** `spaces`, `accounts` or `tokens`. */
    readonly tab = input.required<string>();
}
