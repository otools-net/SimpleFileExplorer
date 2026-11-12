import type { FileConflict } from './models';

/** One thing inside an archive (`api/files/explore`): a folder (with its counts), a file, a link. */
export interface ArchiveItem {
    kind: 'folder' | 'file' | 'link' | 'other';
    id: number;
    name: string;
    /** A folder: its whole tree's files. */
    size: number;
    /** 0 when the format doesn't tell it per entry (solid 7z, rar). */
    packed: number;
    modified: string | null;
    encrypted: boolean;
    /** A folder's folders and entries right inside it, and its files in all. */
    folders?: number | null;
    files?: number | null;
    allFiles?: number | null;
    /**
     * In a search's results: the path of the folder it is in ("" at the top; deep down, its last folders after "…/").
     */
    where?: string | null;
    /** In a search's results: the id of the folder it is in. */
    in?: number | null;
    /** A file the library calls a picture, video or audio (by its extension). */
    media?: 'image' | 'video' | 'audio' | null;
    /** The client's own: its path in the archive, when known (links between its files lead there). */
    at?: string | null;
}

/**
 * A page of a search in an archive: `next` goes on (a page cut short by its time may be empty); null once all was
 * looked through.
 */
export interface ArchiveSearch {
    items: ArchiveItem[];
    next: string | null;
}

/** `api/files/explore/unlock`: what later requests about the archive send back (never the password). */
export interface ArchiveUnlocked {
    unlock: string;
}

/** The archive's list is being read (202): entries read so far. */
export interface ArchiveReading {
    read: number;
}

/** What a search in an archive answers: a page, or the list still being read (ask again). */
export type ArchiveSearchAnswer = ArchiveSearch | ArchiveReading;

/** What trying a password answers: an unlock, the list still being read, or (409) nothing in it is protected. */
export type ArchiveUnlockAnswer = ArchiveUnlocked | ArchiveReading | FileConflict;
