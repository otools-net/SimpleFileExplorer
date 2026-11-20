import type { SettingTab } from '../settings-schema';

/**
 * The Picture editor and File management tabs of the settings.
 */

export const EDITOR_SETTINGS: SettingTab = {
    id: 'editor',
    title: 'Picture editor',
    intro:
        'The Edit button of the viewer crops pictures. The result is always saved as a new file: ' +
        'originals are never changed.',
    groups: [
        {
            title: 'Saving',
            fields: [
                {
                    key: 'editor.outputFolder',
                    label: 'Folder for edited pictures',
                    type: 'text',
                    placeholder: 'Edited',
                    hint:
                        'A folder of the library (path from the library root) where edited pictures are saved, ' +
                        'in the same sub-folders as their originals: Photos/2024/beach.jpg is saved as ' +
                        'Edited/Photos/2024/beach.jpg. Created when first needed. Empty: next to the original, ' +
                        'as “beach (edited).jpg”. Existing files are never replaced: ' +
                        'a number is added to the name.',
                },
                {
                    key: 'editor.quality',
                    label: 'Quality (JPEG and WebP)',
                    type: 'number',
                    min: 10,
                    max: 100,
                    hint:
                        'How much detail JPEG and WebP pictures keep when saved: higher is sharper and larger. ' +
                        'PNG pictures are saved without loss; other formats are saved as PNG.',
                },
            ],
        },
    ],
};

export const FILE_SETTINGS: SettingTab = {
    id: 'files',
    title: 'File management',
    intro:
        'Creating folders, renaming, moving, copying, uploading and deleting files and folders from the ' +
        'listing, for the groups given those rights (Spaces & accounts).',
    groups: [
        {
            title: 'Deleting',
            fields: [
                {
                    key: 'files.deletePermanently',
                    label: 'Always delete permanently',
                    type: 'checkbox',
                    hint:
                        'Every delete skips the trash: files are gone at once, ' +
                        'and only those who may delete permanently can delete. ' +
                        'What is already in the trash stays there.',
                },
                {
                    key: 'files.trash.retentionDays',
                    label: 'Days in the trash',
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint:
                        'What was deleted is deleted for good after this many days in the trash (a background ' +
                        'task, Background tasks › Trash purge); 0 keeps it until the trash is emptied.',
                },
            ],
        },
        {
            title: 'Copies, uploads and downloads',
            fields: [
                {
                    key: 'files.minimumFreeSpaceMegabytes',
                    label: 'Free space kept by copies and uploads (MB)',
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint:
                        'A copy or an upload is refused when it would leave less free space than this on the ' +
                        'drive it goes to. 0: they may fill the drive.',
                },
                {
                    key: 'files.uploads.maxFileSizeMegabytes',
                    label: 'Largest file to upload (MB)',
                    type: 'number',
                    min: 0,
                    max: 1073741823,
                    hint:
                        'Larger files are refused before they are sent. 0: no limit (only the free space kept ' +
                        'counts).',
                },
                {
                    key: 'files.uploads.keepUnfinishedHours',
                    label: 'Keep unfinished uploads for (hours)',
                    type: 'number',
                    min: 1,
                    max: 8760,
                    hint:
                        'An upload that receives nothing for this long is removed with what it received. ' +
                        'Until then, picking the same file again goes on where it stopped.',
                },
                {
                    key: 'files.maxArchiveMegabytes',
                    label: 'Largest zip to download (MB)',
                    type: 'number',
                    min: 0,
                    max: 1073741823,
                    hint:
                        'Downloading several files or a folder makes one zip. A larger one is refused before ' +
                        'it starts, as far as the index knows the files (folders not indexed count nothing). 0: ' +
                        'no limit.',
                },
            ],
        },
        {
            title: 'Names',
            fields: [
                {
                    key: 'files.portableNames',
                    label: 'Only names Windows accepts',
                    type: 'checkbox',
                    hint:
                        'New names must also work on Windows: no < > : " \\ | ? *, ' +
                        'no name ending with a dot or a space, no CON, NUL, COM1… Keep it on when the library is ' +
                        'shared with Windows computers or may move to one. A Windows server always refuses them.',
                },
            ],
        },
        {
            title: 'Work folder',
            fields: [
                {
                    key: 'files.workFolder',
                    label: 'Where SFE keeps its work files',
                    type: 'select',
                    options: [
                        ['library', 'In the library'],
                        ['server', 'On the server'],
                    ],
                    hint:
                        'For the trash and unfinished uploads. In the library (a hidden .sfe folder at its ' +
                        'root), they stay on its own drive, so moving a file there is instant; ' +
                        "in the server's data folder, the library holds nothing of SFE's, " +
                        "but large files are copied from one drive to the other. The name .sfe at the library's " +
                        'root stays reserved either way.',
                },
            ],
        },
    ],
};
