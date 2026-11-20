import type { SettingTab } from '../settings-schema';

/**
 * The tabs of the Scanning section of the settings: what scans skip, file types and what scans read.
 */

export const SKIPPED_SETTINGS: SettingTab = {
    id: 'skipped',
    title: 'What to scan',
    groups: [
        {
            title: 'Skipped by scans',
            fields: [
                {
                    key: 'scan.ignoredDirectories',
                    label: 'Ignored folders',
                    type: 'list',
                    placeholder: '$RECYCLE.BIN',
                    hint:
                        'Folder names (not paths) that scans skip, with everything inside them. One per line. ' +
                        'Already indexed files in them stay until the next scan of their parent folder.',
                },
                {
                    key: 'scan.ignoredFiles',
                    label: 'Ignored files',
                    type: 'list',
                    placeholder: 'Thumbs.db',
                    hint: 'File names that scans skip. One per line.',
                },
                {
                    key: 'scan.excludedFolders',
                    label: 'Excluded folders',
                    type: 'list',
                    placeholder: 'Downloads\nMovies/Old',
                    hint:
                        'Library folders (paths from the library root, one per line) that scans leave out, ' +
                        'with their sub-folders. What the index already has of them stays as it is, ' +
                        'never refreshed. Scans and previews… in the menu of a folder, ' +
                        'or Scanning › Excluded folders, adds and removes them; ⋮ › Remove from index forgets a ' +
                        'folder and can add it here too.',
                },
            ],
        },
        {
            title: 'Hidden items',
            fields: [
                {
                    key: 'showHiddenItems',
                    label: 'Show hidden files and folders',
                    type: 'checkbox',
                    hint:
                        'Includes items marked hidden or system (on Linux and macOS, ' +
                        'names starting with a dot), in listings and in scans.',
                },
            ],
        },
    ],
};

export const FILE_TYPE_SETTINGS: SettingTab = {
    id: 'fileTypes',
    title: 'File types',
    groups: [
        {
            title: 'File types',
            key: 'fileTypes',
            hint: 'Which files are photos, videos, audio and text, by their extension.',
            fields: [
                {
                    key: 'fileTypes.images',
                    label: 'Photos',
                    type: 'extensions',
                    hint: 'Extensions opened in the photo viewer and given thumbnails, separated by commas.',
                },
                {
                    key: 'fileTypes.videos',
                    label: 'Videos',
                    type: 'extensions',
                    hint: 'Extensions played in the video player and given thumbnails and trickplay.',
                },
                {
                    key: 'fileTypes.audio',
                    label: 'Audio',
                    type: 'extensions',
                    hint: 'Extensions played in the audio player. Other extensions open in a new tab.',
                },
                {
                    key: 'fileTypes.text',
                    label: 'More text files',
                    type: 'extensions',
                    hint:
                        'Extensions opened in the text viewer besides the ones it knows (.txt, .log, .md, ' +
                        '.json, .xml, code and scripts…), e.g. dat, out, sub. Not media extensions.',
                },
            ],
        },
    ],
};

export const INDEXING_SETTINGS: SettingTab = {
    id: 'indexing',
    title: 'Indexing',
    groups: [
        {
            title: 'Read from files',
            fields: [
                {
                    key: 'scan.computeHashes',
                    label: 'Fingerprint files',
                    type: 'checkbox',
                    hint:
                        'Reads the start of each new or changed file to compute a fingerprint, ' +
                        'used to find duplicates and to recognise moved files. Makes scans slower, ' +
                        'especially over the network.',
                },
                {
                    key: 'scan.readResolution',
                    label: 'Read resolutions and durations',
                    type: 'checkbox',
                    hint:
                        'Reads the size in pixels of each new or changed photo and video, ' +
                        'and how long each video and audio file lasts, to show them and to search by them (w:, ' +
                        'h:, r:, d:). Videos and audio need ffmpeg, which makes scans of new ones slower. ' +
                        'Files already indexed are read by the next scan.',
                },
            ],
        },
        {
            title: 'Performance',
            fields: [
                {
                    key: 'scan.parallelism',
                    label: 'Folders scanned at once',
                    type: 'number',
                    min: 1,
                    max: 64,
                    hint: 'Higher is faster on SSDs and fast network drives, lower is gentler on spinning disks.',
                },
            ],
        },
    ],
};
