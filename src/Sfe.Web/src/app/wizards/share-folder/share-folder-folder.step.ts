import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { FolderPickerComponent, FolderSource } from '../../shared/folder-picker.component';
import { injectWizard } from '../wizard-context';
import { ShareFolderDraft } from './share-folder-draft';

/** The folder to share, picked among every folder of the library (whatever rules apply to the one sharing it). */
@Component({
    selector: 'sfe-share-folder-folder',
    imports: [FolderPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="field">
            <sfe-folder-picker
                [source]="source"
                [initial]="wizard.draft().path"
                placeholder="Pick a folder below"
                i18n-placeholder
                (picked)="wizard.change({ path: $event })"
                (failed)="failed.set($event)"
            />
            <span class="field-hint" i18n>Click a folder to choose it; click again inside it to go deeper. What you
                choose applies to everything below it too.</span>
            @if (failed(); as reason) {
                <span class="field-error" i18n>Could not list the folders: {{ reason }}</span>
            }
            @if (wizard.errors()['path']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </div>
    `,
})
export class ShareFolderFolderStep {
    private readonly api = inject(Api);
    protected readonly wizard = injectWizard<ShareFolderDraft>();
    protected readonly failed = signal<string | null>(null);
    protected readonly source: FolderSource = (path) => this.api.spaceFolders(path);
}
