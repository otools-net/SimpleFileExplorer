import type { SettingTab } from '../settings-schema';

/**
 * The Browsing and Search tabs of the settings (Interface): listings, resolutions and durations, results and
 * the search box.
 */

const API_PAGE_HELP = $localize`Only scripts and other programs using the API ask for pages; the app itself uses the \
page size above.`;

export const BROWSING_SETTINGS: SettingTab = {
    id: 'browsing',
    title: $localize`Browsing`,
    groups: [
        {
            title: $localize`Listings`,
            fields: [
                {
                    key: 'browse.defaultPageSize',
                    label: $localize`Items per page`,
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint: $localize`Files and folders loaded at a time; more load as you scroll. Smaller pages \
show up faster on slow drives.`,
                },
                {
                    key: 'browse.maxPageSize',
                    label: $localize`Largest page`,
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint: $localize`The most items a single request can ask for.`,
                    help: API_PAGE_HELP,
                },
                {
                    key: 'browse.folderPictures',
                    label: $localize`Pictures per folder`,
                    type: 'number',
                    min: 0,
                    max: 16,
                    hint: $localize`Folders show their first photos and videos: 4 makes a 2×2 grid, 9 a 3×3 grid. \
0 lists folders without pictures.`,
                    help: $localize`Only the grid and one-per-row views show them, where folders are cards.`,
                },
                {
                    key: 'browse.liveChanges',
                    label: $localize`Show changes made outside SFE`,
                    type: 'checkbox',
                    hint: $localize`The folder you view is listed again when another program changes it on the disk, \
not only when SFE does.`,
                    help: $localize`The server watches only the folders open in a browser. On a network folder it may \
read the folder again every 10 seconds instead, which takes longer on very large folders. In a connection, it asks the \
storage service what changed every 30 seconds. Off, a folder shows such changes when you open it again.`,
                },
                {
                    key: 'browse.copyPaths',
                    label: $localize`Copy full paths`,
                    type: 'checkbox',
                    hint: $localize`Adds buttons that copy the full path of a folder or file on the server.`,
                    help: $localize`For example D:\\Media\\Photos\\beach.jpg. The buttons sit next to the location \
for the current folder, on each folder and file, and in the viewer. Everyone who sees them learns where the library \
is on the server's disk.`,
                },
            ],
        },
        {
            title: $localize`Resolution and duration`,
            hint: $localize`These need the resolutions and durations read by scans (Scanning › Indexing › Read \
resolutions and durations).`,
            fields: [
                {
                    key: 'browse.showResolution',
                    label: $localize`Show resolutions`,
                    type: 'checkbox',
                    hint: $localize`Shows the size in pixels of photos and videos (e.g. 1920×1080) next to their \
file size, in listings and in the viewer.`,
                },
                {
                    key: 'browse.showDuration',
                    label: $localize`Show durations`,
                    type: 'checkbox',
                    hint: $localize`Shows how long each video and audio file lasts (e.g. 1:23:45) next to its file \
size, in listings and in the viewer.`,
                },
                {
                    key: 'browse.resolutionMark',
                    label: $localize`Mark high and low resolution`,
                    type: 'checkbox',
                    hint: $localize`Labels the preview of each photo and video HR (high resolution) or LR (low \
resolution) in its bottom-right corner, by the thresholds below.`,
                },
                {
                    key: 'browse.imageHighResolutionPixels',
                    label: $localize`Photos are high resolution from (pixels)`,
                    type: 'number',
                    min: 1,
                    max: 20000,
                    hint: $localize`A photo whose shorter side reaches this many pixels is HR.`,
                    help: $localize`With 1500, a 2000×1500 photo (or a portrait one of 1500×2000) is HR, and a \
1000×1000 one is LR.`,
                },
                {
                    key: 'browse.videoHighResolutionPixels',
                    label: $localize`Videos are high resolution from (pixels)`,
                    type: 'number',
                    min: 1,
                    max: 20000,
                    hint: $localize`A video whose shorter side reaches this many pixels is HR.`,
                    help: $localize`With 720, videos of 1280×720 and 1000×1000 are HR, and one of 854×480 is LR. The \
searches r:hr and r:lr use both thresholds, whether the marks are shown or not.`,
                },
            ],
        },
    ],
};

export const SEARCH_SETTINGS: SettingTab = {
    id: 'search',
    title: $localize`Search`,
    groups: [
        {
            title: $localize`Results`,
            fields: [
                {
                    key: 'search.defaultPageSize',
                    label: $localize`Results per page`,
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint: $localize`Search results loaded at a time; more load as you scroll.`,
                },
                {
                    key: 'search.maxPageSize',
                    label: $localize`Largest page`,
                    type: 'number',
                    min: 1,
                    max: 10000,
                    hint: $localize`The most results a single request can ask for.`,
                    help: API_PAGE_HELP,
                },
            ],
        },
        {
            title: $localize`Search box`,
            action: 'clearSearchHistory',
            fields: [
                {
                    key: 'search.explain',
                    label: $localize`Explain searches`,
                    type: 'checkbox',
                    hint: $localize`Adds a button to the search box that puts the search into words as you type.`,
                    help: $localize`It also points out the parts of a search that are ignored, or that don't work \
the way they may seem to.`,
                },
                {
                    key: 'search.historySize',
                    label: $localize`Searches remembered`,
                    type: 'number',
                    min: 0,
                    max: 100,
                    hint: $localize`The last searches each browser keeps and suggests again as you type, newest \
first. 0 keeps none.`,
                    help: $localize`Each browser keeps its own searches: they never reach the server or other \
devices.`,
                },
            ],
        },
    ],
};
