import type { SettingTab } from '../settings-schema';

/**
 * The Thumbnails and Trickplay tabs of the settings.
 */

/** Where else the lists of excluded folders are managed. */
const EXCLUSIONS_HELP = $localize`Those already made are still shown. “Scans and previews…” in a folder's menu, or \
Scanning › Excluded folders, adds and removes folders here too.`;

export const THUMBNAIL_SETTINGS: SettingTab = {
    id: 'thumbnails',
    title: $localize`Thumbnails`,
    groups: [
        {
            title: $localize`Previews`,
            fields: [
                {
                    key: 'thumbnails.enabled',
                    label: $localize`Show thumbnails`,
                    type: 'checkbox',
                    hint: $localize`Shows small pictures of photos and videos, made with ffmpeg and kept in the \
server's cache folder.`,
                },
                {
                    key: 'thumbnails.width',
                    label: $localize`Width (pixels)`,
                    type: 'number',
                    min: 32,
                    max: 4096,
                    hint: $localize`Applies to new thumbnails; existing ones are kept until their file changes.`,
                },
                {
                    key: 'thumbnails.videoSeekSeconds',
                    label: $localize`Video position (seconds)`,
                    type: 'number',
                    min: 0,
                    max: 3600,
                    hint: $localize`The moment of a video shown as its thumbnail. Shorter videos use their first \
frame.`,
                },
                {
                    key: 'thumbnails.largeImageKilobytes',
                    label: $localize`Large image size (KB)`,
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint: $localize`Larger photos are previewed through their thumbnail instead of being downloaded \
whole. 0 uses thumbnails for every photo.`,
                    help: $localize`The viewer shows the thumbnail until the original has loaded.`,
                },
                {
                    key: 'thumbnails.singleViewOriginals',
                    label: $localize`Full images in one-per-row view`,
                    type: 'checkbox',
                    hint: $localize`The one-per-row view shows each photo itself instead of its thumbnail, sharp at \
any size.`,
                    help: $localize`This applies even above the large image size: large photos are downloaded \
whole as you scroll past them, which can be slow over the network.`,
                },
                {
                    key: 'thumbnails.animatedHover',
                    label: $localize`Play animated images on hover`,
                    type: 'checkbox',
                    hint: $localize`Resting the mouse on an animated GIF, PNG, WebP or AVIF plays it (on a touch \
screen: press and hold).`,
                    help: $localize`It concerns animated images shown through their thumbnail. Each time, the whole \
file is downloaded.`,
                },
                {
                    key: 'thumbnails.excludedFolders',
                    label: $localize`Excluded folders`,
                    type: 'list',
                    placeholder: 'Scans\nPhotos/Raw',
                    hint: $localize`Folders whose files get no new thumbnails, with the folders inside them: paths \
from the top of the library, one per line.`,
                    help: EXCLUSIONS_HELP,
                },
            ],
        },
        {
            title: $localize`Performance`,
            fields: [
                {
                    key: 'thumbnails.maxConcurrency',
                    label: $localize`Rendered at once`,
                    type: 'number',
                    min: 1,
                    max: 32,
                    hint: $localize`Thumbnails rendered in parallel. Each one runs ffmpeg, so higher values use \
more of the processor.`,
                },
                {
                    key: 'thumbnails.timeoutSeconds',
                    label: $localize`Time limit (seconds)`,
                    type: 'number',
                    min: 1,
                    max: 600,
                    hint: $localize`A thumbnail taking longer is given up, and tried again the next time it is \
asked for.`,
                },
            ],
        },
    ],
};

