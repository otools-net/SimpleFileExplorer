import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { SpacePartComponent } from './space-part.component';
import { SpacesPageComponent } from './spaces-page.component';
import { AllTokensComponent } from '../tokens/all-tokens.component';

/**
 * Administration › Spaces & accounts: the spaces (each its library), then a space's accounts, groups, folder rules and
 * visitors (`SpacePartComponent`), and the access tokens.
 */
@Component({
    selector: 'sfe-access-section',
    imports: [SpacesPageComponent, SpacePartComponent, AllTokensComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @switch (tab()) {
            @case ('accounts') {
                <sfe-space-part part="accounts" />
            }
            @case ('groups') {
                <sfe-space-part part="groups" />
            }
            @case ('folders') {
                <sfe-space-part part="folders" />
            }
            @case ('visitors') {
                <sfe-space-part part="visitors" />
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
    /** `spaces`, `accounts`, `groups`, `folders`, `visitors` or `tokens`. */
    readonly tab = input.required<string>();
}
