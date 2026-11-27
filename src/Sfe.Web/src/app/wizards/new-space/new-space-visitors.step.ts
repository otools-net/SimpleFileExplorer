import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { checkedOf } from '../../shared/events';
import { injectWizard } from '../wizard-context';
import { NewSpaceDraft } from './new-space-draft';

/** Whether people without an account may look at the new space (with its Guests group's rights). */
@Component({
    selector: 'sfe-new-space-visitors',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <label class="check">
            <input
                type="checkbox"
                [checked]="wizard.draft().guestAccess"
                (change)="wizard.change({ guestAccess: checkedOf($event) })"
            >
            <span><ng-container i18n>Let visitors in without an account</ng-container>
                <span class="field-hint" i18n>They get the rights of the space's Guests group. You can change this
                    later on the space's page.</span></span>
        </label>
        @if (local()) {
            <p class="field-hint" i18n>This server is in Local mode: nobody comes in without an account until it is
                in External mode.</p>
        }
        @if (wizard.errors()['guestAccess']; as error) {
            <span class="field-error">{{ error }}</span>
        }
    `,
})
export class NewSpaceVisitorsStep {
    protected readonly wizard = injectWizard<NewSpaceDraft>();
    private readonly config = inject(ConfigService);
    protected readonly local = computed(() => this.config.config()?.mode === 'local');
    protected readonly checkedOf = checkedOf;
}
