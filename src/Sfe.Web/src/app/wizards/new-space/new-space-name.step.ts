import { ChangeDetectionStrategy, Component } from '@angular/core';
import { valueOf } from '../../shared/events';
import { injectWizard } from '../wizard-context';
import { NewSpaceDraft } from './new-space-draft';

/** The new space's name, and its short name (made from the name when left empty). */
@Component({
    selector: 'sfe-new-space-name',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="field">
            <label class="field-label" for="new-space-name" i18n>Name</label>
            <input
                id="new-space-name"
                type="text"
                autocomplete="off"
                placeholder="For example Family, Work, Holiday photos"
                i18n-placeholder
                [value]="wizard.draft().name"
                (input)="wizard.change({ name: valueOf($event) })"
            >
            <span class="field-hint" i18n>Shown in the ⋮ menu and on the sign-in page.</span>
            @if (wizard.errors()['name']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </div>
        <div class="field">
            <label class="field-label" for="new-space-slug"><ng-container i18n>Short name</ng-container>
                <span class="field-hint" i18n>(optional)</span></label>
            <input
                id="new-space-slug"
                type="text"
                autocomplete="off"
                spellcheck="false"
                placeholder="Made from the name"
                i18n-placeholder
                aria-describedby="new-space-slug-hint"
                [value]="wizard.draft().slug"
                (input)="wizard.change({ slug: valueOf($event) })"
            >
            <span class="field-hint" id="new-space-slug-hint" i18n>Lowercase letters, digits and “-”. It names the
                space's index and cache folders on the server, and can't change later.</span>
            @if (wizard.errors()['slug']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </div>
    `,
})
export class NewSpaceNameStep {
    protected readonly wizard = injectWizard<NewSpaceDraft>();
    protected readonly valueOf = valueOf;
}
