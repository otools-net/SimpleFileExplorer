import { formatDate, formatSize, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { TrashItem, TrashSize } from './file.models';

/**
 * The sentences of Administration › File management › Trash, as whole messages marked for translation
 * (agents/docs/development/translations.md).
 */

/** The hint of Restore (its button and its menu entry). */
export function restoreHint(): string {
    return $localize`Put it back where it was. If its name is taken there, a number is added to it.`;
}

/** The hint of Delete for good (its button and its menu entry). */
export function purgeHint(): string {
    return $localize`Delete it for good (asks first).`;
}

/** What the page says first: what is listed, what restoring does, how long things stay (null: not known yet). */
export function trashIntro(retentionDays: number | null): string {
    const listed = $localize`You see what you deleted, and what was deleted in folders where you may delete. \
Restoring puts an item back where it was, with its tags and folder access.`;
    if (retentionDays === null) return listed;
    const keeps =
        retentionDays === 0
            ? $localize`The trash keeps them until they are restored or deleted for good.`
            : icu(
                  $localize`{days, plural, one {Each is deleted for good # day after it was deleted.} \
other {Each is deleted for good # days after it was deleted.}}`,
                  { days: retentionDays },
              );
    return `${listed} ${keeps}`;
}

/** What the whole trash holds, as the index knew its entries when they were deleted. */
export function trashSizeText(size: TrashSize): string {
    const bytes = formatSize(size.bytes);
    if (!size.unknown) return $localize`The trash holds ${bytes}:size:.`;
    return icu(
        $localize`The trash holds ${bytes}:size:, not counting {count, plural, one {# item} other {# items}} whose \
size isn't known.`,
        { count: size.unknown },
    );
}

/** "3 files · 2 KB · Deleted 22 Nov 2026 by Sam.": what an entry holds, then when and by whom. */
export function describeTrashed(item: TrashItem): string {
    const date = formatDate(item.deletedAt);
    const who = item.deletedBy;
    const when = item.previousVersion
        ? who
            ? $localize`Saved over ${date}:date: by ${who}:account:.`
            : $localize`Saved over ${date}:date:.`
        : who
          ? $localize`Deleted ${date}:date: by ${who}:account:.`
          : $localize`Deleted ${date}:date:.`;
    return joinParts([
        item.isFolder && item.files !== null
            ? icu($localize`{count, plural, one {# file} other {# files}}`, { count: item.files })
            : null,
        item.size !== null ? formatSize(item.size) : null,
        when,
    ]);
}

/** The hint of the link to the folder an entry was deleted from. */
export function openFolderHint(folder: string): string {
    return folder ? $localize`Open “${folder}:folder:”, where it was.` : $localize`Open the top of the library.`;
}

/** The hint of the menu entry opening that folder in a new tab. */
export function openFolderInTabHint(folder: string): string {
    return folder
        ? $localize`Open “${folder}:folder:”, where it was, in a new tab.`
        : $localize`Open the top of the library in a new tab.`;
}

/** The toast after Restore: the item back where it was, or under another name (`path`) when its name was taken. */
export function restoredText(name: string, path: string | null): string {
    return path ? $localize`“${name}:name:” restored as “${path}:path:”.` : $localize`“${name}:name:” restored.`;
}

/** Restore refused or failed, with the reason when there is one. */
export function notRestored(name: string, reason?: string | null): string {
    return reason
        ? $localize`Could not restore “${name}:name:”: ${reason}:reason:`
        : $localize`Could not restore “${name}:name:”.`;
}

/** The question before deleting one entry for good. */
export function purgeQuestion(name: string): string {
    return $localize`Delete “${name}:name:” for good? This can't be undone.`;
}

/** The question before emptying the trash. */
export function emptyQuestion(): string {
    return $localize`Delete for good everything in the trash that you may delete? This can't be undone.`;
}

/** The toast after emptying the trash. */
export function purgedText(count: number): string {
    return icu($localize`{count, plural, one {# item deleted for good.} other {# items deleted for good.}}`, { count });
}
