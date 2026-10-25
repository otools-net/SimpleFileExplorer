// The Flash viewer's frame (agents/docs/features/flash-viewer.md): Ruffle's settings, and the messages with the app
// around it (formats/flash/flash-player.ts). The page has no origin: it hears only its parent, and plays only movies
// of the app's flash links.
(() => {
    'use strict';

    const post = message => parent.postMessage(message, '*');

    window.RufflePlayer = window.RufflePlayer || {};
    window.RufflePlayer.config = {
        // Nothing replaced on its own: the app says what to play.
        polyfills: false,
        autoplay: 'on',
        unmuteOverlay: 'visible',
        splashScreen: false,
        letterbox: 'on',
        contextMenu: 'rightClickOnly',
        // A movie neither calls the page, nor opens addresses, nor opens sockets.
        allowScriptAccess: false,
        allowNetworking: 'internal',
        openUrlMode: 'deny',
        socketProxy: [],
        upgradeToHttps: false,
        warnOnUnsupportedContent: true,
        showSwfDownload: false,
        logLevel: 'error',
    };

    let player = null;
    let playing = null;

    function report() {
        const now = !!player && player.ruffle().isPlaying;
        if (now !== playing) {
            playing = now;
            post({ type: 'state', playing: now });
        }
    }

    function volumeOf(value) {
        return typeof value === 'number' && value >= 0 && value <= 1 ? value : 1;
    }

    function load(message) {
        let url;
        try {
            url = new URL(message.url);
        } catch {
            return post({ type: 'failed', message: 'not an address' });
        }
        // Only the app's flash links (the frame's own address is the app's, though it has no origin).
        if (url.origin !== location.origin || !url.pathname.includes('/api/files/flash/'))
            return post({ type: 'failed', message: 'not a movie of the library' });

        player?.remove();
        playing = null;
        player = window.RufflePlayer.newest().createPlayer();
        document.body.append(player);
        player.addEventListener('loadedmetadata', () => {
            const metadata = player.ruffle().metadata;
            post({
                type: 'metadata',
                metadata: {
                    width: metadata.width,
                    height: metadata.height,
                    frameRate: metadata.frameRate,
                    numFrames: metadata.numFrames,
                    swfVersion: metadata.swfVersion,
                    isActionScript3: metadata.isActionScript3,
                    backgroundColor: metadata.backgroundColor ?? null,
                },
            });
        });
        player.ruffle().volume = volumeOf(message.volume);
        // Relative addresses of the movie (its levels, music...) are its folder's, not this page's.
        player.ruffle().load({ url: url.href, base: new URL('.', url).href }).then(
            () => {
                post({ type: 'loaded' });
                report();
            },
            error => post({ type: 'failed', message: String(error?.message ?? error) }));
    }

    addEventListener('message', event => {
        const message = event.data;
        if (event.source !== parent || !message || typeof message !== 'object')
            return;
        switch (message.type) {
            case 'load':
                load(message);
                break;
            case 'play':
                player?.ruffle().play();
                break;
            case 'pause':
                player?.ruffle().pause();
                break;
            case 'volume':
                if (player)
                    player.ruffle().volume = volumeOf(message.value);
                break;
            case 'focus':
                player?.focus();
                break;
        }
        report();
    });

    // Esc in the movie reaches the viewer (which closes on the next one), unless it leaves full screen.
    addEventListener('keydown', event => {
        if (event.key === 'Escape' && !document.fullscreenElement)
            post({ type: 'key', key: 'Escape' });
    }, true);

    // Ruffle pauses and plays from its own menu too.
    setInterval(report, 500);
    addEventListener('DOMContentLoaded', () => post({ type: 'ready' }));
})();
