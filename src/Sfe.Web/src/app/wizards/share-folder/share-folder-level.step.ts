import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FolderAccessEntry } from '../../core/access.models';
import { Api } from '../../core/api.service';
import { FOLDER_LEVELS } from '../../access/folder-levels';
import { injectWizard } from '../wizard-context';
import { levelLabel, ShareFolderDraft } from './share-folder-draft';

/** What the group or the account may do in the folder: not see it, only read it, or change its files too. */
@Component({
    selector: 'sfe-share-folder-level',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (now(); as now) {
            <p class="field-hint share-folder-now">{{ now }}</p>
        }
        <fieldset class="access-choices">
            <legend class="field-label" i18n>Level</legend>
            @for (choice of levels; track choice.level) {
                <label class="check">
                    <input
                        type="radio"
                        name="share-folder-level"
                        [value]="choice.level"
                        [checked]="wizard.draft().level === choice.level"
                        (change)="wizard.change({ level: choice.level })"
                    >
                    <span>{{ choice.label }} <span class="field-hint">{{ choice.hint }}</span></span>
                </label>
            }
        </fieldset>
        @if (account() && wizard.draft().level === 'hidden') {
            <p class="field-hint" i18n>Hidden keeps the folder from this account only if none of its groups sees
                it.</p>
        }
        @if (wizard.errors()['level']; as error) {
            <span class="field-error">{{ error }}</span>
        }
    `,
})
export class ShareFolderLevelStep implements OnInit {
    private readonly api = inject(Api);
    protected readonly wizard = injectWizard<ShareFolderDraft>();
    protected readonly levels = FOLDER_LEVELS;
    protected readonly account = computed(() => this.wizard.draft().who?.startsWith('u') ?? false);
    /** The group's or the account's entry in who has access to the folder today. */
    private readonly today = signal<FolderAccessEntry | null>(null);

    /** What the group or the account may do there today, in a sentence. */
    protected readonly now = computed(() => {
        const entry = this.today();
        if (!entry) return null;
        const name = this.wizard.draft().whoName;
        if (entry.level && entry.explicit)
            return $localize`Today “${name}:name:” has ${levelLabel(entry.level)}:level: from a rule on this folder.`;
        if (entry.level) return $localize`Today “${name}:name:” has ${levelLabel(entry.level)}:level: here.`;
        return entry.sees
            ? $localize`Today “${name}:name:” has no rule of its own here; its groups let it see the folder.`
            : $localize`Today “${name}:name:” has no rule of its own here; its groups don't let it see the folder.`;
    });

    async ngOnInit(): Promise<void> {
        const { path, who } = this.wizard.draft();
        if (path === null || !who) return;
        const id = Number(who.slice(1));
        try {
            const report = await this.api.folderAccess(path);
            const entries = who.startsWith('g') ? report.groups : report.accounts;
            this.today.set(entries.find((entry) => (entry.groupId ?? entry.userId) === id) ?? null);
        } catch {
            // Only a hint: the step works without it.
        }
    }
}
