import type { DirectoryListing, FolderExclusions, ItemKind, LibraryItem } from '../core/models';
import type { TagUsage } from '../core/tag.models';
import type { ScanState } from '../core/task.models';

export type SortField = 'name' | 'modified' | 'size' | 'type' | 'path' | 'duration' | 'resolution';

export interface TagSidebar {
    popular: TagUsage[];
    searched: TagUsage[];
    viewed: TagUsage[];
    lucky: TagUsage[];
    historyEnabled: boolean;
}

/** A folder or file kept in the sidebar's quick links (`api/me/quick-links`). */
export interface QuickLink {
    path: string;
    name: string;
    /** `directory`, or the kind of file by its extension, as it was when added. */
    kind: ItemKind;
    extension: string | null;
    /** False once it was moved, renamed or deleted. */
    exists: boolean;
}

export type ExclusionKind = keyof FolderExclusions;

/**
 * A folder on the exclusion lists (Administration › Scanning › Excluded folders): which lists, and whether it is still
 * on the disk.
 */
export interface ExcludedFolder {
    path: string;
    scan: boolean;
    trickplay: boolean;
    thumbnails: boolean;
    exists: boolean;
}

/** How many files and folders of the trash the session sees, counted up to 99 (`more` above). */
export interface TrashCount {
    count: number;
    more: boolean;
}

export interface SearchResults {
    query: string;
    path: string;
    items: LibraryItem[];
    limit: number;
    hasMore: boolean;
    next: string | null;
    /** Matches counted on the first page (null on the next ones), at most 10,000: `totalCapped` when there are more. */
    total?: number | null;
    totalCapped?: boolean;
}

export type ListingPage = DirectoryListing | SearchResults;

export interface FolderSiblings {
    path: string;
    previous: LibraryItem | null;
    next: LibraryItem | null;
    position: number;
    count: number;
}

export interface FolderCover {
    path: string;
    items: LibraryItem[];
}

/** How keep alive goes in the space, as the change feed tells it: `none` when off or nothing pinged. */
export type KeepAliveState = 'none' | 'ok' | 'failing';

export interface ScanStatus {
    state: ScanState;
    path: string | null;
    startedAt: string | null;
    finishedAt: string | null;
    directoriesScanned: number;
    directoriesQueued: number;
    filesIndexed: number;
    removed: number;
    errors: number;
    currentDirectory: string | null;
    message: string | null;
    relinked: number;
    missing: number | null;
}

/** What the index holds of a folder and its sub-folders. */
export interface IndexedFolder {
    path: string;
    indexed: boolean;
    directories: number;
    files: number;
    taggedFiles: number;
    /** In the scans' excluded folders. */
    excluded: boolean;
}

export interface RemovedFromIndex {
    path: string;
    directories: number;
    files: number;
    taggedFiles: number;
    excluded: boolean;
}
