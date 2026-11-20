import type { SettingTab } from '../settings-schema';

/**
 * The Thumbnails and Trickplay tabs of the settings.
 */

export const THUMBNAIL_SETTINGS: SettingTab = {
    id: 'thumbnails',
    title: 'Thumbnails',
    groups: [
        {
            title: 'Previews',
            fields: [
                {
                    key: 'thumbnails.enabled',
                    label: 'Show thumbnails',
                    type: 'checkbox',
                    hint:
                        "Previews of photos and videos, rendered with ffmpeg and kept in the server's cache " +
                        'folder.',
                },
                {
                    key: 'thumbnails.width',
                    label: 'Width (pixels)',
                    type: 'number',
                    min: 32,
                    max: 4096,
                    hint: 'Applies to new thumbnails; existing ones are kept until their file changes.',
                },
                {
                    key: 'thumbnails.videoSeekSeconds',
                    label: 'Video position (seconds)',
                    type: 'number',
                    min: 0,
                    max: 3600,
                    hint: 'Moment of the video used as its preview. Shorter clips use their first frame.',
                },
                {
                    key: 'thumbnails.largeImageKilobytes',
                    label: 'Large image size (KB)',
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint:
                        'Larger photos are previewed through their thumbnail instead of being downloaded ' +
                        'whole; the viewer shows the thumbnail until the original has loaded. ' +
                        '0 uses thumbnails for every photo.',
                },
                {
                    key: 'thumbnails.singleViewOriginals',
                    label: 'Full images in one-per-row view',
                    type: 'checkbox',
                    hint:
                        'The one-per-row view shows each photo itself instead of its thumbnail, ' +
                        'sharp at any size, even above the large image size. Large photos are downloaded whole ' +
                        'as you scroll past them, which can be slow over the network.',
                },
                {
                    key: 'thumbnails.animatedHover',
                    label: 'Play animated images on hover',
                    type: 'checkbox',
                    hint:
                        'Resting the mouse on an animated GIF, PNG, WebP or AVIF shown through its thumbnail ' +
                        '(or, on a touch screen, holding it) plays the image itself. Each time, ' +
                        'the whole file is downloaded.',
                },
                {
                    key: 'thumbnails.excludedFolders',
                    label: 'Excluded folders',
                    type: 'list',
                    placeholder: 'Scans\nPhotos/Raw',
                    hint:
                        'Library folders (paths from the library root, one per line) ' +
                        'whose files get no new thumbnails, with their sub-folders; ' +
                        'those already made are still shown. Scans and previews… in the menu of a folder, ' +
                        'or Scanning › Excluded folders, adds and removes them.',
                },
            ],
        },
        {
            title: 'Performance',
            fields: [
                {
                    key: 'thumbnails.maxConcurrency',
                    label: 'Rendered at once',
                    type: 'number',
                    min: 1,
                    max: 32,
                    hint: 'Thumbnails rendered in parallel. Each one runs ffmpeg, so higher values use more CPU.',
                },
                {
                    key: 'thumbnails.timeoutSeconds',
                    label: 'Time limit (seconds)',
                    type: 'number',
                    min: 1,
                    max: 600,
                    hint: 'A thumbnail taking longer is given up on, and tried again on the next request.',
                },
            ],
        },
    ],
};

export const TRICKPLAY_SETTINGS: SettingTab = {
    id: 'trickplay',
    title: 'Trickplay',
    intro:
        'Trickplay is a strip of small frames taken along a video. The player shows them above its seek bar ' +
        'while you hover or drag it, so you can see where you are going. They are created in the background with ' +
        "ffmpeg and kept in the trickplay folder of the server's cache.",
    groups: [
        {
            title: 'Background job',
            action: 'trickplayStatus',
            fields: [
                {
                    key: 'trickplay.enabled',
                    label: 'Create trickplay',
                    type: 'checkbox',
                    hint:
                        'The first pass over a large library keeps ffmpeg busy for a long time; ' +
                        'the following ones only handle new and changed videos.',
                },
                {
                    key: 'trickplay.folders',
                    label: 'Folders',
                    type: 'list',
                    placeholder: 'Movies\nSeries/Anime',
                    hint:
                        'Library folders whose videos get trickplay, with their sub-folders: ' +
                        'paths from the library root, one per line. Empty: the whole library.',
                },
                {
                    key: 'trickplay.excludedFolders',
                    label: 'Excluded folders',
                    type: 'list',
                    placeholder: 'Movies/Trailers',
                    hint:
                        'Library folders whose videos get no trickplay, with their sub-folders, ' +
                        'even inside the folders above; those already made are still shown. ' +
                        'Scans and previews… in the menu of a folder, or Scanning › Excluded folders, ' +
                        'adds and removes them.',
                },
                {
                    key: 'trickplay.recheckHours',
                    label: 'Look for new videos every (hours)',
                    type: 'number',
                    min: 0,
                    max: 720,
                    hint:
                        'The folders are also checked when the server starts and when these settings change, ' +
                        'and a video you open is handled right away. 0: no periodic check. ' +
                        'Background tasks shows when the next check runs, and can schedule checks at set times.',
                },
            ],
        },
        {
            title: 'Listings',
            fields: [
                {
                    key: 'trickplay.hoverPreview',
                    label: 'Play on hover',
                    type: 'checkbox',
                    hint:
                        'Resting the mouse on a video plays its trickplay over the thumbnail. ' +
                        'On a touch screen: press and hold it. Only videos that already have trickplay.',
                },
            ],
        },
        {
            title: 'Frames',
            hint:
                'Changing these creates the trickplay of every video again, in the background. ' +
                'The old frames are shown until then.',
            fields: [
                {
                    key: 'trickplay.intervalSeconds',
                    label: 'Interval (seconds)',
                    type: 'number',
                    min: 1,
                    max: 600,
                    hint: 'Time between two frames. Shorter is more precise, but takes longer and more space.',
                },
                {
                    key: 'trickplay.width',
                    label: 'Frame width (pixels)',
                    type: 'number',
                    min: 64,
                    max: 1280,
                    hint: 'Smaller videos keep their own width.',
                },
                {
                    key: 'trickplay.columns',
                    label: 'Frames per row',
                    type: 'number',
                    min: 1,
                    max: 20,
                    hint: 'Frames are stored on sheets of rows × columns.',
                },
                {
                    key: 'trickplay.rows',
                    label: 'Rows per sheet',
                    type: 'number',
                    min: 1,
                    max: 20,
                    hint: 'Bigger sheets mean fewer, larger files.',
                },
                {
                    key: 'trickplay.keyFramesOnly',
                    label: 'Fast mode',
                    type: 'checkbox',
                    hint:
                        'Decodes only the key frames of the video: many times faster, ' +
                        'but a frame can be a few seconds off its position.',
                },
            ],
        },
        {
            title: 'Performance',
            fields: [
                {
                    key: 'trickplay.parallelism',
                    label: 'Videos at once',
                    type: 'number',
                    min: 1,
                    max: 16,
                    hint: 'Each one runs ffmpeg, which can use several CPU cores.',
                },
                {
                    key: 'trickplay.timeoutMinutes',
                    label: 'Time limit per video (minutes)',
                    type: 'number',
                    min: 1,
                    max: 1440,
                    hint: 'A video taking longer is skipped until it or these settings change.',
                },
            ],
        },
    ],
};
