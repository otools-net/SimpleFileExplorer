/*
 * What the pages of Administration › Spaces & accounts share. The model they show: a space has one library (a folder of
 * the server, with its own index, tags and settings), its own groups, each granting rights, and its accounts: an
 * account is in a space when it is in at least one of the space's groups, and may be in several spaces.
 */

import { ApiError } from '../core/api.service';
import { GroupInfo, RightDefinition, UserInfo } from '../core/models';

/** A space as the account and group editors name it. */
export interface SpaceRef {
    id: number;
    name: string;
}

/** The errors of a validation problem by field, null for other failures (shown as a message). */
export function fieldErrors(error: unknown): Record<string, string> | null {
    return error instanceof ApiError && error.errors
        ? Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')]))
        : null;
}

export function toggled<T>(set: ReadonlySet<T>, value: T, on: boolean): ReadonlySet<T> {
    const next = new Set(set);
    if (on)
        next.add(value);
    else
        next.delete(value);
    return next;
}

/** The account is in the space (one of its groups there). */
export function inSpace(user: UserInfo, space: number): boolean {
    return user.groups.some(group => group.tenantId === space);
}

/** The names of the account's groups in the space, in the order of `groups` (built-in ones first). */
export function groupNamesIn(user: UserInfo, space: number, groups: GroupInfo[]): string[] {
    const ids = new Set(user.groups.filter(group => group.tenantId === space).map(group => group.groupId));
    const ordered = groups.filter(group => ids.has(group.id)).map(group => group.name);
    // Groups not loaded (another space's) keep the account's own order.
    return ordered.length === ids.size ? ordered : user.groups.filter(group => group.tenantId === space).map(group => group.group);
}

/** The rights of a space by area, in the catalog's order. */
export function rightAreas(rights: RightDefinition[]): { title: string; rights: RightDefinition[] }[] {
    const spaceRights = rights.filter(right => right.scope === 'tenant');
    return [...new Set(spaceRights.map(right => right.area))].map(title => ({ title, rights: spaceRights.filter(right => right.area === title) }));
}

/** What a built-in group is for, in a few words. */
export const BUILT_IN_PURPOSE: Record<string, string> = {
    administrators: 'Manage the space: everything but the server\'s own settings',
    users: 'Everyday use: browse, play, download and tag',
    guests: 'What visitors without an account may do',
};