export const TRICKPLAY_SETTINGS: SettingTab = {
    id: 'trickplay',
    title: $localize`Trickplay`,
    intro: $localize`Trickplay is a strip of small frames taken along a video. The player shows them above its seek \
bar while you hover over it or drag it, so you can see where you are going. They are made in the background with \
ffmpeg and kept in the trickplay folder of the server's cache.`,
    groups: [
        {
            title: $localize`Background task`,
            action: 'trickplayStatus',
            fields: [
                {
                    key: 'trickplay.enabled',
                    label: $localize`Create trickplay`,
                    type: 'checkbox',
                    hint: $localize`The first pass over a large library keeps ffmpeg busy for a long time; later \
ones only handle new and changed videos.`,
                },
                {
                    key: 'trickplay.folders',
                    label: $localize`Folders`,
                    type: 'list',
                    placeholder: 'Movies\nSeries/Anime',
                    hint: $localize`Folders whose videos get trickplay, with the folders inside them: paths from \
the top of the library, one per line. Empty: the whole library.`,
                },
                {
                    key: 'trickplay.excludedFolders',
                    label: $localize`Excluded folders`,
                    type: 'list',
                    placeholder: 'Movies/Trailers',
                    hint: $localize`Folders whose videos get no trickplay, with the folders inside them, even \
inside the folders above.`,
                    help: EXCLUSIONS_HELP,
                },
                {
                    key: 'trickplay.recheckHours',
                    label: $localize`Look for new videos every (hours)`,
                    type: 'number',
                    min: 0,
                    max: 720,
                    hint: $localize`How often the folders are checked for new videos. 0: no regular check.`,
                    help: $localize`The folders are also checked when the server starts and when these settings \
change, and a video you open is handled right away. Background tasks shows when the next check runs, and can \
schedule checks at set times.`,
                },
            ],
        },
        {
            title: $localize`Listings`,
            fields: [
                {
                    key: 'trickplay.hoverPreview',
                    label: $localize`Play on hover`,
                    type: 'checkbox',
                    hint: $localize`Resting the mouse on a video plays its trickplay over the thumbnail (on a touch \
screen: press and hold). Only videos that already have trickplay.`,
                },
            ],
        },
        {
            title: $localize`Frames`,
            hint: $localize`Changing these makes the trickplay of every video again, in the background. The old \
frames are shown until then.`,
            fields: [
                {
                    key: 'trickplay.intervalSeconds',
                    label: $localize`Interval (seconds)`,
                    type: 'number',
                    min: 1,
                    max: 600,
                    hint: $localize`Time between two frames. Shorter is more precise, but takes longer and more \
space.`,
                },
                {
                    key: 'trickplay.width',
                    label: $localize`Frame width (pixels)`,
                    type: 'number',
                    min: 64,
                    max: 1280,
                    hint: $localize`Narrower videos keep their own width.`,
                },
                {
                    key: 'trickplay.columns',
                    label: $localize`Frames per row`,
                    type: 'number',
                    min: 1,
                    max: 20,
                    hint: $localize`Frames are stored on sheets of rows × columns.`,
                },
                {
                    key: 'trickplay.rows',
                    label: $localize`Rows per sheet`,
                    type: 'number',
                    min: 1,
                    max: 20,
                    hint: $localize`Bigger sheets mean fewer, larger files.`,
                },
                {
                    key: 'trickplay.keyFramesOnly',
                    label: $localize`Fast mode`,
                    type: 'checkbox',
                    hint: $localize`Decodes only the key frames of each video: many times faster, but a frame can be \
a few seconds off its position.`,
                },
            ],
        },
        {
            title: $localize`Performance`,
            fields: [
                {
                    key: 'trickplay.parallelism',
                    label: $localize`Videos at once`,
                    type: 'number',
                    min: 1,
                    max: 16,
                    hint: $localize`Each one runs ffmpeg, which can use several processor cores.`,
                },
                {
                    key: 'trickplay.timeoutMinutes',
                    label: $localize`Time limit per video (minutes)`,
                    type: 'number',
                    min: 1,
                    max: 1440,
                    hint: $localize`A video taking longer is skipped until it, or these settings, change.`,
                },
            ],
        },
    ],
};
