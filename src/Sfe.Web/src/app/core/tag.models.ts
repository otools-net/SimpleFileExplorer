export interface TagSummary {
    id: number;
    name: string;
    displayName: string;
    color: string | null;
    isCategory: boolean;
    isHidden: boolean;
}

export interface TagDetails extends TagSummary {
    shorthand: string | null;
    isBuiltIn: boolean;
    disambiguationId: number | null;
    aliases: string[];
    parents: TagSummary[];
    fileCount: number;
}

export interface TagSearchResults {
    matches: TagDetails[];
    descendants: TagDetails[];
}

export interface TagGroup {
    category: TagSummary | null;
    tags: TagSummary[];
}

export interface FileTags {
    path: string;
    tags: TagSummary[];
    groups: TagGroup[];
}

export interface FileTagChangeResult {
    added: number;
    removed: number;
    files: FileTags[];
}

export interface TagUsage {
    tag: TagSummary;
    fileCount: number;
}

/**
 * Files tagged together without listing them: those of a folder (`recursive`: with its sub-folders), or a search's
 * matches (`query`).
 */
export interface TagScope {
    path: string;
    recursive: boolean;
    query: string | null;
}

export interface ScopeTagChange extends TagScope {
    add?: number[];
    remove?: number[];
}

/** A default tag a folder gets from one of its parents (`path`, '' = the root). */
export interface InheritedDefaultTag {
    path: string;
    tag: TagSummary;
}

/**
 * The default tags of a folder: every file entering it or one of its sub-folders (scanned, created, uploaded, copied
 * or moved there) gets them once. Its own, and those of its parents.
 */
export interface FolderDefaults {
    path: string;
    own: TagSummary[];
    inherited: InheritedDefaultTag[];
}

/**
 * Default tags to add to a folder or remove from it; `addToFiles`/`removeFromFiles` change the files already there too.
 */
export interface FolderDefaultTagsChange {
    path: string;
    add?: number[];
    remove?: number[];
    addToFiles?: boolean;
    removeFromFiles?: boolean;
}

export interface TagColor {
    slug: string;
    name: string;
    hex: string;
}
