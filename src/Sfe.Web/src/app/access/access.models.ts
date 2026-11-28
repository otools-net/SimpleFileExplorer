import type { FolderAccessEntry, FolderLevel, Membership, UserKind } from '../core/access.models';

export type BuiltInGroup = 'administrators' | 'users' | 'guests';

export interface RightDefinition {
    id: string;
    scope: 'tenant' | 'server';
    /** Guests may get it. */
    guestAllowed: boolean;
    defaultGroups: BuiltInGroup[];
    area: string;
    title: string;
    description: string;
    /** `write` when one of its functions changes something (files, index, tags, settings, accounts). */
    kind: RightKind;
    /** What a folder must allow for it to apply there; null for a right of the whole space. */
    atFolder: FolderLevel | null;
}

export type RightKind = 'read' | 'write';

/** A folder rule: what a group, or one account, may do in a folder and below it. */
export interface FolderRuleView {
    id: number;
    path: string;
    groupId: number | null;
    userId: number | null;
    /** The group's name, or the account's. */
    name: string;
    level: FolderLevel;
    /** The folder is still there (false once moved or renamed outside SFE). */
    exists: boolean;
    created: string;
}

/** Who may see and change a folder. */
export interface FolderAccessReport {
    path: string;
    exists: boolean;
    groups: FolderAccessEntry[];
    accounts: FolderAccessEntry[];
    /** Visitors without an account, when the space lets them in. */
    visitors: FolderAccessEntry | null;
}

export interface RightsCatalog {
    rights: RightDefinition[];
    /** Switched off for the whole server (Sfe:Rights:Disabled). */
    disabledByServer: string[];
    /** Switched off in the space asked about. */
    disabledInTenant: string[];
}

export interface UserInfo {
    id: number;
    userName: string;
    displayName: string | null;
    source: string;
    kind: UserKind;
    isServerAdmin: boolean;
    disabled: boolean;
    lockedOut: boolean;
    created: string;
    lastSignIn: string | null;
    groups: Membership[];
}

export interface GroupInfo {
    id: number;
    tenantId: number;
    name: string;
    builtIn: BuiltInGroup | null;
    externalName: string | null;
    rights: string[];
    memberIds: number[];
    /** Its members' home folder when they chose none (`{user}`: the account's name); null for none. */
    homeFolder?: string | null;
}

export interface TenantDetails {
    id: number;
    slug: string;
    name: string;
    guestAccess: boolean;
    isDefault: boolean;
    disabled: boolean;
    /** Full path of its library folder on the server. */
    root: string;
    disabledRights: string[];
    /** Accounts in at least one of its groups. */
    members: number;
    /** Its groups, the built-in ones included. */
    groups: number;
}
