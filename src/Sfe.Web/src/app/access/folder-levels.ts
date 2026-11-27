import { FolderLevel } from '../core/access.models';

/** The levels a folder rule sets, as the pages name them (the Folder access card, the share-a-folder wizard). */
export const FOLDER_LEVELS: { level: FolderLevel; label: string; hint: string }[] = [
    {
        level: 'hidden',
        label: $localize`:folder access level:Hidden`,
        hint: $localize`They don't see the folder (unless another of their groups does)`,
    },
    {
        level: 'read',
        label: $localize`:folder access level:Read only`,
        hint: $localize`They see and use its files with their rights, but may not change the files`,
    },
    {
        level: 'write',
        label: $localize`:folder access level:Read & write`,
        hint: $localize`Their rights apply in full, those that change files too`,
    },
];
