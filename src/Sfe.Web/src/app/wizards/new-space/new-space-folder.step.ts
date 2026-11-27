import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { FolderPickerComponent, FolderSource } from '../../shared/folder-picker.component';
import { injectWizard } from '../wizard-context';
import { folderName, NewSpaceDraft } from './new-space-draft';

/** The new space's library folder, picked among the folders the server allows (Sfe:Tenants:AllowedRoots). */
@Component({
    selector: 'sfe-new-space-folder',
    imports: [FolderPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="field">
            <sfe-folder-picker
                [source]="source"
                [initial]="wizard.draft().root || null"
                placeholder="Pick a folder below"
                i18n-placeholder
                (picked)="pick($event)"
                (failed)="failed.set($event)"
            />
            <span class="field-hint" i18n>Click a folder to choose it; click again inside it to go deeper. Only the
                folders the server allows are listed, and the folder can't change once the space exists.</span>
            @if (failed(); as reason) {
                <span class="field-error" i18n>Could not list the folders: {{ reason }}</span>
            }
            @if (wizard.errors()['root']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </div>
    `,
})
export class NewSpaceFolderStep {
    private readonly api = inject(Api);
    protected readonly wizard = injectWizard<NewSpaceDraft>();
    protected readonly failed = signal<string | null>(null);
    protected readonly source: FolderSource = (path) => this.api.tenantFolders(path);

    /** The folder opened is the one chosen; its name names the space until one is typed. */
    protected pick(root: string): void {
        const draft = this.wizard.draft();
        const named = draft.name && draft.name !== folderName(draft.root);
        this.wizard.change({ root, name: named ? draft.name : folderName(root) });
    }
}
