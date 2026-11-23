import type { SettingTab } from '../settings-schema';

/**
 * The tabs of the Scanning section of the settings: what scans skip, file types and what scans read.
 */

export const SKIPPED_SETTINGS: SettingTab = {
    id: 'skipped',
    title: $localize`What to scan`,
    groups: [
        {
            title: $localize`Skipped by scans`,
            fields: [
                {
                    key: 'scan.ignoredDirectories',
                    label: $localize`Ignored folders`,
                    type: 'list',
                    placeholder: '$RECYCLE.BIN',
                    hint: $localize`Folder names (not paths) that scans skip, with everything inside them. One per \
line.`,
                    help: $localize`Files of these folders already in the index stay there until the next scan of \
the folder above them.`,
                },
                {
                    key: 'scan.ignoredFiles',
                    label: $localize`Ignored files`,
                    type: 'list',
                    placeholder: 'Thumbs.db',
                    hint: $localize`File names that scans skip. One per line.`,
                },
                {
                    key: 'scan.excludedFolders',
                    label: $localize`Excluded folders`,
                    type: 'list',
                    placeholder: 'Downloads\nMovies/Old',
                    hint: $localize`Folders that scans leave out, with the folders inside them: paths from the top \
of the library, one per line.`,
                    help: $localize`What the index already has of them stays as it is, never refreshed. “Scans and \
previews…” in a folder's menu, or Scanning › Excluded folders, adds and removes them too; ⋮ › Remove from index \
forgets a folder and can add it here as well.`,
                },
            ],
        },
        {
            title: $localize`Hidden items`,
            fields: [
                {
                    key: 'showHiddenItems',
                    label: $localize`Show hidden files and folders`,
                    type: 'checkbox',
                    hint: $localize`Includes items marked hidden or system (on Linux and macOS, names starting with \
a dot), in listings and in scans.`,
                },
            ],
        },
    ],
};

export const FILE_TYPE_SETTINGS: SettingTab = {
    id: 'fileTypes',
    title: $localize`File types`,
    groups: [
        {
            title: $localize`File types`,
            key: 'fileTypes',
            hint: $localize`Which files are photos, videos, audio and text, by their extension.`,
            fields: [
                {
                    key: 'fileTypes.images',
                    label: $localize`Photos`,
                    type: 'extensions',
                    hint: $localize`Extensions opened in the photo viewer and given thumbnails, separated by \
commas.`,
                },
                {
                    key: 'fileTypes.videos',
                    label: $localize`Videos`,
                    type: 'extensions',
                    hint: $localize`Extensions played in the video player and given thumbnails and trickplay.`,
                },
                {
                    key: 'fileTypes.audio',
                    label: $localize`Audio`,
                    type: 'extensions',
                    hint: $localize`Extensions played in the audio player. Other extensions open in a new tab.`,
                },
                {
                    key: 'fileTypes.text',
                    label: $localize`More text files`,
                    type: 'extensions',
                    hint: $localize`Extensions opened in the text viewer besides those it knows, e.g. dat, out, sub. \
Not media extensions.`,
                    help: $localize`The text viewer already knows .txt, .log, .md, .json, .xml, code and scripts.`,
                },
            ],
        },
    ],
};

export const INDEXING_SETTINGS: SettingTab = {
    id: 'indexing',
    title: $localize`Indexing`,
    groups: [
        {
            title: $localize`Read from files`,
            fields: [
                {
                    key: 'scan.computeHashes',
                    label: $localize`Fingerprint files`,
                    type: 'checkbox',
                    hint: $localize`Reads the start of each new or changed file to find duplicates and recognise \
moved files.`,
                    help: $localize`It makes scans slower, especially over the network.`,
                },
                {
                    key: 'scan.readResolution',
                    label: $localize`Read resolutions and durations`,
                    type: 'checkbox',
                    hint: $localize`Reads the size in pixels of photos and videos, and how long videos and audio \
last, to show them and search by them (w:, h:, r:, d:).`,
                    help: $localize`Files already in the index are read by the next scan. Videos and audio need \
ffmpeg, which makes scanning new ones slower.`,
                },
            ],
        },
        {
            title: $localize`As you browse`,
            fields: [
                {
                    key: 'scan.indexWhenListed',
                    label: $localize`Index folders as they are opened`,
                    type: 'checkbox',
                    hint: $localize`Indexes a folder in the background when someone opens it, that folder only, at \
most once every 10 minutes.`,
                    help: $localize`New files then show their tags, resolutions and durations, and searches find \
them, without waiting for the next scan. The folders inside it are left to scans. Excluded folders are not \
indexed.`,
                },
            ],
        },
        {
            title: $localize`Performance`,
            fields: [
                {
                    key: 'scan.parallelism',
                    label: $localize`Folders scanned at once`,
                    type: 'number',
                    min: 1,
                    max: 64,
                    hint: $localize`Higher is faster on SSDs and fast network drives; lower is gentler on spinning \
disks.`,
                },
            ],
        },
    ],
};
