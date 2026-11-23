import { icu } from '../core/i18n/icu';
import { quoted } from './file-dialog-texts';

/**
 * The sentences of the item commands that choose between messages (agents/docs/development/translations.md): why an
 * entry is greyed out, said in its hint (and under it on phones).
 */

/** What a greyed out entry would have done: the verb of its reason. */
export type RefusedAction =
    'view' | 'edit' | 'move' | 'copy' | 'paste' | 'rename' | 'tags' | 'extract' | 'compress' | 'download' | 'delete';

/** "the selected items", or the one item's name quoted. */
function what(names: readonly string[]): string {
    return names.length === 1 ? quoted(names[0]) : $localize`:what a greyed out action is about:the selected items`;
}

/** Why an entry is greyed out: the rights of this space don't let one do it to `names` (or, pasting, into them). */
export function refusedText(action: RefusedAction, names: readonly string[]): string {
    return icu(
        $localize`:the reason a menu entry is greyed out:{action, select, \
view {You may not open {what} here.} \
edit {You may not change {what}.} \
move {You may not move {what}.} \
copy {You may not copy {what}.} \
paste {You may not put anything into {what}.} \
rename {You may not rename {what}.} \
tags {You may not change the tags of {what}.} \
extract {You may not extract {what}.} \
compress {You may not put {what} in a zip.} \
download {You may not download {what}.} \
delete {You may not delete {what}.} \
other {You may not do this here.}}`,
        { action, what: what(names) },
    );
}
