import type { SaveImageResult, SavedImage } from '../core/config.models';

/**
 * Saving an edited picture over its original: its version, and whether this account may (`api/editor/images/access`).
 */
export interface EditedImageAccess {
    version: string;
    /** files.edit at its folder and the rule of Replace, and a JPEG, PNG or WebP file. */
    overwrite: boolean;
    /** The space deletes for good: no previous version is kept in the trash. */
    permanent: boolean;
}

export function isSavedImage(result: SaveImageResult): result is SavedImage {
    return 'width' in result;
}
