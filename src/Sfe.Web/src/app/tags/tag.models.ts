import type { FolderDefaults, TagScope, TagUsage } from '../core/tag.models';

export interface TagInput {
    name: string;
    shorthand?: string | null;
    aliases?: string[];
    parentIds?: number[];
    disambiguationId?: number | null;
    color?: string | null;
    isCategory?: boolean;
    isHidden?: boolean;
}

export interface FileTagChange {
    paths: string[];
    add?: number[];
    remove?: number[];
}

/** The indexed files of a scope and the tags on them, most common first. */
export interface ScopeTags {
    scope: TagScope;
    files: number;
    tags: TagUsage[];
}

export interface ScopeTagChangeResult {
    scope: TagScope;
    files: number;
    added: number;
    removed: number;
}

/** `files`: what changed on the files already there, null when that wasn't asked. */
export interface FolderDefaultTagsResult {
    defaults: FolderDefaults;
    files: ScopeTagChangeResult | null;
}
