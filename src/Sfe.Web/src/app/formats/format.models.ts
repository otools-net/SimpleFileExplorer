import type { FileConflict } from '../core/models';
import type { ArchiveItem, ArchiveReading } from '../core/archive.models';
import type { SaveTextResult, SavedText } from '../core/config.models';

/** A page of a folder of an archive. */
export interface ArchiveListing {
    format: string;
    /** False when the list stopped (a limit, damage): `stopped` says why. */
    complete: boolean;
    stopped: string | null;
    needsPassword: boolean;
    entries: number;
    /** Entries not listed (names leaving the archive, too deep). */
    leftOut: number;
    folder: ArchiveItem;
    /** The way to the folder: its last few folders, and how many sit between them and the root. */
    trail: { skipped: number; last: { id: number; name: string }[] };
    items: ArchiveItem[];
    next: string | null;
    /** Some of its files are encrypted (or its list was read with a password). */
    protected?: boolean;
    /** The unlock sent opens this archive. */
    unlocked?: boolean;
    /** The folder's whole path in the archive ("" at its top), on a first page of a folder not too deep; else null. */
    path?: string | null;
}

/** What `api/files/explore` answers: a page, the reading so far, or why it can't be explored (409). */
export type ArchiveAnswer = ArchiveListing | ArchiveReading | FileConflict;

/** A text file as the viewer reads it (`api/files/text`). */
export interface TextFile {
    /** Line endings as they are in the file. */
    text: string;
    encoding: 'utf-8' | 'utf-8-bom' | 'utf-16le' | 'utf-16be' | 'windows-1252' | string;
    lineEnding: 'lf' | 'crlf' | 'cr' | 'mixed' | string;
    /** The content's version, which a save will name. */
    version: string;
    size: number;
    modified: string;
    /** What the session may do with it (from the server). */
    access?: TextAccess | null;
    /** The line ending a save writes: the file's, or the most used one of a file that mixes them. */
    saveLineEnding?: 'lf' | 'crlf' | 'cr' | string | null;
}

/** A part of a text file (`api/files/text/part`): from `offset` to `end` bytes of `size`. */
export interface TextPart {
    text: string;
    offset: number;
    end: number;
    size: number;
    encoding: string;
    modified: string;
}

/** What saving a text file may do (`api/files/text`). */
export interface TextAccess {
    /** Save over it: may delete it and create files in its folder (as move/copy's Replace). */
    overwrite: boolean;
    /** Save a copy in its folder. */
    saveCopy: boolean;
    download: boolean;
    /** The space deletes for good: no previous version is kept in the trash. */
    permanent: boolean;
}

export function isSaved(result: SaveTextResult): result is SavedText {
    return 'version' in result;
}

/** A text to save: in `encoding`, its lines ending with `lineEnding`. */
export interface TextToSave {
    text: string;
    encoding: string;
    lineEnding: string;
}
