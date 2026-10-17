import { DestroyRef, inject, Injectable, signal, WritableSignal } from '@angular/core';
import type { ImageEditorDialog } from '../editor/image-editor.dialog';
import type { DeleteDialog } from '../files/delete.dialog';
import type { NameDialog } from '../files/name.dialog';
import type { PropertiesDialog } from '../files/properties.dialog';
import type { TransferDialog } from '../files/transfer.dialog';
import type { FileTagsDialog } from '../tags/file-tags.dialog';
import type { FilesTagsDialog } from '../tags/files-tags.dialog';
import type { ScopeTagsDialog } from '../tags/scope-tags.dialog';
import type { TagEditorDialog } from '../tags/tag-editor.dialog';
import type { TagManagerDialog } from '../tags/tag-manager.dialog';
import type { ViewerComponent } from './viewer/viewer.component';
import type { RemoveFromIndexDialog } from './scan/remove-from-index.dialog';
import type { SearchHelpDialog } from './topbar/search-help.dialog';

/**
 * The dialogs of the listing page, which register themselves here when they are created, so that any part of the
 * page can open one: `dialogs.tagManager()?.open()`.
 */
@Injectable({ providedIn: 'root' })
export class Dialogs {
    readonly viewer = signal<ViewerComponent | null>(null);
    readonly scopeTags = signal<ScopeTagsDialog | null>(null);
    readonly fileTags = signal<FileTagsDialog | null>(null);
    readonly filesTags = signal<FilesTagsDialog | null>(null);
    readonly tagEditor = signal<TagEditorDialog | null>(null);
    readonly tagManager = signal<TagManagerDialog | null>(null);
    readonly removeFromIndex = signal<RemoveFromIndexDialog | null>(null);
    readonly imageEditor = signal<ImageEditorDialog | null>(null);
    readonly searchHelp = signal<SearchHelpDialog | null>(null);
    /** New folder and Rename. */
    readonly name = signal<NameDialog | null>(null);
    readonly delete = signal<DeleteDialog | null>(null);
    /** Move to… and Copy to…, and the confirmation of a paste or a drop. */
    readonly transfer = signal<TransferDialog | null>(null);
    readonly properties = signal<PropertiesDialog | null>(null);

    /** Closes the open dialogs, e.g. before searching for a tag picked in one. */
    closeAll(): void {
        for (const dialog of document.querySelectorAll('dialog[open]'))
            (dialog as HTMLDialogElement).close();
    }
}

/** Registers `dialog` in its `slot` of Dialogs for as long as it exists; call from its constructor. */
export function registerDialog<T>(slot: (dialogs: Dialogs) => WritableSignal<T | null>, dialog: T): void {
    const target = slot(inject(Dialogs));
    target.set(dialog);
    inject(DestroyRef).onDestroy(() => {
        if (target() === dialog)
            target.set(null);
    });
}
