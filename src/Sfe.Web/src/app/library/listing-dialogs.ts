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

/** The dialogs of the listing page (each registers itself in `Dialogs`), for its component's `imports`. */
export const LISTING_DIALOGS = [
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
];
