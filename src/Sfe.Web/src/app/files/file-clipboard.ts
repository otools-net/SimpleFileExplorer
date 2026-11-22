import { icu } from '../core/i18n/icu';
import { LibraryItem } from '../core/models';
import { FileJobKind } from './file.models';

/** Items cut or copied (Ctrl+X, Ctrl+C), pasted into a folder by Ctrl+V or the menu. */
export interface FileClipboard {
    kind: FileJobKind;
    items: LibraryItem[];
    /** Where they are: a connection's id, null for the library. */
    connection: number | null;
}

/**
 * The hint of a Paste entry: "Move “a.jpg” into this folder.", "Copy the 3 items kept into “Trips”." `into`: the
 * folder's name when it isn't the one listed.
 */
export function pasteTitle(clipboard: FileClipboard, into: string | null = null): string {
    const values = {
        count: clipboard.items.length,
        name: clipboard.items[0]?.name ?? '',
        here: into === null ? 'yes' : 'no',
        into: into ?? '',
    };
    return clipboard.kind === 'move'
        ? icu(
              $localize`{here, select, \
yes {{count, plural, one {Move “{name}” into this folder.} other {Move the # items kept into this folder.}}} \
other {{count, plural, one {Move “{name}” into “{into}”.} other {Move the # items kept into “{into}”.}}}}`,
              values,
          )
        : icu(
              $localize`{here, select, \
yes {{count, plural, one {Copy “{name}” into this folder.} other {Copy the # items kept into this folder.}}} \
other {{count, plural, one {Copy “{name}” into “{into}”.} other {Copy the # items kept into “{into}”.}}}}`,
              values,
          );
}
