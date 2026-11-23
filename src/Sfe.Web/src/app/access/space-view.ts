import { TenantDetails } from './access.models';

/**
 * The space a page shows: from `api/tenants` for server administrators, from `api/access/tenant` for the space's own.
 */
export interface SpaceView {
    id: number;
    name: string;
    /** Full path of its library folder; null when the session may not see it (a space's administrator). */
    root: string | null;
    guestAccess: boolean;
    isDefault: boolean;
    disabled: boolean;
    /** The session's own space (the requests about it need no `tenant`). */
    current: boolean;
}

/** A space of `api/tenants` as a page shows it; `current` is the id of the session's space. */
export function spaceView(space: TenantDetails, current: number | undefined): SpaceView {
    return {
        id: space.id,
        name: space.name,
        root: space.root,
        guestAccess: space.guestAccess,
        isDefault: space.isDefault,
        disabled: space.disabled,
        current: space.id === current,
    };
}
