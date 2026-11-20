import type { SettingTab } from '../settings-schema';

/**
 * The Picture editor and File management tabs of the settings.
 */

export const EDITOR_SETTINGS: SettingTab = {
    id: 'editor',
    title: $localize`Picture editor`,
    intro: $localize`The Edit button of the viewer crops pictures. The result is saved as a new file, or over the \
original by those allowed to replace files (its previous version is kept in the trash).`,
    groups: [
        {
            title: $localize`Saving`,
            fields: [
                {
                    key: 'editor.outputFolder',
                    label: $localize`Folder for edited pictures`,
                    type: 'text',
                    placeholder: $localize`:folder name:Edited`,
                    hint: $localize`A folder of the library (a path from its top) where edited pictures are saved, \
in the same folders as their originals. Empty: next to the original.`,
                    help: $localize`With Edited, Photos/2024/beach.jpg is saved as Edited/Photos/2024/beach.jpg; \
the folder is created when first needed. Empty, it is saved as “beach (edited).jpg” next to the original. Existing \
files are never replaced: a number is added to the name.`,
                },
                {
                    key: 'editor.quality',
                    label: $localize`Quality (JPEG and WebP)`,
                    type: 'number',
                    min: 10,
                    max: 100,
                    hint: $localize`How much detail JPEG and WebP pictures keep when saved: higher is sharper and \
larger.`,
                    help: $localize`PNG pictures are saved without loss; other formats are saved as PNG.`,
                },
            ],
        },
    ],
};

export const FILE_SETTINGS: SettingTab = {
    id: 'files',
    title: $localize`File management`,
    intro: $localize`Creating folders, renaming, moving, copying, uploading and deleting files and folders from the \
listing, for the groups given those rights (Spaces & accounts).`,
    groups: [
        {
            title: $localize`Deleting`,
            fields: [
                {
                    key: 'files.deletePermanently',
                    label: $localize`Always delete permanently`,
                    type: 'checkbox',
                    hint: $localize`Every delete skips the trash: files are gone at once, and only those who may \
delete permanently can delete.`,
                    help: $localize`What is already in the trash stays there.`,
                },
                {
                    key: 'files.trash.retentionDays',
                    label: $localize`Days in the trash`,
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint: $localize`Items are deleted for good after this many days in the trash. 0 keeps them \
until the trash is emptied.`,
                    help: $localize`A background task does it: Background tasks › Trash purge.`,
                },
            ],
        },
        {
            title: $localize`Copies, uploads and downloads`,
            fields: [
                {
                    key: 'files.minimumFreeSpaceMegabytes',
                    label: $localize`Free space kept by copies and uploads (MB)`,
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint: $localize`A copy or an upload is refused when it would leave less free space than this on \
the drive it goes to. 0: they may fill the drive.`,
                },
                {
                    key: 'files.uploads.maxFileSizeMegabytes',
                    label: $localize`Largest file to upload (MB)`,
                    type: 'number',
                    min: 0,
                    max: 1073741823,
                    hint: $localize`Larger files are refused before they are sent. 0: no limit (only the free space \
kept counts).`,
                },
                {
                    key: 'files.uploads.keepUnfinishedHours',
                    label: $localize`Keep unfinished uploads for (hours)`,
                    type: 'number',
                    min: 1,
                    max: 8760,
                    hint: $localize`An upload that receives nothing for this long is removed, with what it \
received.`,
                    help: $localize`Until then, picking the same file again carries on where it stopped.`,
                },
                {
                    key: 'files.maxArchiveMegabytes',
                    label: $localize`Largest zip to download (MB)`,
                    type: 'number',
                    min: 0,
                    max: 1073741823,
                    hint: $localize`Downloading several files or a folder makes one zip; a larger one is refused \
before it starts. 0: no limit.`,
                    help: $localize`The size is what the index knows of the files: folders not in the index count \
for nothing.`,
                },
            ],
        },
        {
            title: $localize`Names`,
            fields: [
                {
                    key: 'files.portableNames',
                    label: $localize`Only names Windows accepts`,
                    type: 'checkbox',
                    hint: $localize`New names must also work on Windows. Keep it on when the library is shared with \
Windows computers or may move to one.`,
                    help: $localize`That means no < > : " \\ | ? *, no name ending with a dot or a space, and no \
CON, NUL, COM1… A Windows server always refuses them.`,
                },
            ],
        },
        {
            title: $localize`Work folder`,
            fields: [
                {
                    key: 'files.workFolder',
                    label: $localize`Where SFE keeps its work files`,
                    type: 'select',
                    options: [
                        ['library', $localize`In the library`],
                        ['server', $localize`On the server`],
                    ],
                    hint: $localize`Where the trash and unfinished uploads are kept.`,
                    help: $localize`In the library (a hidden .sfe folder at its top), they stay on the library's \
drive, so moving a file there is instant. In the server's data folder, the library holds nothing of SFE's, but \
large files are copied from one drive to the other. The name .sfe at the top of the library stays reserved either \
way.`,
                },
            ],
        },
    ],
};
