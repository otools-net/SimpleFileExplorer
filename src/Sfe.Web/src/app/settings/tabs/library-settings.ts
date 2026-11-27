import type { SettingTab } from '../settings-schema';

/**
 * The Duplicates & moves and Tags tabs of the settings.
 */

export const TRACKING_SETTINGS: SettingTab = {
    id: 'tracking',
    title: $localize`Duplicates & moves`,
    intro: $localize`Find identical files in the library, and keep the tags of files that are moved, renamed, or \
whose drive was missing during a scan. Both work from the index: scan folders to keep it up to date.`,
    groups: [
        {
            title: $localize`Duplicates`,
            hint: $localize`Files with the same size and fingerprint are taken as identical. This needs \
fingerprints (Scanning › Indexing › Fingerprint files).`,
            fields: [
                {
                    key: 'fileTracking.duplicates.minimumKilobytes',
                    label: $localize`Smallest file (KB)`,
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint: $localize`Smaller files are left out, as libraries hold many identical tiny files. \
0 compares every file.`,
                },
                {
                    key: 'fileTracking.duplicates.groupsPerPage',
                    label: $localize`Groups per page`,
                    type: 'number',
                    min: 1,
                    max: 500,
                    hint: $localize`Groups of identical files loaded at a time; “Load more” shows the next ones.`,
                },
                {
                    key: 'fileTracking.duplicates.allowRemove',
                    label: $localize`Allow deleting copies`,
                    type: 'checkbox',
                    hint: $localize`Adds a Remove button to each copy: the file goes to the trash and its tags are \
added to a copy you keep.`,
                    help: $localize`The two files are compared byte for byte first, so an identical copy always \
stays. Only groups with the right “Remove duplicates” can remove copies; with “Always delete permanently” (File \
management), they are deleted for good.`,
                },
            ],
        },
        {
            title: $localize`Similar videos`,
            hint: $localize`Videos showing the same pictures in other files (another resolution, quality or format, \
trimmed) are found from fingerprints made with their trickplay: turn trickplay on for the folders to compare.`,
            fields: [
                {
                    key: 'fileTracking.similarVideos.minimumPercent',
                    label: $localize`Smallest match (%)`,
                    type: 'number',
                    min: 50,
                    max: 100,
                    hint: $localize`The share of frames two videos must have in common, out of the longer one. \
Applies from the next comparison.`,
                    help: $localize`A copy at another resolution is near 100%, and a copy cut by a tenth near \
90%.`,
                },
            ],
        },
        {
            title: $localize`Moved and renamed files`,
            fields: [
                {
                    key: 'fileTracking.relink.enabled',
                    label: $localize`Keep the tags of moved files`,
                    type: 'checkbox',
                    hint: $localize`A tagged file that a scan no longer finds is remembered with its tags, which \
move over when it is found again. Off: it loses its tags.`,
                    help: $localize`The file is found again when a scan indexes the same file somewhere else: \
moved, renamed, or back on its drive.`,
                },
                {
                    key: 'fileTracking.relink.matchByName',
                    label: $localize`Recognise files without a fingerprint by name`,
                    type: 'checkbox',
                    hint: $localize`Files indexed without a fingerprint are matched on their name, size and date \
modified: a move is recognised, a rename is not.`,
                },
                {
                    key: 'fileTracking.relink.retentionDays',
                    label: $localize`Remember missing files for (days)`,
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint: $localize`Missing files not found again in that time are forgotten, with their tags. \
0: until found or forgotten by hand.`,
                },
            ],
        },
    ],
};

export const TAG_SETTINGS: SettingTab = {
    id: 'tags',
    title: $localize`Tags`,
    groups: [
        {
            title: $localize`Sidebar`,
            fields: [
                {
                    key: 'tags.sidebarCount',
                    label: $localize`Tags per sidebar section`,
                    type: 'number',
                    min: 1,
                    max: 20,
                    hint: $localize`How many tags each section of the sidebar shows.`,
                },
                {
                    key: 'tags.sidebarPopular',
                    label: $localize`Show “Most popular”`,
                    type: 'checkbox',
                    hint: $localize`Lists the tags on the most files in the sidebar.`,
                },
                {
                    key: 'tags.sidebarSearched',
                    label: $localize`Show “Last searched”`,
                    type: 'checkbox',
                    hint: $localize`Lists the tags you last searched for in the sidebar, while tag history is on.`,
                },
                {
                    key: 'tags.sidebarLucky',
                    label: $localize`Show “I feel lucky”`,
                    type: 'checkbox',
                    hint: $localize`Lists random tags that the other sections don't show, for a surprise.`,
                },
            ],
        },
        {
            title: $localize`History`,
            action: 'clearHistory',
            fields: [
                {
                    key: 'tags.recordHistory',
                    label: $localize`Remember tag history`,
                    type: 'checkbox',
                    hint: $localize`Records the tags you search for and the tags of the files you open, for the \
sidebar's “Last searched” and “Last viewed” lists.`,
                    help: $localize`Off: nothing new is recorded, and those lists are hidden. “Last searched” shows \
only when switched on under Sidebar; searches are recorded while it is hidden, so its list is ready then.`,
                },
            ],
        },
    ],
};
