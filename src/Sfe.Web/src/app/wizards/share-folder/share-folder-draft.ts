import { FolderLevel, FolderRuleChange } from '../../core/access.models';
import { FolderAccessReport } from '../../access/access.models';
import { FOLDER_LEVELS } from '../../access/folder-levels';
import type { WizardReviewLine } from '../wizard-definition';

/** Who a rule is for: `g12` (a group) or `u5` (an account). */
export type Who = `g${number}` | `u${number}`;

/** What the share-a-folder wizard asks for (plain values: a draft is compared and may be kept as JSON). */
export interface ShareFolderDraft {
    /** The folder, relative to the top of the library; null until one is picked. */
    path: string | null;
    who: Who | null;
    /** The group's or the account's name, for the review and the end. */
    whoName: string;
    level: FolderLevel | null;
}

/** What `PUT access/folder-rules` takes for the draft (the parts not chosen yet left empty). */
export function ruleChange(draft: ShareFolderDraft): FolderRuleChange {
    const id = draft.who ? Number(draft.who.slice(1)) : null;
    return {
        path: draft.path ?? '',
        groupId: draft.who?.startsWith('g') ? id : null,
        userId: draft.who?.startsWith('u') ? id : null,
        level: draft.level,
    };
}

/** How the pages name a level. */
export function levelLabel(level: FolderLevel | null): string {
    return FOLDER_LEVELS.find((choice) => choice.level === level)?.label ?? '';
}

/** What someone may do in the folder, all their groups and rules counted. */
function accessText(entry: { sees: boolean; changesFiles: boolean }): string {
    if (!entry.sees) return $localize`Doesn't see it`;
    return entry.changesFiles
        ? $localize`Sees it and may change its files`
        : $localize`Sees it, may not change its files`;
}

/** Who has access to the folder: each account, and visitors when the space lets them in. */
export function accessLines(report: FolderAccessReport): WizardReviewLine[] {
    const lines = report.accounts.map((account) => ({ label: account.name, value: accessText(account) }));
    if (report.visitors)
        lines.push({ label: $localize`Visitors without an account`, value: accessText(report.visitors) });
    return lines;
}
