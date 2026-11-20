import type { SettingTab } from '../settings-schema';

/**
 * The Player and Streaming tabs of the settings (Playback): the video player, the external player, Flash, the
 * data server and videos converted by the server.
 */

// Players that a web page can open a file in through a link they register, for the "External player" choice:
// [label, name on the button, link]. Android has no custom schemes for these: intent: links name the app's package.
// The names on the buttons are the apps' own: not translated.
export const EXTERNAL_PLAYERS: [string, string, string][] = [
    [$localize`VLC (iPhone, iPad; on a computer only with a vlc:// handler installed)`, 'VLC', 'vlc://{url}'],
    [$localize`VLC (Android)`, 'VLC', 'intent:{url}#Intent;package=org.videolan.vlc;type=video/*;end'],
    [
        $localize`MX Player (Android)`,
        'MX Player',
        'intent:{url}#Intent;package=com.mxtech.videoplayer.ad;type=video/*;end',
    ],
    [
        $localize`MX Player Pro (Android)`,
        'MX Player',
        'intent:{url}#Intent;package=com.mxtech.videoplayer.pro;type=video/*;end',
    ],
    [$localize`PotPlayer (Windows)`, 'PotPlayer', 'potplayer://{url}'],
    [$localize`Infuse (iPhone, iPad, Mac)`, 'Infuse', 'infuse://x-callback-url/play?url={encodedUrl}'],
    [$localize`IINA (macOS)`, 'IINA', 'iina://weblink?url={encodedUrl}'],
];

export const PLAYER_SETTINGS: SettingTab = {
    id: 'player',
    title: $localize`Player`,
    groups: [
        {
            title: $localize`Video player`,
            fields: [
                {
                    key: 'player.seekSeconds',
                    label: $localize`Jump back / forward (seconds)`,
                    type: 'number',
                    min: 1,
                    max: 600,
                    hint: $localize`How far the player's back and forward buttons and the J and L keys jump.`,
                    help: $localize`A double tap on the left or right of the picture jumps by the same amount, and \
each further tap jumps again.`,
                },
                {
                    key: 'player.loop',
                    label: $localize`Loop videos`,
                    type: 'checkbox',
                    hint: $localize`Videos start over when they end. The player's loop button (R) switches it for \
the video on screen.`,
                },
            ],
        },
        {
            title: $localize`External player`,
            fields: [
                {
                    key: 'player.externalPreset',
                    label: $localize`External player`,
                    type: 'preset',
                    presets: EXTERNAL_PLAYERS,
                    fills: { url: 'player.externalUrl', name: 'player.externalName' },
                    hint: $localize`The app that the viewer's ▶ button opens videos and audio in, on every \
browser.`,
                    help: $localize`The app must be installed on the device you watch on, and that device must be \
able to reach this server.`,
                },
                {
                    key: 'player.externalUrl',
                    label: $localize`External player link`,
                    type: 'text',
                    placeholder: $localize`Empty: no external player button`,
                    hint: $localize`Filled in by the choice above, or written by hand.`,
                    help: $localize`{url} is replaced by the file's address, and {encodedUrl} by the same address \
encoded for a query string. Without either, the address is added at the end (e.g. vlc://).`,
                },
                {
                    key: 'player.externalName',
                    label: $localize`External player name`,
                    type: 'text',
                    placeholder: $localize`external player`,
                    hint: $localize`Shown on its button, as in “Open in VLC”.`,
                },
                {
                    key: 'player.linkLifetimeMinutes',
                    label: $localize`Link lifetime (minutes)`,
                    type: 'number',
                    min: 1,
                    max: 10080,
                    hint: $localize`How long the link given to the player works, without signing in.`,
                    help: $localize`Make it long enough to watch a whole film: players ask again each time you \
seek. A new password or a disabled account ends the links it gave out.`,
                },
            ],
        },
        {
            title: $localize`Flash movies`,
            fields: [
                {
                    key: 'player.flash',
                    label: $localize`Play Flash movies`,
                    type: 'checkbox',
                    hint: $localize`Flash movies and games (.swf) play in the viewer. Off: .swf files can only be \
downloaded.`,
                    help: $localize`They play through Ruffle, a Flash Player emulator that comes with SFE. A movie \
can only read the files of its own folder, and never acts as the person watching it.`,
                },
            ],
        },
    ],
};

export const STREAMING_SETTINGS: SettingTab = {
    id: 'streaming',
    title: $localize`Streaming`,
    groups: [
        {
            title: $localize`Data server`,
            fields: [
                {
                    key: 'dataServer',
                    label: $localize`Data server`,
                    type: 'text',
                    placeholder: $localize`Empty: SFE streams the files`,
                    hint: $localize`The http:// or https:// address of another web server that already publishes \
the library. Files then open from there instead of being streamed by SFE.`,
                    help: $localize`The file's path is added after the address, or put where you write {url} \
(e.g. http://nas/files/{url}?raw=1). {encodedUrl} is the same path fully percent-encoded, slashes too \
(e.g. http://nas/get?file={encodedUrl}).`,
                },
            ],
        },
        {
            title: $localize`Videos converted by the server`,
            hint: $localize`These need ffmpeg on the server (System › About says whether it was found). \
Playback › Video streams lists the videos being converted.`,
            fields: [
                {
                    key: 'streaming.mode',
                    label: $localize`Convert videos`,
                    type: 'select',
                    options: [
                        ['fallback', $localize`When the browser can't play them`],
                        ['always', $localize`:convert videos:Always`],
                        ['off', $localize`:convert videos:Never`],
                    ],
                    hint: $localize`When the browser can't play a video (AVI, WMV, HEVC pictures…), the player asks \
the server for it converted, from where it was.`,
                    help: $localize`Converted videos play as H.264 with AAC sound, which every browser plays. \
“Always” converts every video, for browsers that play some files without picture or sound. The player's ⟳ button \
converts the video on screen either way. A conversion keeps the server's processor busy while someone watches.`,
                },
                {
                    key: 'streaming.maxStreams',
                    label: $localize`Videos converted at once`,
                    type: 'number',
                    min: 1,
                    max: 32,
                    hint: $localize`For the whole server. Each one keeps a processor core or more busy.`,
                    help: $localize`Someone asking for one more is told to try again later.`,
                },
                {
                    key: 'streaming.quality',
                    label: $localize`Quality (CRF)`,
                    type: 'number',
                    min: 15,
                    max: 40,
                    hint: $localize`Lower looks better and is heavier: 18 looks like the original, 23 is a good \
balance, 28 is small.`,
                },
                {
                    key: 'streaming.maxHeight',
                    label: $localize`Largest height (pixels)`,
                    type: 'number',
                    min: 0,
                    max: 4320,
                    hint: $localize`Taller videos are scaled down to it, keeping their shape (1080 for Full HD). \
0 keeps their size.`,
                },
                {
                    key: 'streaming.copyVideo',
                    label: $localize`Copy H.264 pictures`,
                    type: 'checkbox',
                    hint: $localize`Pictures already in H.264 (8-bit) are only repackaged, with their sound \
converted: almost no work for the server.`,
                    help: $localize`The catch: a seek lands on the nearest earlier key frame, and the largest \
height doesn't apply.`,
                },
            ],
        },
    ],
};
