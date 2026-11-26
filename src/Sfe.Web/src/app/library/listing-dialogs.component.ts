import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ImageEditorDialog } from '../editor/image-editor.dialog';
import { BatchRenameDialog } from '../files/batch-rename.dialog';
import { CompressDialog } from '../files/compress.dialog';
import { DeleteDialog } from '../files/delete.dialog';
import { ExtractDialog } from '../files/extract.dialog';
import { NameDialog } from '../files/name.dialog';
import { PropertiesDialog } from '../files/properties.dialog';
import { TransferDialog } from '../files/transfer.dialog';
import { VersionsDialog } from '../files/versions.dialog';
import { FileTagsDialog } from '../tags/file-tags.dialog';
import { FilesTagsDialog } from '../tags/files-tags.dialog';
import { ScopeTagsDialog } from '../tags/scope-tags.dialog';
import { TagEditorDialog } from '../tags/tag-editor.dialog';
import { TagManagerDialog } from '../tags/tag-manager.dialog';
import { FolderExclusionsDialog } from './scan/folder-exclusions.dialog';
import { RemoveFromIndexDialog } from './scan/remove-from-index.dialog';

/**
 * The dialogs of the listing page (each registers itself in `Dialogs`). The page shows them in a `@defer` block, so
 * that their code is a chunk of its own, loaded right after the page is shown rather than with the app: callers open
 * them with `dialogs.name()?.…`, and menus offer them once registered. The viewer is not one of them: a click on a
 * file must find it at once.
 */
@Component({
    selector: 'sfe-listing-dialogs',
    imports: [
        FileTagsDialog,
        FilesTagsDialog,
        ScopeTagsDialog,
        TagEditorDialog,
        TagManagerDialog,
        RemoveFromIndexDialog,
        FolderExclusionsDialog,
        ImageEditorDialog,
        NameDialog,
        DeleteDialog,
        TransferDialog,
        ExtractDialog,
        CompressDialog,
        PropertiesDialog,
        VersionsDialog,
        BatchRenameDialog,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-file-tags />
        <sfe-files-tags />
        <sfe-scope-tags />
        <sfe-tag-editor />
        <sfe-tag-manager />
        <sfe-remove-from-index />
        <sfe-folder-exclusions />
        <sfe-name-dialog />
        <sfe-delete-dialog />
        <sfe-transfer-dialog />
        <sfe-extract-dialog />
        <sfe-compress-dialog />
        <sfe-properties-dialog />
        <sfe-versions-dialog />
        <sfe-batch-rename-dialog />
        <!-- Shown after the viewer (this component follows it): it opens over it. -->
        <sfe-image-editor />
    `,
})
export class ListingDialogsComponent {}
