/* Accounts, groups, spaces and rights as the access pages get them, for their specs. */

import { FolderLevel } from '../app/core/access.models';
import { Rights } from '../app/core/rights';
import {
    GroupInfo,
    RightDefinition,
    RightKind,
    RightsCatalog,
    TenantDetails,
    UserInfo,
} from '../app/access/access.models';
import { LIBRARY } from './fakes';

export const WORK = { id: 2, slug: 'work', name: 'Work' };

export function user(id: number, userName: string, overrides: Partial<UserInfo> = {}): UserInfo {
    return {
        id,
        userName,
        displayName: null,
        source: 'local',
        kind: 'user',
        isServerAdmin: false,
        disabled: false,
        lockedOut: false,
        created: '2026-01-01T00:00:00Z',
        lastSignIn: null,
        groups: [],
        ...overrides,
    };
}

export function group(id: number, name: string, overrides: Partial<GroupInfo> = {}): GroupInfo {
    return {
        id,
        tenantId: LIBRARY.id,
        name,
        builtIn: null,
        externalName: null,
        rights: [],
        memberIds: [],
        ...overrides,
    };
}

/** The built-in groups of a space: Administrators `base`, Users `base + 1`, Guests `base + 2`. */
export function builtIns(tenantId: number, base: number): GroupInfo[] {
    return [
        group(base, 'Administrators', { tenantId, builtIn: 'administrators' }),
        group(base + 1, 'Users', { tenantId, builtIn: 'users' }),
        group(base + 2, 'Guests', { tenantId, builtIn: 'guests' }),
    ];
}

export function member(groupInfo: GroupInfo) {
    return { tenantId: groupInfo.tenantId, groupId: groupInfo.id, group: groupInfo.name };
}

export function space(id: number, name: string, overrides: Partial<TenantDetails> = {}): TenantDetails {
    return {
        id,
        slug: name.toLowerCase(),
        name,
        guestAccess: false,
        isDefault: id === LIBRARY.id,
        disabled: false,
        root: `D:\\Media\\${name}`,
        disabledRights: [],
        members: 0,
        groups: 3,
        ...overrides,
    };
}

export function right(
    id: string,
    guestAllowed: boolean,
    area = 'Library',
    kind: RightKind = guestAllowed ? 'read' : 'write',
    atFolder: FolderLevel | null = null,
): RightDefinition {
    return {
        id,
        scope: 'tenant',
        guestAllowed,
        defaultGroups: guestAllowed ? ['administrators', 'users', 'guests'] : ['administrators'],
        area,
        title: id,
        description: `About ${id}`,
        kind,
        atFolder,
    };
}

export const catalog: RightsCatalog = {
    rights: [
        right(Rights.editImages, false, 'Library', 'write', 'write'),
        right(Rights.browse, true, 'Library', 'read', 'read'),
        right(Rights.manageTags, false, 'Tags'),
        right(Rights.download, true, 'Library', 'read', 'read'),
        { ...right(Rights.manageTenants, false, 'Server'), scope: 'server' },
    ],
    disabledByServer: [Rights.download],
    disabledInTenant: [],
};
