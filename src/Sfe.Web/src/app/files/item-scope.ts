import { LibraryItem } from '../core/models';
import { MenuEntry } from '../menus/context-menu';
import { RefusedAction, refusedText } from './item-command-texts';

/**
 * What an item command acts on (agents/docs/features/context-menu.md): the item whose menu it is and, when that item is
 * part of the selection, all of it (the selection bar: the selection, its first item).
 */
export interface ItemScope {
    item: LibraryItem;
    targets: LibraryItem[];
}

/** Every command on listed items: the menu of an item, the selection bar and its "⋯" (`ItemCommands`). */
export type ItemCommandId =
    | 'open'
    | 'newTab'
    | 'openFolder'
    | 'openAsText'
    | 'openAsArchive'
    | 'editText'
    | 'editPicture'
    | 'cut'
    | 'copy'
    | 'paste'
    | 'rename'
    | 'moveTo'
    | 'copyTo'
    | 'tags'
    | 'quickLink'
    | 'exclusions'
    | 'folderAccess'
    | 'extractBeside'
    | 'extractEach'
    | 'extract'
    | 'compress'
    | 'download'
    | 'copyPath'
    | 'delete'
    | 'properties';

/** The entries grouping others in a submenu ("Open as ▸", "Archive ▸"). */
export type ItemSubmenuId = 'openAs' | 'archive';

/** One entry of an item's menu: always an action (a submenu's does nothing itself). */
export interface ItemMenuEntry extends MenuEntry {
    id: ItemCommandId | ItemSubmenuId;
    run: () => void;
}

/**
 * The entry of a command for a scope; null where it doesn't apply (the kind of item, how many are selected, a function
 * the server doesn't have): hidden. One the rights don't allow is returned greyed out (`refused`).
 */
export type ItemCommand = (scope: ItemScope) => ItemMenuEntry | null;

/** The names of what `scope` acts on. */
export function namesOf(scope: ItemScope): string[] {
    return scope.targets.map((target) => target.name);
}

/** `entry` greyed out unless `allowed`, its hint then saying why (the rights of the space). */
export function refused(entry: ItemMenuEntry, allowed: boolean, action: RefusedAction, names: string[]): ItemMenuEntry {
    return allowed ? entry : { ...entry, disabled: true, title: refusedText(action, names) };
}
