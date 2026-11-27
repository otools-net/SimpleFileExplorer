/*
 * Connections to storage outside the libraries (agents/docs/features/connections.md): what `api/connections` and the
 * sign-in routes answer. A connection's credentials never reach the client.
 */

import { ConnectionLevelName } from './config.models';
import { DirectoryListing, ItemAccess, LibraryItem } from './models';

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
    account: $localize`:level of a connection:Only me`,
    library: $localize`:level of a connection:This space`,
    server: $localize`:level of a connection:The server`,
};

/** `GET api/connections/{id}/browse`: a page of a connection's folder, in the library's listing shape. */
export interface ConnectionListing {
    connectionId: number;
    /** The connection's name: the root folder's. */
    name: string;
    kind: string;
    /** Paths relative to the connection's root; the first breadcrumb is the connection's name. */
    listing: DirectoryListing;
    access: FolderLevelName;
    /** False for a folder too large to read whole: it comes in the provider's order. */
    sorted: boolean;
    /** The folder has more items than `listing.total` counts so far. */
    hasMore: boolean;
    capabilities: Record<string, boolean | number | string | null>;
}

/** The connection the listing shows (`?c=`): its files' addresses and what can be done there. */
export interface ConnectionPlace {
    id: number;
    /** Its name, shown for its root folder; empty until listed. */
    name: string;
    /** Its provider makes thumbnails. */
    thumbnails: boolean;
    access: FolderLevelName;
    /** Its provider moves (`serverSideMove`) and copies (`serverSideCopy`) files itself. */
    serverSideMove: boolean;
    serverSideCopy: boolean;
    /** Its provider keeps deleted files for a while (`ownTrash`). */
    ownTrash: boolean;
    /** Its provider takes files sent in parts (`uploadsInParts`): uploads, with write access. */
    uploadsInParts: boolean;
}

/** What became of one path moved or copied on a connection (`POST api/connections/{id}/files/transfer`). */
export interface PlaceTransferItem {
    path: string;
    /** `exists`: the name is taken in the target (asked again with `keepBoth` or `replace`). */
    status: 'done' | 'exists' | 'notFound' | 'invalid' | string;
    message?: string | null;
    /** The item moved or copied, where it is now. */
    item?: LibraryItem | null;
}

export interface PlaceTransferResults {
    items: PlaceTransferItem[];
}

/** `GET api/connections/{id}/files/properties?path=`: what the provider tells about one of a connection's items. */
export interface PlaceProperties {
    /** Its path is relative to the connection's root; no modified date for a Dropbox folder. */
    item: LibraryItem;
    /** The provider's kind (`config.connections.kinds[].kind`). */
    kind: string;
    /** Whose storage it is at the provider, when known. */
    account: string | null;
    /** The provider's own hash of the content, and its name (e.g. "Dropbox content hash"). */
    contentHash: string | null;
    hashName: string | null;
    /** False for an online document: nothing to download. */
    downloadable: boolean;
    /** What the session may do with it (`disk`: whether the storage takes changes where it is). */
    access?: ItemAccess | null;
}

/** A folder rule of a connection for one group or account of the library (`api/connections/{id}/folder-rules`). */
export interface ConnectionRule {
    /** Relative to the connection's root folder; empty for the root. */
    path: string;
    groupId: number | null;
    userId: number | null;
    level: FolderLevelName;
    /** The provider's id of the folder, when known. */
    itemId: string | null;
}

/** A folder rule of a connection as listed: with its id and its group's or account's name. */
export interface ConnectionRuleView extends ConnectionRule {
    id: number;
    name: string;
}

/** `GET api/connections/{id}/index`: where the index of a connection stands in the session's library. */
export interface ConnectionIndexStatus {
    /** The library has begun indexing it (it was opened here). */
    indexed: boolean;
    running: boolean;
    /** A full listing is under way: the index doesn't hold everything yet. */
    listing: boolean;
    files: number;
    folders: number;
    lastSync: string | null;
    /** Items left out: their path is longer than the index stores. */
    tooLong: number;
    lastError: string | null;
    /** What the running sync is doing, in words. */
    activity: string | null;
}
