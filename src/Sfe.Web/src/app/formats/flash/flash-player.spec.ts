import { FlashEvents, FlashHandle, FlashMetadata, openFlash } from './flash-player';

describe('openFlash', () => {
    let container: HTMLElement;
    let events: { metadata: ReturnType<typeof vi.fn<(metadata: FlashMetadata) => void>>; state: ReturnType<typeof vi.fn<(playing: boolean) => void>>; escape: ReturnType<typeof vi.fn<() => void>> } & FlashEvents;

    function frame(): HTMLIFrameElement {
        return container.querySelector('iframe')!;
    }

    /** A message as the frame (or another window) posts it. */
    function message(data: unknown, source: Window | null = frame().contentWindow): void {
        window.dispatchEvent(new MessageEvent('message', { data, source, origin: 'null' }));
    }

    beforeEach(() => {
        container = document.createElement('div');
        document.body.appendChild(container);
        events = { metadata: vi.fn<(metadata: FlashMetadata) => void>(), state: vi.fn<(playing: boolean) => void>(), escape: vi.fn<() => void>() };
    });

    afterEach(() => container.remove());

    it('plays the movie in a frame of no origin, told what to play once it is ready', async () => {
        const opening = openFlash(container, 'http://sfe/api/files/flash/T/Run.swf', 0.5, events);
        const iframe = frame();
        const sent = vi.spyOn(iframe.contentWindow!, 'postMessage');

        expect(iframe.getAttribute('sandbox')).toBe('allow-scripts allow-pointer-lock');
        expect(iframe.getAttribute('sandbox')).not.toContain('allow-same-origin');
        expect(iframe.src).toBe(new URL('flash/player.html', document.baseURI).href);

        message({ type: 'ready' });
        expect(sent).toHaveBeenCalledWith({ type: 'load', url: 'http://sfe/api/files/flash/T/Run.swf', volume: 0.5 }, '*');
        message({ type: 'metadata', metadata: { width: 550 } });
        message({ type: 'loaded' });
        const handle = await opening;

        expect(events.metadata).toHaveBeenCalledWith({ width: 550 });
        handle.pause();
        handle.setVolume(0.2);
        expect(sent).toHaveBeenCalledWith({ type: 'pause' }, '*');
        expect(sent).toHaveBeenCalledWith({ type: 'volume', value: 0.2 }, '*');
        message({ type: 'state', playing: false });
        message({ type: 'key', key: 'Escape' });
        expect(events.state).toHaveBeenCalledWith(false);
        expect(events.escape).toHaveBeenCalled();

        handle.destroy();
        expect(container.querySelector('iframe'), 'removed: its sound stops').toBeNull();
    });

    it('hears only its own frame', async () => {
        let handle: FlashHandle | null = null;
        void openFlash(container, 'http://sfe/api/files/flash/T/Run.swf', 1, events).then(opened => handle = opened);

        message({ type: 'loaded' }, window);
        message({ type: 'state', playing: true }, null);
        await Promise.resolve();

        expect(handle).toBeNull();
        expect(events.state).not.toHaveBeenCalled();
        message({ type: 'loaded' });
        await Promise.resolve();
        expect(handle).not.toBeNull();
        handle!.destroy();
    });

    it('fails when Ruffle can not play the movie, and leaves nothing behind', async () => {
        const opening = openFlash(container, 'http://sfe/api/files/flash/T/Bad.swf', 1, events);

        message({ type: 'failed', message: 'Invalid SWF' });

        await expect(opening).rejects.toThrow('Invalid SWF');
        expect(container.querySelector('iframe')).toBeNull();
    });
});
