import { LibraryItem } from '../core/models';
import { FileJobKind } from './file.models';

/** Items cut or copied (Ctrl+X, Ctrl+C), pasted into a folder by Ctrl+V or the menu. */
export interface FileClipboard {
    kind: FileJobKind;
    items: LibraryItem[];
    /** Where they are: a connection's id, null for the library. */
    connection: number | null;
}

/** The tooltip of a Paste entry: "Move a.jpg into this folder", "Copy the 3 items kept into this folder". */
export function pasteTitle(clipboard: FileClipboard): string {
    const items = clipboard.items.length === 1 ? clipboard.items[0].name : `the ${clipboard.items.length} items kept`;
    return `${clipboard.kind === 'move' ? 'Move' : 'Copy'} ${items} into this folder`;
}
