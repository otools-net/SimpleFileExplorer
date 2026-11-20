import type { SettingTab } from '../settings-schema';

/**
 * The Browsing and Search tabs of the settings (Interface): listings, resolutions and durations, results and
 * the search box.
 */

export const BROWSING_SETTINGS: SettingTab = {
    id: 'browsing',
    title: 'Browsing',
    groups: [
        {
            title: 'Listings',
            fields: [
                {
                    key: 'browse.defaultPageSize',
                    label: 'Items per page',
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint:
                        'Files and folders loaded at a time; more load as you scroll. ' +
                        'Smaller pages show up faster on slow drives.',
                },
                {
                    key: 'browse.maxPageSize',
                    label: 'Largest page',
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint:
                        'Most items a single request can ask for. Only matters for scripts and other programs ' +
                        'using the API.',
                },
                {
                    key: 'browse.folderPictures',
                    label: 'Pictures per folder',
                    type: 'number',
                    min: 0,
                    max: 16,
                    hint:
                        'In the grid and one-per-row views, folders are cards showing their first photos and ' +
                        'videos: 4 makes a 2×2 grid, 9 a 3×3 grid. 0 lists folders without pictures.',
                },
                {
                    key: 'browse.copyPaths',
                    label: 'Copy full paths',
                    type: 'checkbox',
                    hint:
                        'Adds buttons copying the full path of a folder or file on the server (e.g. ' +
                        'D:\\Media\\Photos\\beach.jpg): next to the location for the current folder, ' +
                        'on each folder and file, and in the viewer. This shows every visitor where the library ' +
                        "is on the server's disk.",
                },
            ],
        },
        {
            title: 'Resolution and duration',
            hint:
                'Needs the resolutions and durations read by scans (Scanning › Indexing › Read resolutions and ' +
                'durations).',
            fields: [
                {
                    key: 'browse.showResolution',
                    label: 'Show resolutions',
                    type: 'checkbox',
                    hint:
                        'Shows the size in pixels of photos and videos (e.g. 1920×1080) ' +
                        'next to their file size, in the listings and the viewer.',
                },
                {
                    key: 'browse.showDuration',
                    label: 'Show durations',
                    type: 'checkbox',
                    hint:
                        'Shows how long each video and audio file lasts (e.g. 1:23:45) next to its file size, ' +
                        'in the listings and the viewer.',
                },
                {
                    key: 'browse.resolutionMark',
                    label: 'Mark high and low resolution',
                    type: 'checkbox',
                    hint:
                        'Labels the preview of each photo and video HR (high resolution) ' +
                        'or LR (low resolution) in its bottom-right corner, by the thresholds below.',
                },
                {
                    key: 'browse.imageHighResolutionPixels',
                    label: 'Photos are high resolution from (pixels)',
                    type: 'number',
                    min: 1,
                    max: 20000,
                    hint:
                        'Shorter side, in pixels, from which a photo is HR: with 1500, ' +
                        'a 2000×1500 photo (or a portrait 1500×2000) is HR, a 1000×1000 one LR.',
                },
                {
                    key: 'browse.videoHighResolutionPixels',
                    label: 'Videos are high resolution from (pixels)',
                    type: 'number',
                    min: 1,
                    max: 20000,
                    hint:
                        'Shorter side, in pixels, from which a video is HR: with 720, ' +
                        '1280×720 and 1000×1000 videos are HR, 854×480 is LR. The searches r:hr and r:lr use ' +
                        'both thresholds, marks on or off.',
                },
            ],
        },
    ],
};

export const SEARCH_SETTINGS: SettingTab = {
    id: 'search',
    title: 'Search',
    groups: [
        {
            title: 'Results',
            fields: [
                {
                    key: 'search.defaultPageSize',
                    label: 'Results per page',
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint: 'Search results loaded at a time; more load as you scroll.',
                },
                {
                    key: 'search.maxPageSize',
                    label: 'Largest page',
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint:
                        'Most results a single request can ask for. Only matters for scripts and other ' +
                        'programs using the API.',
                },
            ],
        },
        {
            title: 'Search box',
            action: 'clearSearchHistory',
            fields: [
                {
                    key: 'search.explain',
                    label: 'Explain searches',
                    type: 'checkbox',
                    hint:
                        'Adds a button to the search box that puts the typed search into words as you type, ' +
                        'and points out the parts that are ignored or do not work as they may seem.',
                },
                {
                    key: 'search.historySize',
                    label: 'Searches remembered',
                    type: 'number',
                    min: 0,
                    max: 100,
                    hint:
                        'The last searches each browser keeps and suggests again as you type in the search ' +
                        'box, newest first. Kept by each browser for itself. 0: none are kept or suggested.',
                },
            ],
        },
    ],
};
