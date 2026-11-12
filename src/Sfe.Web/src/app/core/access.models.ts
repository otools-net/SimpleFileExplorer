export type UserKind = 'user' | 'guest';

export interface SessionAccount {
    id: number;
    userName: string;
    displayName: string | null;
    kind: UserKind;
    isServerAdmin: boolean;
    /** A local account: its password can be changed here (a directory's is changed in the directory). */
    local: boolean;
}

export interface SessionTenant {
    id: number;
    slug: string;
    name: string;
}

/** /api/session: who is signed in, in which space, and what the sign-in page offers. */
export interface SessionInfo {
    user: SessionAccount | null;
    tenant: SessionTenant | null;
    /** The spaces the account can switch to. */
    tenants: SessionTenant[];
    /** Spaces open to visitors without an account. */
    guestTenants: SessionTenant[];
    setupRequired: boolean;
    setupAvailable: boolean;
    canBrowse: boolean;
}

/**
 * none = the install belongs to this version; the others keep everything but the update page closed (503
 * `migration-required`).
 */
export type MigrationStatus = 'none' | 'required' | 'running' | 'failed' | 'newer' | 'unavailable';

/** A version of CHANGELOG.md: its sections (Added, Changed, Fixed), each item in the changelog's Markdown. */
export interface ChangelogEntry {
    version: string;
    sections: { title: string; items: string[] }[];
}

/** A setting of the installation page; `locked` when the server's environment (variables, command line) decides it. */
export interface InstallationField {
    value: string | null;
    locked: boolean;
    lockedBy: string | null;
}

/** /api/installation: the library folder, listening address and ffmpeg of the server. */
export interface InstallationView {
    root: InstallationField;
    rootExists: boolean;
    urls: InstallationField;
    port: number;
    /** Listens for other devices of the network, not only this computer. */
    network: boolean;
    ffmpegPath: InstallationField;
    ffmpegFound: string | null;
    ffmpegVersion: string | null;
    /** The address saved applies once SFE starts again. */
    restartRequired: boolean;
    /** `Sfe:Mode` as configured; a null value = not chosen yet (missing from older servers). */
    mode?: InstallationField;
    /** The mode the server works in: without one configured, Local. */
    modeInEffect?: ServerMode;
    /** The visitor is on the server's own computer: only they may choose Local. */
    onThisComputer?: boolean;
}

/** How the server is used: on this computer by its administrators, or by other people over the network. */
export type ServerMode = 'local' | 'external';

export interface InstallationGrant {
    /** Sent back in `X-SFE-Installation` for an hour. */
    token: string;
    settings: InstallationView;
}

export interface SetupInput {
    code: string;
    userName: string;
    password: string;
    displayName?: string | null;
    libraryName?: string | null;
    /** Sign the administrator in at once in the browser SFE opens when it starts (server setting `fastSignIn`). */
    fastSignIn?: boolean;
}

/** Sets a group's or an account's level at a folder; `level` null removes its rule there. */
export interface FolderRuleChange {
    path: string;
    groupId: number | null;
    userId: number | null;
    level: FolderLevel | null;
}

export interface FolderAccessEntry {
    groupId: number | null;
    userId: number | null;
    name: string;
    /** A group's level there; an account's own rule's (null: its groups decide). */
    level: FolderLevel | null;
    /** From a rule on this very folder. */
    explicit: boolean;
    /** May browse the folder, all things counted. */
    sees: boolean;
    /** May change its files. */
    changesFiles: boolean;
}

/** What a folder lets a subject do in it: `write` includes `read`; only `write` lets its files on disk change. */
export type FolderLevel = 'hidden' | 'read' | 'write';

export interface Membership {
    tenantId: number;
    groupId: number;
    group: string;
}

export interface UserInput {
    userName: string;
    displayName?: string | null;
    /** Required for a new account; null keeps the current one. */
    password?: string | null;
    kind: UserKind;
    isServerAdmin: boolean;
    disabled: boolean;
    groupIds?: number[] | null;
    unlock?: boolean;
}

export interface GroupInput {
    name: string;
    externalName?: string | null;
    rights?: string[] | null;
    memberIds?: number[] | null;
}

export interface TenantAccess {
    id: number;
    name: string;
    guestAccess: boolean;
    /** The server lets spaces open to visitors without an account (Sfe:Security:AnonymousGuests). */
    anonymousGuestsAllowed: boolean;
    disabledRights: string[];
}

export interface TenantInput {
    name: string;
    slug?: string | null;
    root?: string | null;
    guestAccess: boolean;
    disabled: boolean;
    disabledRights?: string[] | null;
}
