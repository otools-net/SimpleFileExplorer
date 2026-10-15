/*
 * The settings' tabs of grouped fields, each shown as a sub-page of a section of the administration page (see
 * admin/admin-sections.ts). Each field maps to a key of the API's settings (e.g. "browse.defaultPageSize");
 * validation errors come back keyed the same way, or by a group's `key` for errors about several of its fields. The
 * library root, the cache folder, the database, ffmpeg and the system commands are left out on purpose: they are only
 * set in the server's configuration files.
 */

export type GroupAction = 'clearHistory' | 'clearSearchHistory' | 'trickplayStatus' | 'browserTheme';

interface FieldBase {
    key: string;
    label: string;
    hint: string;
    placeholder?: string;
}

export type SettingField = FieldBase & (
    | { type: 'number'; min: number; max: number }
    | { type: 'checkbox' | 'text' | 'list' | 'extensions' }
    | { type: 'select'; options: [string, string][] }
    /** An unsaved choice filling the `fills` fields from `presets` ([label, name, link]). */
    | { type: 'preset'; presets: [string, string, string][]; fills: { url: string; name: string } }
);

export interface SettingGroup {
    title: string;
    /** Key of the errors about several of its fields. */
    key?: string;
    hint?: string;
    action?: GroupAction;
    fields: SettingField[];
}

export interface SettingTab {
    id: string;
    title: string;
    intro?: string;
    groups: SettingGroup[];
}

// Settings shown in the simple view, the default until "Show all settings" is ticked (remembered per browser). Each
// stays in its own group and tab; groups with none of these are hidden. Add or remove keys of TABS freely.
export const SIMPLE_SETTINGS: ReadonlySet<string> = new Set([
    'appearance.theme',
    'browse.defaultPageSize',
    'browse.folderPictures',
    'browse.showResolution',
    'browse.showDuration',
    'browse.resolutionMark',
    'browse.imageHighResolutionPixels',
    'browse.videoHighResolutionPixels',
    'search.defaultPageSize',
    'search.historySize',
    'showHiddenItems',
    'player.seekSeconds',
    'player.loop',
    'player.externalPreset',
    'streaming.mode',
    'scan.ignoredDirectories',
    'scan.ignoredFiles',
    'scan.excludedFolders',
    'fileTypes.images',
    'fileTypes.videos',
    'fileTypes.audio',
    'scan.computeHashes',
    'scan.readResolution',
    'thumbnails.enabled',
    'trickplay.enabled',
    'trickplay.folders',
    'trickplay.hoverPreview',
    'fileTracking.relink.enabled',
    'tags.recordHistory',
    'tasks.logRetentionDays',
    'logs.retentionDays',
    'logs.writeFiles',
    'editor.outputFolder',
]);

// Players that a web page can open a file in through a link they register, for the "External player" choice:
// [label, name on the button, link]. Android has no custom schemes for these: intent: links name the app's package.
export const EXTERNAL_PLAYERS: [string, string, string][] = [
    ['VLC (iPhone, iPad; on a computer only with a vlc:// handler installed)', 'VLC', 'vlc://{url}'],
    ['VLC (Android)', 'VLC', 'intent:{url}#Intent;package=org.videolan.vlc;type=video/*;end'],
    ['MX Player (Android)', 'MX Player', 'intent:{url}#Intent;package=com.mxtech.videoplayer.ad;type=video/*;end'],
    ['MX Player Pro (Android)', 'MX Player', 'intent:{url}#Intent;package=com.mxtech.videoplayer.pro;type=video/*;end'],
    ['PotPlayer (Windows)', 'PotPlayer', 'potplayer://{url}'],
    ['Infuse (iPhone, iPad, Mac)', 'Infuse', 'infuse://x-callback-url/play?url={encodedUrl}'],
    ['IINA (macOS)', 'IINA', 'iina://weblink?url={encodedUrl}'],
];

