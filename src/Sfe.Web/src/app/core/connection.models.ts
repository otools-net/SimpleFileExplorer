/*
 * Connections to storage outside the libraries (agents/docs/features/connections.md): what `api/connections` and the
 * sign-in routes answer. A connection's credentials never reach the client.
 */

import { ConnectionLevelName } from './config.models';

export type FolderLevelName = 'hidden' | 'read' | 'write';
export type ConnectionStateName = 'ready' | 'needsReconnecting';
export type SignInKindName = 'oAuthRedirect' | 'oAuthCode' | 'password' | 'key';

/** A group of a library using a connection, and how far. */
export interface ConnectionGroup {
    groupId: number;
    tenantId: number;
    level: FolderLevelName;
}

/** A connection as those who manage it see it. */
export interface ConnectionInfo {
    id: number;
    kind: string;
    name: string;
    level: ConnectionLevelName;
    ownerId: number | null;
    tenantId: number | null;
    /** Server level: offered to every library. */
    allTenants: boolean;
    tenantIds: number[];
    /** The folder of the remote storage it shows ('' = all of it). */
    rootPath: string;
    /** Whose storage it is at the provider (an e-mail address). */
    account: string | null;
    state: ConnectionStateName;
    disabled: boolean;
    readOnly: boolean;
    created: string;
    createdById: number | null;
    groups: ConnectionGroup[];
}

/** A connection the session may use in its library. */
export interface UsableConnection {
    id: number;
    kind: string;
    name: string;
    level: ConnectionLevelName;
    state: ConnectionStateName;
    access: FolderLevelName;
}

/** A connection the session manages: `manage` = rename, switch, sign in again; `remove` = remove. */
export interface ManagedConnection {
    connection: ConnectionInfo;
    manage: boolean;
    remove: boolean;
}

export interface NamedItem {
    id: number;
    name: string;
}

/** `GET api/connections`. */
export interface ConnectionsView {
    usable: UsableConnection[];
    managed: ManagedConnection[];
    /** The library's groups (with `connections.library`). */
    groups: NamedItem[];
    /** Every library (with `connections.server`). */
    libraries: NamedItem[];
}

export interface ConnectionChange {
    name?: string;
    disabled?: boolean;
    readOnly?: boolean;
    rootPath?: string;
}

/** What is asked to make a connection (`POST api/connections/authorize`). */
export interface ConnectRequest {
    kind: string;
    level: ConnectionLevelName;
    name: string;
    method: SignInKindName;
    rootPath?: string;
    fields?: Record<string, string>;
}

/** A field the person fills (described by the server, so the form needs no code per kind). */
export interface SignInField {
    id: string;
    label: string;
    type: 'text' | 'secret' | 'address';
}

/** A sign-in begun: done (`connection`), or the provider's page to open and what to bring back. */
export interface SignInBegun {
    connection: ConnectionInfo | null;
    address: string | null;
    state: string | null;
    fields: SignInField[] | null;
}

/** A sign-in ended. */
export interface SignInDone {
    connection: ConnectionInfo;
    reconnected: boolean;
    otherAccount: boolean;
}

/** How a kind is set up on this server (`api/connections/providers/{kind}`). */
export interface ProviderSetup {
    kind: string;
    name: string;
    offered: boolean;
    /** What to register at the provider for the redirect sign-in. */
    returnAddress: string;
    /** `none`, `page`, `configuration`; null for a kind without an app of its own. */
    appSecret: 'none' | 'page' | 'configuration' | null;
}

export interface ProviderTest {
    ok: boolean;
    message: string;
}

/** The words of a level, for people. */
export const LEVEL_NAMES: Record<ConnectionLevelName, string> = {
    account: 'Only me',
    library: 'This space',
    server: 'The server',
};
