import type { SettingTab } from '../settings-schema';

/**
 * The Duplicates & moves and Tags tabs of the settings.
 */

export const TRACKING_SETTINGS: SettingTab = {
    id: 'tracking',
    title: 'Duplicates & moves',
    intro:
        'Find identical files in the library, and keep the tags of files that are moved, renamed, ' +
        'or whose drive was missing during a scan. Both work from the index: scan folders to keep it current.',
    groups: [
        {
            title: 'Duplicates',
            hint:
                'Files with the same size and fingerprint are taken as identical. ' +
                'Needs fingerprints (Scanning › Indexing › Fingerprint files).',
            fields: [
                {
                    key: 'fileTracking.duplicates.minimumKilobytes',
                    label: 'Smallest file (KB)',
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint:
                        'Smaller files are left out: libraries hold many identical tiny files. ' +
                        '0 compares every file.',
                },
                {
                    key: 'fileTracking.duplicates.groupsPerPage',
                    label: 'Groups per page',
                    type: 'number',
                    min: 1,
                    max: 500,
                    hint: 'Groups of identical files loaded at a time; “Load more” shows the next ones.',
                },
                {
                    key: 'fileTracking.duplicates.allowRemove',
                    label: 'Allow deleting copies',
                    type: 'checkbox',
                    hint:
                        'Adds a Remove button to each copy: the file is deleted from the disk and its tags are ' +
                        'added to a copy you keep. The two files are compared byte for byte first, ' +
                        'so an identical copy always stays. Anyone who can open SFE can use it.',
                },
            ],
        },
        {
            title: 'Similar videos',
            hint:
                'Videos showing the same pictures in other files (another resolution, quality or format, ' +
                'trimmed) are found from fingerprints made with their trickplay: ' +
                'turn trickplay on for the folders to compare.',
            fields: [
                {
                    key: 'fileTracking.similarVideos.minimumPercent',
                    label: 'Smallest match (%)',
                    type: 'number',
                    min: 50,
                    max: 100,
                    hint:
                        'Share of frames two videos must have in common, out of the longer one. ' +
                        'A copy at another resolution is near 100%, a copy cut by a tenth near 90%. ' +
                        'Applies from the next comparison.',
                },
            ],
        },
        {
            title: 'Moved and renamed files',
            fields: [
                {
                    key: 'fileTracking.relink.enabled',
                    label: 'Keep the tags of moved files',
                    type: 'checkbox',
                    hint:
                        'A tagged file that a scan no longer finds is remembered with its tags. ' +
                        'When a scan indexes the same file somewhere else (moved, renamed, ' +
                        'or back on its drive), the tags move over to it. When off, ' +
                        'a file no longer found loses its tags.',
                },
                {
                    key: 'fileTracking.relink.matchByName',
                    label: 'Recognise files without a fingerprint by name',
                    type: 'checkbox',
                    hint:
                        'Files indexed without a fingerprint are matched on their name, ' +
                        'size and modification date: a move is recognised, a rename is not.',
                },
                {
                    key: 'fileTracking.relink.retentionDays',
                    label: 'Remember missing files for (days)',
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint:
                        'Missing files not found again in that time are forgotten, with their tags. 0: ' +
                        'until found or forgotten by hand.',
                },
            ],
        },
    ],
};

export const TAG_SETTINGS: SettingTab = {
    id: 'tags',
    title: 'Tags',
    groups: [
        {
            title: 'Sidebar',
            fields: [
                {
                    key: 'tags.sidebarCount',
                    label: 'Tags per sidebar section',
                    type: 'number',
                    min: 1,
                    max: 20,
                    hint: 'How many tags each sidebar section shows.',
                },
            ],
        },
        {
            title: 'History',
            action: 'clearHistory',
            fields: [
                {
                    key: 'tags.recordHistory',
                    label: 'Remember tag history',
                    type: 'checkbox',
                    hint:
                        'Records the tags you search for and the tags of the files you open, ' +
                        "for the sidebar's “Last searched” and “Last viewed” lists. When off, " +
                        'nothing new is recorded and those lists are hidden.',
                },
            ],
        },
    ],
};