export const TABS: SettingTab[] = [
    {
        id: 'appearance',
        title: 'Appearance',
        intro: 'Changes apply as soon as you save them, for everyone using the library (the server\'s settings: every library); a setting locked above says why it cannot change here. The library location, the cache folder, the database, ffmpeg and the server commands are set in the server\'s configuration file.',
        groups: [
            {
                title: 'Theme',
                action: 'browserTheme',
                fields: [
                    { key: 'appearance.theme', label: 'Default theme', type: 'select',
                        options: [['system', 'Follow the device'], ['dark', 'Dark'], ['light', 'Light']],
                        hint: 'Colors of the pages, for the browsers that have not picked their own below. “Follow the device” uses the light or dark mode of the computer or phone.' },
                ],
            },
        ],
    },
    {
        id: 'browsing',
        title: 'Browsing',
        groups: [
            {
                title: 'Listings',
                fields: [
                    { key: 'browse.defaultPageSize', label: 'Items per page', type: 'number', min: 1, max: 10000,
                        hint: 'Files and folders loaded at a time; more load as you scroll. Smaller pages show up faster on slow drives.' },
                    { key: 'browse.maxPageSize', label: 'Largest page', type: 'number', min: 1, max: 10000,
                        hint: 'Most items a single request can ask for. Only matters for scripts and other programs using the API.' },
                    { key: 'browse.folderPictures', label: 'Pictures per folder', type: 'number', min: 0, max: 16,
                        hint: 'In the grid and one-per-row views, folders are cards showing their first photos and videos: 4 makes a 2×2 grid, 9 a 3×3 grid. 0 lists folders without pictures.' },
                    { key: 'browse.copyPaths', label: 'Copy full paths', type: 'checkbox',
                        hint: 'Adds buttons copying the full path of a folder or file on the server (e.g. D:\\Media\\Photos\\beach.jpg): next to the location for the current folder, on each folder and file, and in the viewer. This shows every visitor where the library is on the server\'s disk.' },
                ],
            },
            {
                title: 'Resolution and duration',
                hint: 'Needs the resolutions and durations read by scans (Scanning › Indexing › Read resolutions and durations).',
                fields: [
                    { key: 'browse.showResolution', label: 'Show resolutions', type: 'checkbox',
                        hint: 'Shows the size in pixels of photos and videos (e.g. 1920×1080) next to their file size, in the listings and the viewer.' },
                    { key: 'browse.showDuration', label: 'Show durations', type: 'checkbox',
                        hint: 'Shows how long each video and audio file lasts (e.g. 1:23:45) next to its file size, in the listings and the viewer.' },
                    { key: 'browse.resolutionMark', label: 'Mark high and low resolution', type: 'checkbox',
                        hint: 'Labels the preview of each photo and video HR (high resolution) or LR (low resolution) in its bottom-right corner, by the thresholds below.' },
                    { key: 'browse.imageHighResolutionPixels', label: 'Photos are high resolution from (pixels)', type: 'number', min: 1, max: 20000,
                        hint: 'Shorter side, in pixels, from which a photo is HR: with 1500, a 2000×1500 photo (or a portrait 1500×2000) is HR, a 1000×1000 one LR.' },
                    { key: 'browse.videoHighResolutionPixels', label: 'Videos are high resolution from (pixels)', type: 'number', min: 1, max: 20000,
                        hint: 'Shorter side, in pixels, from which a video is HR: with 720, 1280×720 and 1000×1000 videos are HR, 854×480 is LR. The searches r:hr and r:lr use both thresholds, marks on or off.' },
                ],
            },
        ],
    },
    {
        id: 'search',
        title: 'Search',
        groups: [
            {
                title: 'Results',
                fields: [
                    { key: 'search.defaultPageSize', label: 'Results per page', type: 'number', min: 1, max: 10000,
                        hint: 'Search results loaded at a time; more load as you scroll.' },
                    { key: 'search.maxPageSize', label: 'Largest page', type: 'number', min: 1, max: 10000,
                        hint: 'Most results a single request can ask for. Only matters for scripts and other programs using the API.' },
                ],
            },
            {
                title: 'Search box',
                action: 'clearSearchHistory',
                fields: [
                    { key: 'search.explain', label: 'Explain searches', type: 'checkbox',
                        hint: 'Adds a button to the search box that puts the typed search into words as you type, and points out the parts that are ignored or do not work as they may seem.' },
                    { key: 'search.historySize', label: 'Searches remembered', type: 'number', min: 0, max: 100,
                        hint: 'The last searches each browser keeps and suggests again as you type in the search box, newest first. Kept by each browser for itself. 0: none are kept or suggested.' },
                ],
            },
        ],
    },
    {
        id: 'playback',
        title: 'Playback',
        groups: [
            {
                title: 'Video player',
                fields: [
                    { key: 'player.seekSeconds', label: 'Jump back / forward (seconds)', type: 'number', min: 1, max: 600,
                        hint: 'Used by the player\'s back and forward buttons, the J and L keys, and a double tap on the left or right of the picture (each further tap jumps again).' },
                    { key: 'player.loop', label: 'Loop videos', type: 'checkbox',
                        hint: 'Videos start over when they end. The player\'s loop button (R) switches it for the video on screen.' },
                ],
            },
            {
                title: 'External player',
                fields: [
                    { key: 'player.externalPreset', label: 'External player', type: 'preset', presets: EXTERNAL_PLAYERS,
                        fills: { url: 'player.externalUrl', name: 'player.externalName' },
                        hint: 'App that the viewer\'s ▶ button opens videos and audio in. It must be installed on the device you watch on, and that device must reach this server. Applies to every browser.' },
                    { key: 'player.externalUrl', label: 'External player link', type: 'text', placeholder: 'Empty: no external player button',
                        hint: 'Filled in by the choice above, or written by hand: {url} is replaced by the file\'s address, {encodedUrl} by the same address encoded for a query string; without either, the address is added at the end (e.g. vlc://).' },
                    { key: 'player.externalName', label: 'External player name', type: 'text', placeholder: 'external player',
                        hint: 'Shown on its button, as in “Open in VLC”.' },
                    { key: 'player.linkLifetimeMinutes', label: 'Link lifetime (minutes)', type: 'number', min: 1, max: 10080,
                        hint: 'The player gets a link of its own to the file, working without signing in, for this long: long enough to watch a whole film, as players ask again at each seek. A new password or a disabled account ends the links it gave out.' },
                ],
            },
            {
                title: 'Streaming',
                fields: [
                    { key: 'dataServer', label: 'Data server', type: 'text', placeholder: 'Empty: SFE streams the files',
                        hint: 'Optional http:// or https:// address of another web server that already publishes the library. Files then open from there instead of being streamed by SFE. The file\'s path is added after the address, or put where you write {url} (e.g. http://nas/files/{url}?raw=1); {encodedUrl} is the same path fully percent-encoded, slashes too (e.g. http://nas/get?file={encodedUrl}).' },
                ],
            },
            {
                title: 'Videos converted by the server',
                hint: 'Needs ffmpeg on the server (System › About tells whether it was found). Converted videos play as H.264 with AAC sound, which every browser plays; the conversion keeps the server\'s processor busy while someone watches. System › Video streams lists those being converted.',
                fields: [
                    { key: 'streaming.mode', label: 'Convert videos', type: 'select',
                        options: [['fallback', 'When the browser can\'t play them'], ['always', 'Always'], ['off', 'Never']],
                        hint: 'When the browser can\'t play a video (AVI, WMV, HEVC pictures...), the player asks the server for it converted, from where it was. “Always” converts every video, for browsers that play some files without picture or sound. The player\'s ⟳ button converts the video on screen either way.' },
                    { key: 'streaming.maxStreams', label: 'Videos converted at once', type: 'number', min: 1, max: 32,
                        hint: 'For the whole server: each one keeps a processor core or more busy. A viewer asking for one more is told to try again later.' },
                    { key: 'streaming.quality', label: 'Quality (CRF)', type: 'number', min: 15, max: 40,
                        hint: 'Lower is better and heavier: 18 looks like the original, 23 is a good balance, 28 is small.' },
                    { key: 'streaming.maxHeight', label: 'Largest height (pixels)', type: 'number', min: 0, max: 4320,
                        hint: 'Taller videos are scaled down to it while converted, keeping their shape (1080 for Full HD). 0 keeps their size.' },
                    { key: 'streaming.copyVideo', label: 'Copy H.264 pictures', type: 'checkbox',
                        hint: 'Pictures already in H.264 (8-bit) are only repackaged, with their sound converted: almost no work for the server, but a seek lands on the nearest earlier key frame and the largest height doesn\'t apply.' },
                ],
            },
        ],
    },
    {
        id: 'skipped',
        title: 'What to scan',
        groups: [
            {
                title: 'Skipped by scans',
                fields: [
                    { key: 'scan.ignoredDirectories', label: 'Ignored folders', type: 'list', placeholder: '$RECYCLE.BIN',
                        hint: 'Folder names (not paths) that scans skip, with everything inside them. One per line. Already indexed files in them stay until the next scan of their parent folder.' },
                    { key: 'scan.ignoredFiles', label: 'Ignored files', type: 'list', placeholder: 'Thumbs.db',
                        hint: 'File names that scans skip. One per line.' },
                    { key: 'scan.excludedFolders', label: 'Excluded folders', type: 'list', placeholder: 'Downloads\nMovies/Old',
                        hint: 'Library folders (paths from the library root, one per line) that scans leave out, with their sub-folders. The next scan of a folder above one removes it from the index. ⋮ › Remove from index adds folders here.' },
                ],
            },
            {
                title: 'Hidden items',
                fields: [
                    { key: 'showHiddenItems', label: 'Show hidden files and folders', type: 'checkbox',
                        hint: 'Includes items marked hidden or system (on Linux and macOS, names starting with a dot), in listings and in scans.' },
                ],
            },
        ],
    },
    {
        id: 'fileTypes',
        title: 'File types',
        groups: [
            {
                title: 'File types',
                key: 'fileTypes',
                hint: 'Which files are photos, videos and audio, by their extension.',
                fields: [
                    { key: 'fileTypes.images', label: 'Photos', type: 'extensions',
                        hint: 'Extensions opened in the photo viewer and given thumbnails, separated by commas.' },
                    { key: 'fileTypes.videos', label: 'Videos', type: 'extensions',
                        hint: 'Extensions played in the video player and given thumbnails and trickplay.' },
                    { key: 'fileTypes.audio', label: 'Audio', type: 'extensions',
                        hint: 'Extensions played in the audio player. Other extensions open in a new tab.' },
                ],
            },
        ],
    },
    {
        id: 'indexing',
        title: 'Indexing',
        groups: [
            {
                title: 'Read from files',
                fields: [
                    { key: 'scan.computeHashes', label: 'Fingerprint files', type: 'checkbox',
                        hint: 'Reads the start of each new or changed file to compute a fingerprint, used to find duplicates and to recognise moved files. Makes scans slower, especially over the network.' },
                    { key: 'scan.readResolution', label: 'Read resolutions and durations', type: 'checkbox',
                        hint: 'Reads the size in pixels of each new or changed photo and video, and how long each video and audio file lasts, to show them and to search by them (w:, h:, r:, d:). Videos and audio need ffmpeg, which makes scans of new ones slower. Files already indexed are read by the next scan.' },
                ],
            },
            {
                title: 'Performance',
                fields: [
                    { key: 'scan.parallelism', label: 'Folders scanned at once', type: 'number', min: 1, max: 64,
                        hint: 'Higher is faster on SSDs and fast network drives, lower is gentler on spinning disks.' },
                ],
            },
        ],
    },
    {
        id: 'thumbnails',
        title: 'Thumbnails',
        groups: [
            {
                title: 'Previews',
                fields: [
                    { key: 'thumbnails.enabled', label: 'Show thumbnails', type: 'checkbox',
                        hint: 'Previews of photos and videos, rendered with ffmpeg and kept in the server\'s cache folder.' },
                    { key: 'thumbnails.width', label: 'Width (pixels)', type: 'number', min: 32, max: 4096,
                        hint: 'Applies to new thumbnails; existing ones are kept until their file changes.' },
                    { key: 'thumbnails.videoSeekSeconds', label: 'Video position (seconds)', type: 'number', min: 0, max: 3600,
                        hint: 'Moment of the video used as its preview. Shorter clips use their first frame.' },
                    { key: 'thumbnails.largeImageKilobytes', label: 'Large image size (KB)', type: 'number', min: 0, max: 1048576,
                        hint: 'Larger photos are previewed through their thumbnail instead of being downloaded whole; the viewer shows the thumbnail until the original has loaded. 0 uses thumbnails for every photo.' },
                    { key: 'thumbnails.singleViewOriginals', label: 'Full images in one-per-row view', type: 'checkbox',
                        hint: 'The one-per-row view shows each photo itself instead of its thumbnail, sharp at any size, even above the large image size. Large photos are downloaded whole as you scroll past them, which can be slow over the network.' },
                ],
            },
            {
                title: 'Performance',
                fields: [
                    { key: 'thumbnails.maxConcurrency', label: 'Rendered at once', type: 'number', min: 1, max: 32,
                        hint: 'Thumbnails rendered in parallel. Each one runs ffmpeg, so higher values use more CPU.' },
                    { key: 'thumbnails.timeoutSeconds', label: 'Time limit (seconds)', type: 'number', min: 1, max: 600,
                        hint: 'A thumbnail taking longer is given up on, and tried again on the next request.' },
                ],
            },
        ],
    },
    {
        id: 'trickplay',
        title: 'Trickplay',
        intro: 'Trickplay is a strip of small frames taken along a video. The player shows them above its seek bar while you hover or drag it, so you can see where you are going. They are created in the background with ffmpeg and kept in the trickplay folder of the server\'s cache.',
        groups: [
            {
                title: 'Background job',
                action: 'trickplayStatus',
                fields: [
                    { key: 'trickplay.enabled', label: 'Create trickplay', type: 'checkbox',
                        hint: 'The first pass over a large library keeps ffmpeg busy for a long time; the following ones only handle new and changed videos.' },
                    { key: 'trickplay.folders', label: 'Folders', type: 'list', placeholder: 'Movies\nSeries/Anime',
                        hint: 'Library folders whose videos get trickplay, with their sub-folders: paths from the library root, one per line. Empty: the whole library.' },
                    { key: 'trickplay.recheckHours', label: 'Look for new videos every (hours)', type: 'number', min: 0, max: 720,
                        hint: 'The folders are also checked when the server starts and when these settings change, and a video you open is handled right away. 0: no periodic check. Background tasks shows when the next check runs, and can schedule checks at set times.' },
                ],
            },
            {
                title: 'Listings',
                fields: [
                    { key: 'trickplay.hoverPreview', label: 'Play on hover', type: 'checkbox',
                        hint: 'Resting the mouse on a video plays its trickplay over the thumbnail. On a touch screen: press and hold it. Only videos that already have trickplay.' },
                ],
            },
            {
                title: 'Frames',
                hint: 'Changing these creates the trickplay of every video again, in the background. The old frames are shown until then.',
                fields: [
                    { key: 'trickplay.intervalSeconds', label: 'Interval (seconds)', type: 'number', min: 1, max: 600,
                        hint: 'Time between two frames. Shorter is more precise, but takes longer and more space.' },
                    { key: 'trickplay.width', label: 'Frame width (pixels)', type: 'number', min: 64, max: 1280,
                        hint: 'Smaller videos keep their own width.' },
                    { key: 'trickplay.columns', label: 'Frames per row', type: 'number', min: 1, max: 20,
                        hint: 'Frames are stored on sheets of rows × columns.' },
                    { key: 'trickplay.rows', label: 'Rows per sheet', type: 'number', min: 1, max: 20,
                        hint: 'Bigger sheets mean fewer, larger files.' },
                    { key: 'trickplay.keyFramesOnly', label: 'Fast mode', type: 'checkbox',
                        hint: 'Decodes only the key frames of the video: many times faster, but a frame can be a few seconds off its position.' },
                ],
            },
            {
                title: 'Performance',
                fields: [
                    { key: 'trickplay.parallelism', label: 'Videos at once', type: 'number', min: 1, max: 16,
                        hint: 'Each one runs ffmpeg, which can use several CPU cores.' },
                    { key: 'trickplay.timeoutMinutes', label: 'Time limit per video (minutes)', type: 'number', min: 1, max: 1440,
                        hint: 'A video taking longer is skipped until it or these settings change.' },
                ],
            },
        ],
    },
    {
        id: 'tracking',
        title: 'Duplicates & moves',
        intro: 'Find identical files in the library, and keep the tags of files that are moved, renamed, or whose drive was missing during a scan. Both work from the index: scan folders to keep it current.',
        groups: [
            {
                title: 'Duplicates',
                hint: 'Files with the same size and fingerprint are taken as identical. Needs fingerprints (Scanning › Indexing › Fingerprint files).',
                fields: [
                    { key: 'fileTracking.duplicates.minimumKilobytes', label: 'Smallest file (KB)', type: 'number', min: 0, max: 1048576,
                        hint: 'Smaller files are left out: libraries hold many identical tiny files. 0 compares every file.' },
                    { key: 'fileTracking.duplicates.groupsPerPage', label: 'Groups per page', type: 'number', min: 1, max: 500,
                        hint: 'Groups of identical files loaded at a time; “Load more” shows the next ones.' },
                    { key: 'fileTracking.duplicates.allowRemove', label: 'Allow deleting copies', type: 'checkbox',
                        hint: 'Adds a Remove button to each copy: the file is deleted from the disk and its tags are added to a copy you keep. The two files are compared byte for byte first, so an identical copy always stays. Anyone who can open SFE can use it.' },
                ],
            },
            {
                title: 'Similar videos',
                hint: 'Videos showing the same pictures in other files (another resolution, quality or format, trimmed) are found from fingerprints made with their trickplay: turn trickplay on for the folders to compare.',
                fields: [
                    { key: 'fileTracking.similarVideos.minimumPercent', label: 'Smallest match (%)', type: 'number', min: 50, max: 100,
                        hint: 'Share of frames two videos must have in common, out of the longer one. A copy at another resolution is near 100%, a copy cut by a tenth near 90%. Applies from the next comparison.' },
                ],
            },
            {
                title: 'Moved and renamed files',
                fields: [
                    { key: 'fileTracking.relink.enabled', label: 'Keep the tags of moved files', type: 'checkbox',
                        hint: 'A tagged file that a scan no longer finds is remembered with its tags. When a scan indexes the same file somewhere else (moved, renamed, or back on its drive), the tags move over to it. When off, a file no longer found loses its tags.' },
                    { key: 'fileTracking.relink.matchByName', label: 'Recognise files without a fingerprint by name', type: 'checkbox',
                        hint: 'Files indexed without a fingerprint are matched on their name, size and modification date: a move is recognised, a rename is not.' },
                    { key: 'fileTracking.relink.retentionDays', label: 'Remember missing files for (days)', type: 'number', min: 0, max: 3650,
                        hint: 'Missing files not found again in that time are forgotten, with their tags. 0: until found or forgotten by hand.' },
                ],
            },
        ],
    },
    {
        id: 'tasks',
        title: 'Tasks',
        intro: 'Scans, trickplay, similar-video comparisons and thumbnails run in the background. The other tabs show what runs, when each task runs next, the schedules that start them, and the log of every run.',
        groups: [
            {
                title: 'History',
                fields: [
                    { key: 'tasks.logRetentionDays', label: 'Keep runs and logs for (days)', type: 'number', min: 0, max: 3650,
                        hint: 'Runs that finished longer ago are forgotten, with their logs. 0: until cleared by hand.' },
                ],
            },
        ],
    },
    {
        id: 'logs',
        title: 'Logs',
        intro: 'What the server logs (its messages, warnings and errors, for every space) is kept for the Logs page. Both settings are the whole server\'s.',
        groups: [
            {
                title: 'Keeping the log',
                fields: [
                    { key: 'logs.retentionDays', label: 'Keep log entries for (days)', type: 'number', min: 0, max: 3650,
                        hint: 'Older entries are forgotten, and so are the daily files. 0: kept forever (the database keeps growing).' },
                    { key: 'logs.writeFiles', label: 'Also save the log to files', type: 'checkbox',
                        hint: 'Writes a copy of every entry to a file per day (sfe-yyyymmdd.log) in the logs folder, next to the database and the thumbnails (data/logs unless the server\'s configuration says otherwise). The files are written even while the database can\'t be.' },
                ],
            },
        ],
    },
    {
        id: 'tags',
        title: 'Tags',
        groups: [
            {
                title: 'Sidebar',
                fields: [
                    { key: 'tags.sidebarCount', label: 'Tags per sidebar section', type: 'number', min: 1, max: 20,
                        hint: 'How many tags each sidebar section shows.' },
                ],
            },
            {
                title: 'History',
                action: 'clearHistory',
                fields: [
                    { key: 'tags.recordHistory', label: 'Remember tag history', type: 'checkbox',
                        hint: 'Records the tags you search for and the tags of the files you open, for the sidebar\'s “Last searched” and “Last viewed” lists. When off, nothing new is recorded and those lists are hidden.' },
                ],
            },
        ],
    },
    {
        id: 'editor',
        title: 'Picture editor',
        intro: 'The Edit button of the viewer crops pictures. The result is always saved as a new file: originals are never changed.',
        groups: [
            {
                title: 'Saving',
                fields: [
                    { key: 'editor.outputFolder', label: 'Folder for edited pictures', type: 'text', placeholder: 'Edited',
                        hint: 'A folder of the library (path from the library root) where edited pictures are saved, in the same sub-folders as their originals: Photos/2024/beach.jpg is saved as Edited/Photos/2024/beach.jpg. Created when first needed. Empty: next to the original, as “beach (edited).jpg”. Existing files are never replaced: a number is added to the name.' },
                    { key: 'editor.quality', label: 'Quality (JPEG and WebP)', type: 'number', min: 10, max: 100,
                        hint: 'How much detail JPEG and WebP pictures keep when saved: higher is sharper and larger. PNG pictures are saved without loss; other formats are saved as PNG.' },
                ],
            },
        ],
    },
];

export function getValue(values: unknown, key: string): unknown {
    return key.split('.').reduce<unknown>((value, part) => (value as Record<string, unknown> | undefined)?.[part], values);
}

export function setValue(values: Record<string, unknown>, key: string, value: unknown): void {
    const parts = key.split('.');
    const last = parts.pop()!;
    (parts.reduce<unknown>((target, part) => (target as Record<string, unknown>)[part], values) as Record<string, unknown>)[last] = value;
}
