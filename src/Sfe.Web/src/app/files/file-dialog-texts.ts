import { formatSize } from '../core/format';
import { icu } from '../core/i18n/icu';
import { AccessGain, FileJobKind } from './file.models';

/**
 * The sentences of the dialogs that put files somewhere (delete, move and copy, batch rename, compress), as whole
 * messages marked for translation: counts are ICU plurals, the phrases placed into a sentence ("2 folders and 3
 * files", a quoted name) are messages of their own (agents/docs/development/translations.md).
 */

/** What is counted: files and folders. */
export interface Counted {
    kind: string;
}

/** A name quoted in a sentence: “holiday.jpg”. */
export function quoted(name: string): string {
    return $localize`:a name quoted in a sentence:“${name}:name:”`;
}

/** "3 files", "2 folders", "2 folders and 3 files". */
export function counted(items: readonly Counted[]): string {
    const folders = items.filter((item) => item.kind === 'directory').length;
    const files = items.length - folders;
    return icu(
        $localize`{which, select, \
folders {{folders, plural, one {# folder} other {# folders}}} \
files {{files, plural, one {# file} other {# files}}} \
other {{folders, plural, one {# folder} other {# folders}} and {files, plural, one {# file} other {# files}}}}`,
        { which: files === 0 ? 'folders' : folders === 0 ? 'files' : 'both', folders, files },
    );
}

/** "3 items". */
export function itemCount(count: number): string {
    return icu($localize`{count, plural, one {# item} other {# items}}`, { count });
}

/** The one item's name quoted, else the items counted. */
export function nameOrCount(names: readonly string[]): string {
    return names.length === 1 ? quoted(names[0]) : itemCount(names.length);
}

/** The last line of a list of names cut short: "and 3 more". */
export function moreNames(count: number): string {
    return icu($localize`:the end of a list of names cut short:{count, plural, other {and # more}}`, { count });
}

/** "2 folders and 3 files (12 MB, and what the folders hold)": what a delete takes. */
function deletedWhat(items: readonly (Counted & { size?: number | null })[]): string {
    const what = counted(items);
    const hasFolders = items.some((item) => item.kind === 'directory');
    const hasFiles = items.some((item) => item.kind !== 'directory');
    const size = formatSize(items.reduce((sum, item) => sum + (item.size ?? 0), 0));
    if (hasFiles && hasFolders) return $localize`${what}:items: (${size}:size:, and what the folders hold)`;
    if (hasFiles) return $localize`${what}:items: (${size}:size:)`;
    return icu($localize`{count, plural, one {# folder and what it holds} other {# folders and what they hold}}`, {
        count: items.length,
    });
}

/** The question of the delete dialog. `place`: the connection's name (`''` when it has none), null in the library. */
export function deleteQuestion(
    items: readonly (Counted & { size?: number | null })[],
    place: string | null,
    permanent: boolean,
): string {
    const what = deletedWhat(items);
    if (place !== null)
        return place
            ? $localize`Delete ${what}:items: from “${place}:connection:”?`
            : $localize`Delete ${what}:items: from the connection?`;
    return permanent ? $localize`Delete ${what}:items: for good?` : $localize`Move ${what}:items: to the trash?`;
}

/** Items that couldn't go to the trash, and whether they may be deleted for good instead. */
export function trashUnavailable(count: number, canForce: boolean): string {
    const why = icu(
        $localize`{count, plural, \
one {# item can't go to the trash: it holds links and is on another drive than the trash.} \
other {# items can't go to the trash: they hold links and are on another drive than the trash.}}`,
        { count },
    );
    if (!canForce) return why;
    const ask = icu(
        $localize`{count, plural, one {Delete it permanently instead?} other {Delete them permanently instead?}}`,
        { count },
    );
    return `${why} ${ask}`;
}

/** The toast of a delete: `how` the items went. */
export function deletedToast(count: number, how: 'connection' | 'permanent' | 'trash'): string {
    return icu(
        $localize`{how, select, \
connection {{count, plural, one {# item deleted.} other {# items deleted.}}} \
permanent {{count, plural, one {# item deleted for good.} other {# items deleted for good.}}} \
other {{count, plural, one {# item moved to the trash.} other {# items moved to the trash.}}}}`,
        { how, count },
    );
}

/** Several items not deleted, the first one's reason given. */
export function notDeleted(count: number, reason: string): string {
    return icu($localize`Could not delete {count, plural, one {# item} other {# items}}: {reason}`, { count, reason });
}

