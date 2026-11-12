import type { LibraryItem } from '../core/models';
import type { TagSummary } from '../core/tag.models';
import type { DuplicateSummary, SimilarVideoState } from '../core/task.models';

export interface DuplicateGroup {
    size: number;
    fingerprint: string;
    count: number;
    wastedBytes: number;
    files: LibraryItem[];
}

export interface DuplicateResults {
    path: string;
    groups: DuplicateGroup[];
    hasMore: boolean;
    next: string | null;
    summary: DuplicateSummary | null;
    fingerprinting: boolean;
    minimumSize: number;
    canRemove: boolean;
    /** A copy removed is deleted for good, not moved to the trash (`Files:DeletePermanently`). */
    removesPermanently?: boolean;
}

export interface SimilarVideoResults {
    path: string;
    status: {
        state: SimilarVideoState;
        checked: number;
        total: number;
        started: string | null;
        finished: string | null;
        message: string | null;
    };
    summary: {
        groups: number;
        files: number;
        wastedBytes: number;
        videos: number;
        compared: number;
        minimumPercent: number;
    } | null;
    groups: {
        count: number;
        wastedBytes: number;
        videos: { file: LibraryItem; match: number | null; duration: number }[];
    }[];
    hasMore: boolean;
    next: string | null;
    trickplayEnabled: boolean;
}

export interface MissingFileInfo {
    id: number;
    path: string;
    name: string;
    size: number;
    modified: string;
    missingSince: string;
    tags: TagSummary[];
    candidates: LibraryItem[];
}

export interface MissingFileResults {
    items: MissingFileInfo[];
    total: number | null;
    hasMore: boolean;
    next: number | null;
    enabled: boolean;
}
