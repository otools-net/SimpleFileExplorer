import type { FolderDefaults, TagSummary } from './tag.models';

/*
 * The JSON shapes of the SFE API (see the records in Sfe.Core and Sfe.Api). Enums come as camelCase strings, dates
 * as ISO 8601 strings.
 */

export type ItemKind = 'directory' | 'image' | 'video' | 'audio' | 'file';

export interface LibraryItem {
    name: string;
    path: string;
    kind: ItemKind;
    extension: string | null;
    size: number | null;
    modified: string | null;
    /** Search results only: the folder of the file ('' = the root). */
    directory?: string | null;
    tags?: TagSummary[] | null;
    /** Pixels of an image or video, when the index knows them. */
    width?: number | null;
    height?: number | null;
    /** Seconds of a video or audio file, when the index knows them. */
    duration?: number | null;
    /** Browsing only: false for a file or folder no scan has indexed yet; null/absent when unknown (search results). */
    indexed?: boolean | null;
    /** Browsing only, for an indexed folder: how many files and sub-folders directly inside it the index has. */
    indexedItems?: number | null;
    /** Browsing only, for a folder kept out of scans, trickplay or thumbnails; absent when it is in none. */
    excluded?: FolderExclusions | null;
    /** Browsing only, for a folder: the default tags set on it (not inherited ones); absent when it has none. */
    defaultTags?: TagSummary[] | null;
}

/**
 * What a folder is kept out of, for the whole space (`index.exclude`): for each, the listed folder it is excluded
 * through (itself, or the nearest listed parent), null when it isn't.
 */
export interface FolderExclusions {
    scan: string | null;
    trickplay: string | null;
    thumbnails: string | null;
}

/**
 * Why a file operation was not done although the request was fine (409): something of that name is there
 * (`file-exists`), a scan runs over the folder (`scan-running`), folder rules are set where a folder would go
 * (`folder-rules`).
 */
export interface FileConflict {
    type: 'file-exists' | 'scan-running' | 'folder-rules' | string;
    detail?: string | null;
}

/** The answer of a file operation: the new or renamed item, or why it was not done. */
export type FileOperationResult = LibraryItem | FileConflict;

export function isFileConflict(result: FileOperationResult): result is FileConflict {
    return !('path' in result);
}

export type DeleteStatus = 'done' | 'notFound' | 'forbidden' | 'scanRunning' | 'trashUnavailable' | 'failed' | string;

/** What became of one path of a delete; `trashId` when it went to the trash (for an Undo). */
export interface DeletedItem {
    path: string;
    status: DeleteStatus;
    message?: string | null;
    trashId?: number | null;
}

export interface DeleteResults {
    items: DeletedItem[];
    /** It skipped the trash (asked, or the space always does). */
    permanent: boolean;
}

export type FileJobState = 'running' | 'waiting' | 'completed' | 'cancelled';

/** An item on one side of a name clash. */
export interface ClashSide {
    name: string;
    isFolder: boolean;
    size: number | null;
    modified: string | null;
}

export interface FileJobConflict {
    source: ClashSide;
    existing: ClashSide;
    /** The session may replace it (it would go to the trash). */
    canReplace: boolean;
}

/** The names already taken in a folder, and whether the session may replace them. */
export interface UploadCheck {
    existing: string[];
    canReplace: boolean;
}

export interface DirectoryListing {
    path: string;
    name: string;
    parentPath: string | null;
    items: LibraryItem[];
    offset: number;
    limit: number;
    total: number;
    directoryCount: number;
    fileCount: number;
    /** What folder rules let the session do here; absent or null when none apply (every right of the space). */
    access?: FolderAccessView | null;
    /** What the folder is kept out of; absent or null when nothing. */
    exclusions?: FolderExclusions | null;
    /** The default tags of the folder, its own and its parents'; absent or null when none. */
    defaultTags?: FolderDefaults | null;
}

/** What folder rules let the session do in a folder. */
export interface FolderAccessView {
    rights: string[];
    /** The session doesn't see the folder: it only leads to folders below that it sees. */
    pathOnly: boolean;
    /** The entries whose access differs from the folder's, by name; the others have the folder's. */
    children?: Record<string, FolderAccessView> | null;
}

export type KeepAliveMode = 'off' | 'auto' | 'always';

/** `list`: the folder is read; `touch`: a small file is written and deleted in the work folder. */
export type KeepAliveMethod = 'list' | 'touch';

/** What the disk says of a file or folder (Properties). Dates are ISO, null when the file system doesn't keep them. */
export interface ItemDetails {
    path: string;
    name: string;
    isFolder: boolean;
    size: number | null;
    created: string | null;
    modified: string;
    accessed: string | null;
    /** A name starting with a dot, or the system's hidden attribute. */
    hidden: boolean;
    readOnly: boolean;
    /** A symbolic link or junction (where it leads is not told). */
    isLink: boolean;
}

export interface ItemProperties {
    item: LibraryItem;
    details: ItemDetails;
    /** May read the file whole for a checksum (right to download it). */
    canChecksum: boolean;
}

/** A zip of several files or folders: the browser downloads it once from `url`, soon. */
export interface ArchiveLink {
    url: string;
    fileName: string;
}

export interface FolderChoice {
    name: string;
    path: string;
}

export interface FolderPicker {
    /** The folder listed; null for the allowed roots. */
    path: string | null;
    parent: string | null;
    folders: FolderChoice[];
    /** At the top of the installation page's picker: the home folder, the library folder now, Pictures... */
    places?: FolderChoice[];
    /** More sub-folders than the server lists. */
    truncated?: boolean;
}

/** A link to a file working without signing in, for a while (external players). */
export interface FileLink {
    /** Relative to the app. */
    url: string;
    expires: string;
}

// The models of each area, until their importers take them from there (readability plan, phase 3).
export * from '../access/access.models';
export * from '../account/account.models';
export * from '../admin/admin.models';
export * from '../editor/editor.models';
export * from '../files/file.models';
export * from '../formats/format.models';
export * from '../library/library.models';
export * from '../logs/log.models';
export * from '../migration/migration.models';
export * from '../settings/settings.models';
export * from '../tags/tag.models';
export * from '../tasks/task.models';
export * from '../tracking/tracking.models';
export * from '../trickplay/trickplay.models';
export * from './access.models';
export * from './archive.models';
export * from './config.models';
export * from './setting.models';
export * from './tag.models';
export * from './task.models';
