import { MenuEntry } from '../app/menus/context-menu';

/** Every entry of a menu, its submenus ("Archive ▸") opened in place. */
export function flatEntries<T extends MenuEntry>(entries: T[]): T[] {
    return entries.flatMap((entry) => (entry.submenu ? flatEntries(entry.submenu() as T[]) : [entry]));
}

/** The ids of the entries that can be used: shown and not greyed out, submenus opened. */
export function usableIds(entries: MenuEntry[]): string[] {
    return flatEntries(entries)
        .filter((entry) => !entry.disabled)
        .map((entry) => entry.id);
}

/** The ids of the entries greyed out (the rights don't allow them), submenus opened. */
export function greyedIds(entries: MenuEntry[]): string[] {
    return flatEntries(entries)
        .filter((entry) => entry.disabled)
        .map((entry) => entry.id);
}

/** The entry `id`, in a submenu or not. */
export function entryOf<T extends MenuEntry>(entries: T[], id: string): T {
    const entry = flatEntries(entries).find((each) => each.id === id);
    if (!entry) throw new Error(`No entry "${id}" in ${entries.map((each) => each.id).join(', ')}.`);
    return entry;
}