/** One item not deleted, with the server's reason when it gives one. */
export function notDeletedOne(name: string, reason?: string | null): string {
    return reason
        ? $localize`Could not delete ${quoted(name)}:name:: ${reason}:reason:`
        : $localize`Could not delete ${quoted(name)}:name:.`;
}

/** The hint of the Undo button of a delete to the trash. */
export function undoDeleteHint(count: number): string {
    return icu($localize`{count, plural, one {Put it back where it was.} other {Put them back where they were.}}`, {
        count,
    });
}

/** Items of the trash put back by Undo. */
export function putBack(count: number): string {
    return icu($localize`{count, plural, one {# item put back.} other {# items put back.}}`, { count });
}

/** An item of the trash Undo could not put back, with the reason when there is one. */
export function notPutBack(reason?: string | null): string {
    return reason
        ? $localize`Could not put an item back: ${reason}:reason:`
        : $localize`Could not put an item back.`;
}

/** The line above the folder picker of Move to… and Copy to…: "Move “a.jpg” into:". */
export function transferPrompt(kind: FileJobKind, what: string): string {
    return icu($localize`{kind, select, move {Move {what} into:} other {Copy {what} into:}}`, { kind, what });
}

/** The question of a paste or a drop: "Copy 3 items into “Trips”?". */
export function transferQuestion(kind: FileJobKind, what: string, target: string): string {
    return icu($localize`{kind, select, move {Move {what} into “{target}”?} other {Copy {what} into “{target}”?}}`, {
        kind,
        what,
        target,
    });
}

/** The hint of the button starting a move or a copy. */
export function transferTitle(kind: FileJobKind, target: string): string {
    return icu(
        $localize`{kind, select, \
move {Move into “{target}”. The move goes on in the background.} \
other {Copy into “{target}”. The copy goes on in the background.}}`,
        { kind, target },
    );
}

/** Who would see or change a folder moved, which they can't now. */
export function gainText(gain: AccessGain): string {
    const who = gain.isGroup ? $localize`The group “${gain.who}:group:”` : gain.who;
    return icu(
        $localize`{before, select, \
hidden {{after, select, \
write {{who} could change “{folder}”, which they don't see now.} \
other {{who} would see “{folder}”, which they don't see now.}}} \
other {{who} could change “{folder}”, where they may only look now.}}`,
        { before: gain.before, after: gain.after, who, folder: gain.folder.split('/').pop() ?? gain.folder },
    );
}

/** The folders a move gives more people access to: “Trips”, or "2 folders". */
export function gainedFolders(folders: readonly string[]): string {
    return folders.length === 1
        ? quoted(folders[0].split('/').pop() ?? folders[0])
        : icu($localize`{count, plural, one {# folder} other {# folders}}`, { count: folders.length });
}

/** The size a zip will read, from the files' sizes and the index; `unknown`: folders never scanned. */
export function sizeToRead(bytes: number, files: number, unknown: number): string {
    const values = { size: formatSize(bytes), files, unknown };
    return unknown
        ? icu(
              $localize`{files, plural, one {{size} in # file to read.} other {{size} in # files to read.}} \
{unknown, plural, one {# folder isn't in the index and isn't counted.} \
other {# folders aren't in the index and aren't counted.}}`,
              values,
          )
        : icu($localize`{files, plural, one {{size} in # file to read.} other {{size} in # files to read.}}`, values);
}

/** The subtitle of the batch rename dialog. */
export function selectedCount(count: number): string {
    return icu($localize`{count, plural, one {# item selected} other {# items selected}}`, { count });
}

/** Under the batch rename preview: the items whose new name can't be given. */
export function leftOut(count: number): string {
    return count ? icu($localize`{count, plural, one {# item left out.} other {# items left out.}}`, { count }) : '';
}

/** The toast of a batch rename. */
export function renamedToast(count: number): string {
    return icu($localize`{count, plural, one {# item renamed.} other {# items renamed.}}`, { count });
}

/** The hint of the Undo button of a batch rename. */
export function undoRenameHint(count: number): string {
    return icu($localize`{count, plural, one {Give it back its name.} other {Give them back their names.}}`, { count });
}

/** The undo of a rename, when the old name couldn't be given back; with the reason when there is one. */
export function nameNotBack(name: string, reason?: string | null): string {
    return reason
        ? $localize`Could not give back the name ${quoted(name)}:name:: ${reason}:reason:`
        : $localize`Could not give back the name ${quoted(name)}:name:.`;
}

/** The undo of a batch rename, when some names couldn't be given back. */
export function namesNotBack(count: number): string {
    return icu(
        $localize`{count, plural, one {Could not give # item its name back.} \
other {Could not give # items their names back.}}`,
        { count },
    );
}
