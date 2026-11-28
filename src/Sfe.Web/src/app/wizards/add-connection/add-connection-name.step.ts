import { ChangeDetectionStrategy, Component } from '@angular/core';
import { valueOf } from '../../shared/events';
import { injectWizard } from '../wizard-context';
import { AddConnectionDraft } from './add-connection-draft';

/** Its name, as people will see it, and the folder of the storage it shows as its top. */
@Component({
    selector: 'sfe-add-connection-name',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <label class="field">
            <span>
                <ng-container i18n>Name</ng-container>
                <span class="field-hint" i18n>As people will see it, in the sidebar.</span>
            </span>
            <input
                type="text"
                data-name
                [value]="wizard.draft().name"
                (input)="wizard.change({ name: value($event) })"
            >
            @if (wizard.errors()['name']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </label>
        <label class="field">
            <span>
                <ng-container i18n>Folder</ng-container>
                <span class="field-hint" i18n>Shown as the top of the connection. Leave it empty for all of it.</span>
            </span>
            <input
                type="text"
                data-root
                placeholder="/Photos"
                i18n-placeholder="an example of a folder"
                [value]="wizard.draft().rootPath"
                (input)="wizard.change({ rootPath: value($event) })"
            >
            @if (wizard.errors()['rootPath']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </label>
    `,
})
export class AddConnectionNameStep {
    protected readonly wizard = injectWizard<AddConnectionDraft>();
    protected readonly value = valueOf;
}
