import { quoted } from './file-dialog-texts';

/*
 * The texts of undoing a new folder, a new file or a copy (`MadeUndo`): what was done, what Undo does, why it was
 * refused. agents/docs/development/ui-texts.md.
 */

/** What was done, for the ⋮ menu's Undo: "Folder “Plans” created." */
export function createdText(kind: 'folder' | 'file', name: string): string {
    return kind === 'folder'
        ? $localize`Folder ${quoted(name)}:name: created.`
        : $localize`File ${quoted(name)}:name: created.`;
}

/** The hint of undoing a new folder or file. */
export function createdHint(kind: 'folder' | 'file', name: string): string {
    return kind === 'folder'
        ? $localize`Move the folder ${quoted(name)}:name: to the trash, unless something changed in it since.`
        : $localize`Move the file ${quoted(name)}:name: to the trash, unless it changed since.`;
}

/** The hint of undoing a copy. */
export function copiedHint(): string {
    return $localize`Move the copies to the trash, unless they changed since.`;
}

/** An item made by the action undone, left where it is: it changed since. */
export function changedSince(name: string): string {
    return $localize`Could not move ${quoted(name)}:name: to the trash: it changed since it was made.`;
}

/** An item made by the action undone that could not go to the trash, with the reason. */
export function notTrashed(name: string, reason: string): string {
    return $localize`Could not move ${quoted(name)}:name: to the trash: ${reason}:reason:`;
}
