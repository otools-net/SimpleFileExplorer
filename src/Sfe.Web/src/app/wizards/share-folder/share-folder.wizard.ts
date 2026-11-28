import { inject } from '@angular/core';
import { Api } from '../../core/api.service';
import { defineWizard, WizardEnd } from '../wizard-definition';
import { accessLines, levelLabel, ruleChange, ShareFolderDraft } from './share-folder-draft';
import { ShareFolderFolderStep } from './share-folder-folder.step';
import { ShareFolderLevelStep } from './share-folder-level.step';
import { ShareFolderWhoStep } from './share-folder-who.step';

/** The rule's checks on the server, nothing set (`validateOnly`): a step's Next. */
async function checkRule(draft: ShareFolderDraft): Promise<void> {
    await inject(Api).setFolderRule(ruleChange(draft), null, true);
}

/**
 * A folder shared with a group or an account: one folder rule (Hidden, Read only, Read & write), set by one request
 * at the end (`PUT access/folder-rules`); the end shows who has access to the folder now.
 */
export const SHARE_FOLDER_WIZARD = defineWizard<ShareFolderDraft>({
    id: 'share-folder',
    title: $localize`:wizard:Share a folder`,
    helpPage: 'administration/wizards.md',
    initialDraft: () => ({ path: null, who: null, whoName: '', level: null }),
    steps: [
        {
            id: 'folder',
            title: $localize`:wizard step:Folder`,
            component: ShareFolderFolderStep,
            explanation: $localize`The folder to share, with everything below it. A folder further down can have a \
rule of its own.`,
            helpAnchor: 'share-a-folder-folder',
            fields: ['path'],
            valid: (draft) => draft.path !== null,
            check: checkRule,
            review: (draft) => [{ label: $localize`Folder`, value: draft.path ?? '' }],
        },
        {
            id: 'who',
            title: $localize`:wizard step:Who`,
            component: ShareFolderWhoStep,
            explanation: $localize`A group decides for everyone in it. Someone sees a folder when one of their \
groups does, or their own account's rule lets them.`,
            helpAnchor: 'share-a-folder-who',
            fields: ['groupId', 'userId'],
            valid: (draft) => draft.who !== null,
            check: checkRule,
            review: (draft) => [
                {
                    label: draft.who?.startsWith('g') ? $localize`Group` : $localize`Account`,
                    value: draft.whoName,
                },
            ],
        },
        {
            id: 'level',
            title: $localize`:wizard step:Level`,
            component: ShareFolderLevelStep,
            explanation: $localize`Hidden: they don't see the folder. Read only: they see and open its files. Read \
& write: their rights apply in full, those that change files too.`,
            helpAnchor: 'share-a-folder-level',
            fields: ['level'],
            valid: (draft) => draft.level !== null,
            review: (draft) => [{ label: $localize`Level`, value: levelLabel(draft.level) }],
        },
    ],
    async finish(draft) {
        const api = inject(Api);
        await api.setFolderRule(ruleChange(draft));
        const end: WizardEnd = {
            message: $localize`“${draft.whoName}:name:” now has ${levelLabel(draft.level)}:level: on \
“${draft.path}:folder:” and everything below it.`,
            links: [
                {
                    label: $localize`Open the Folder access page`,
                    title: $localize`Every folder rule of the space, and who has access to each folder`,
                    route: 'admin',
                    query: { section: 'access', page: 'folders' },
                },
            ],
        };
        try {
            const report = await api.folderAccess(draft.path ?? '');
            return { ...end, details: { title: $localize`Who has access now`, lines: accessLines(report) } };
        } catch {
            // The rule is set: the end shows it without the list.
            return end;
        }
    },
});
