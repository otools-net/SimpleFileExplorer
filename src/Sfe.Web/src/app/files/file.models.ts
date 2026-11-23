import type { DeleteStatus, FileJobConflict, FileJobState } from '../core/models';
import type { FolderLevel } from '../core/access.models';

export type FileJobKind = 'move' | 'copy' | 'extract' | 'compress';

/** Files and folders compressed into one zip (`POST api/files/jobs/compress`). */
export interface CompressRequest {
    paths: string[];
    target: string;
    /** `.zip` added; null: the only item's name, else their folder's. */
    name?: string | null;
    /** 0 (stored) to 9 (smallest). */
    level: number;
    /** Pictures, videos, archives… stored whatever the level. */
    skipCompressed: boolean;
    /** AES-256; the names stay readable. */
    password?: string | null;
    conflicts?: ConflictChoice;
}

/** An archive extracted (`POST api/files/jobs/extract`): all of it, or the folders and files chosen in its listing. */
export interface ExtractRequest {
    archive: string;
    target: string;
    /** Into a new folder named after the archive (or after its one top folder); else straight into `target`. */
    newFolder: boolean;
    /** Ids of the archive's listing. */
    folders?: number[];
    files?: number[];
    conflicts?: ConflictChoice;
    /** The viewer's unlock of the archive, or its password. */
    unlock?: string | null;
    password?: string | null;
}

/** What a job does when the target holds an item of the same name; `ask`: it waits for the answer. */
export type ConflictChoice = 'ask' | 'replace' | 'skip' | 'keepBoth';

export interface FileJobRequest {
    kind: FileJobKind;
    paths: string[];
    target: string;
    conflicts?: ConflictChoice;
    /** Moved folders keep the access each group and account had. */
    keepAccess?: boolean;
    /** Copies get the tags of their originals. */
    keepTags?: boolean;
}

/** A move or copy between two places (`POST api/files/jobs/transfer`): a connection's id, null for the library. */
export interface TransferRequest {
    kind: 'move' | 'copy';
    from: { connection: number | null; paths: string[] };
    to: { connection: number | null; path: string };
    conflicts: ConflictChoice;
}

/** What became of one item of a job; `newPath`: where it is now (moved, or the copy). */
export interface FileJobItem {
    path: string;
    newPath: string | null;
    /** `needsPassword`: an extraction of an archive whose password wasn't given, or was wrong. */
    status: DeleteStatus | 'skipped' | 'invalid' | 'conflict' | 'needsPassword';
    message?: string | null;
}

/** A move or copy running in the background, or done. */
export interface FileJobView {
    id: number;
    kind: FileJobKind;
    state: FileJobState;
    target: string;
    total: number;
    done: number;
    bytesTotal: number;
    bytesDone: number;
    current: string | null;
    conflict: FileJobConflict | null;
    results: FileJobItem[];
    started: string;
    finished: string | null;
    /** The places of a transfer between them (`api/files/jobs/transfer`): a connection's id, null for the library. */
    fromConnection?: number | null;
    toConnection?: number | null;
}

/** What starts an upload (`POST …/uploads`): the folder, the file's name (its path below it), size and date. */
export interface UploadStart {
    target: string;
    name: string;
    size: number;
    modified: string | null;
}

/** An upload not finished yet, as the server has it (`received` bytes so far). */
export interface UploadInfo {
    id: string;
    target: string;
    /** The file's name, or its path below `target` for a folder upload. */
    name: string;
    size: number;
    received: number;
    modified: string | null;
    ownerId: number | null;
    ownerName: string;
    started: string;
    lastActivity: string;
}

/** Someone who would see or change more of a moved folder than before. */
export interface AccessGain {
    folder: string;
    who: string;
    isGroup: boolean;
    before: FolderLevel;
    after: FolderLevel;
}

/** A file or folder in the trash. `path`: where it was. */
export interface TrashItem {
    id: number;
    path: string;
    name: string;
    isFolder: boolean;
    size: number | null;
    files: number | null;
    deletedAt: string;
    deletedBy: string | null;
    deletedById: number | null;
    hasRules?: boolean;
    /** The content of a file before a save over it (text editor); the file itself is still there. */
    previousVersion?: boolean;
}

export interface TrashPage {
    items: TrashItem[];
    hasMore: boolean;
    /** The `before` of the next page. */
    next: number | null;
    /** Days in the trash before the purge; 0 = until emptied. */
    retentionDays: number;
    /** What the whole trash holds: on the first page, for those who see all of it. */
    size?: TrashSize | null;
}

/** The sizes the index knew of the trash's entries, and how many it didn't know. */
export interface TrashSize {
    bytes: number;
    unknown: number;
}

/** What the index knows below a folder, among what the session sees. */
export interface FolderTotals {
    indexed: boolean;
    files: number;
    folders: number;
    bytes: number;
}

export type ChecksumAlgorithm = 'sha256' | 'sha1' | 'md5';

/** A folder counted on the disk, or a file's checksum, running on the server. */
export interface PropertyJob {
    id: string;
    kind: 'count' | 'checksum';
    path: string;
    algorithm: ChecksumAlgorithm | null;
    done: boolean;
    files: number;
    folders: number;
    /** Counted, or read so far for a checksum. */
    bytes: number;
    /** The file's size, for a checksum. */
    total: number | null;
    /** Links not followed and folders that couldn't be read. */
    leftOut: number;
    /** The count stopped at its limit: the folder holds more. */
    capped: boolean;
    checksum: string | null;
    error: string | null;
}
