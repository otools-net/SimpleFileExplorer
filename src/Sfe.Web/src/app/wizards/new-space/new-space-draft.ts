import { TenantInput } from '../../core/access.models';

/** What the new-space wizard asks for (plain values: a draft is compared and may be kept as JSON). */
export interface NewSpaceDraft {
    /** The library folder, a full path of the server. */
    root: string;
    name: string;
    /** Empty: made from the name. */
    slug: string;
    guestAccess: boolean;
}

/** What `POST tenants` takes for the draft. */
export function spaceInput(draft: NewSpaceDraft): TenantInput {
    return {
        name: draft.name.trim(),
        slug: draft.slug.trim() || null,
        root: draft.root || null,
        guestAccess: draft.guestAccess,
        disabled: false,
        disabledRights: [],
    };
}

/** The last name of a folder's path (`D:\Media\Family` → `Family`): the space's name until one is typed. */
export function folderName(path: string): string {
    return (
        path
            .replace(/[\\/]+$/, '')
            .split(/[\\/]/)
            .pop() ?? ''
    );
}
