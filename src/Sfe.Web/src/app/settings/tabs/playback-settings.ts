import type { SettingTab } from '../settings-schema';

/**
 * The Player and Streaming tabs of the settings (Playback): the video player, the external player, Flash, the
 * data server and videos converted by the server.
 */

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

export const PLAYER_SETTINGS: SettingTab = {
    id: 'player',
    title: 'Player',
    groups: [
        {
            title: 'Video player',
            fields: [
                {
                    key: 'player.seekSeconds',
                    label: 'Jump back / forward (seconds)',
                    type: 'number',
                    min: 1,
                    max: 600,
                    hint:
                        "Used by the player's back and forward buttons, the J and L keys, " +
                        'and a double tap on the left or right of the picture (each further tap jumps again).',
                },
                {
                    key: 'player.loop',
                    label: 'Loop videos',
                    type: 'checkbox',
                    hint:
                        "Videos start over when they end. The player's loop button (R) " +
                        'switches it for the video on screen.',
                },
            ],
        },
        {
            title: 'External player',
            fields: [
                {
                    key: 'player.externalPreset',
                    label: 'External player',
                    type: 'preset',
                    presets: EXTERNAL_PLAYERS,
                    fills: { url: 'player.externalUrl', name: 'player.externalName' },
                    hint:
                        "App that the viewer's ▶ button opens videos and audio in. " +
                        'It must be installed on the device you watch on, and that device must reach this ' +
                        'server. Applies to every browser.',
                },
                {
                    key: 'player.externalUrl',
                    label: 'External player link',
                    type: 'text',
                    placeholder: 'Empty: no external player button',
                    hint:
                        "Filled in by the choice above, or written by hand: {url} is replaced by the file's " +
                        'address, {encodedUrl} by the same address encoded for a query string; without either, ' +
                        'the address is added at the end (e.g. vlc://).',
                },
                {
                    key: 'player.externalName',
                    label: 'External player name',
                    type: 'text',
                    placeholder: 'external player',
                    hint: 'Shown on its button, as in “Open in VLC”.',
                },
                {
                    key: 'player.linkLifetimeMinutes',
                    label: 'Link lifetime (minutes)',
                    type: 'number',
                    min: 1,
                    max: 10080,
                    hint:
                        'The player gets a link of its own to the file, working without signing in, ' +
                        'for this long: long enough to watch a whole film, as players ask again at each seek. ' +
                        'A new password or a disabled account ends the links it gave out.',
                },
            ],
        },
        {
            title: 'Flash movies',
            fields: [
                {
                    key: 'player.flash',
                    label: 'Play Flash movies',
                    type: 'checkbox',
                    hint:
                        'Flash movies and games (.swf) play in the viewer, through Ruffle, ' +
                        'a Flash Player emulator that comes with SFE. A movie can only read the files of its own ' +
                        'folder, and never acts as the person watching it. Off: .swf files are only downloaded.',
                },
            ],
        },
    ],
};

export const STREAMING_SETTINGS: SettingTab = {
    id: 'streaming',
    title: 'Streaming',
    groups: [
        {
            title: 'Data server',
            fields: [
                {
                    key: 'dataServer',
                    label: 'Data server',
                    type: 'text',
                    placeholder: 'Empty: SFE streams the files',
                    hint:
                        'Optional http:// or https:// address of another web server that already publishes the ' +
                        'library. Files then open from there instead of being streamed by SFE. ' +
                        "The file's path is added after the address, or put where you write {url} (e.g. " +
                        'http://nas/files/{url}?raw=1); {encodedUrl} is the same path fully percent-encoded, ' +
                        'slashes too (e.g. http://nas/get?file={encodedUrl}).',
                },
            ],
        },
        {
            title: 'Videos converted by the server',
            hint:
                'Needs ffmpeg on the server (System › About tells whether it was found). ' +
                'Converted videos play as H.264 with AAC sound, which every browser plays; ' +
                "the conversion keeps the server's processor busy while someone watches. " +
                'Playback › Video streams lists those being converted.',
            fields: [
                {
                    key: 'streaming.mode',
                    label: 'Convert videos',
                    type: 'select',
                    options: [
                        ['fallback', "When the browser can't play them"],
                        ['always', 'Always'],
                        ['off', 'Never'],
                    ],
                    hint:
                        "When the browser can't play a video (AVI, WMV, HEVC pictures...), " +
                        'the player asks the server for it converted, from where it was. ' +
                        '“Always” converts every video, for browsers that play some files without picture or ' +
                        "sound. The player's ⟳ button converts the video on screen either way.",
                },
                {
                    key: 'streaming.maxStreams',
                    label: 'Videos converted at once',
                    type: 'number',
                    min: 1,
                    max: 32,
                    hint:
                        'For the whole server: each one keeps a processor core or more busy. ' +
                        'A viewer asking for one more is told to try again later.',
                },
                {
                    key: 'streaming.quality',
                    label: 'Quality (CRF)',
                    type: 'number',
                    min: 15,
                    max: 40,
                    hint:
                        'Lower is better and heavier: 18 looks like the original, 23 is a good balance, ' +
                        '28 is small.',
                },
                {
                    key: 'streaming.maxHeight',
                    label: 'Largest height (pixels)',
                    type: 'number',
                    min: 0,
                    max: 4320,
                    hint:
                        'Taller videos are scaled down to it while converted, ' +
                        'keeping their shape (1080 for Full HD). 0 keeps their size.',
                },
                {
                    key: 'streaming.copyVideo',
                    label: 'Copy H.264 pictures',
                    type: 'checkbox',
                    hint:
                        'Pictures already in H.264 (8-bit) are only repackaged, with their sound converted: ' +
                        'almost no work for the server, but a seek lands on the nearest earlier key frame and ' +
                        "the largest height doesn't apply.",
                },
            ],
        },
    ],
};
