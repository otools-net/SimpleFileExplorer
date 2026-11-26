import { ItemAccess, ItemAction, ReadOnlyReason } from '../core/models';

/**
 * The sentences of the properties sheet's Access section (agents/docs/features/item-access.md): what the session may
 * do with an item, why not, the folder a rule is set on, what the disk allows.
 */

/** One line of the section: an action, allowed or not, and why not. */
export interface AccessLine {
    action: ItemAction;
    label: string;
    allowed: boolean;
    reason: string | null;
}

function actionLabel(action: ItemAction, folder: boolean): string {
    switch (action) {
        case 'open':
            return folder ? $localize`:an item's access:Open the folder` : $localize`:an item's access:Open the file`;
        case 'download':
            return folder ? $localize`:an item's access:Download as a zip` : $localize`:an item's access:Download`;
        case 'saveOver':
            return $localize`:an item's access:Save changes over it`;
        case 'createInside':
            return $localize`:an item's access:Add files and folders to it`;
        case 'rename':
            return $localize`:an item's access:Rename`;
        case 'move':
            return $localize`:an item's access:Move`;
        case 'delete':
            return $localize`:an item's access:Move to the trash`;
    }
}

/** Why an action is refused, after its label ("Rename: a folder rule doesn't allow it here."). */
export function refusalText(reason: ReadOnlyReason): string {
    switch (reason) {
        case 'server':
            return $localize`:why an action is refused:Switched off on this server.`;
        case 'space':
            return $localize`:why an action is refused:Switched off for this space.`;
        case 'visitor':
            return $localize`:why an action is refused:Not for visitors and guest accounts.`;
        case 'rule':
            return $localize`:why an action is refused:A folder rule doesn't allow it here.`;
        case 'link':
            return $localize`:why an action is refused:Behind a link leaving the library, kept read-only.`;
        case 'diskReadOnly':
            return $localize`:why an action is refused:The disk is mounted read-only.`;
        case 'diskRefused':
            return $localize`:why an action is refused:The server isn't allowed to write there on the disk.`;
        case 'fileReadOnly':
            return $localize`:why an action is refused:The disk marks the file read-only.`;
        default:
            return $localize`:why an action is refused:Your rights in this space don't include it.`;
    }
}

/** The section's lines, in the server's order. */
export function accessLines(access: ItemAccess, folder: boolean): AccessLine[] {
    return access.actions.map(({ action, allowed, reason }) => ({
        action,
        label: actionLabel(action, folder),
        allowed,
        reason: allowed ? null : refusalText(reason ?? 'rights'),
    }));
}

/** What the disk allows where the item is. */
export function diskText(access: ItemAccess, folder: boolean): string {
    if (!access.disk)
        return folder
            ? $localize`:the disk's side:Takes changes in this folder.`
            : $localize`:the disk's side:Takes changes in the file's folder.`;
    return refusalText(access.disk);
}

/** The folder a rule is set on, from `root` (the library's name): "Set on Media / Private". */
export function ruleFolderText(folder: string, root: string): string {
    if (!folder) return $localize`:where a folder rule is set:Set on the top of the library.`;
    const where = `${root} / ${folder.split('/').join(' / ')}`;
    return $localize`:where a folder rule is set:Set on ${where}:folder:.`;
}
