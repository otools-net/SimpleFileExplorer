import { InjectionToken } from '@angular/core';

/** What Ruffle reads from the movie's header. */
export interface FlashMetadata {
    readonly width: number;
    readonly height: number;
    readonly frameRate: number;
    readonly numFrames: number;
    readonly swfVersion: number;
    readonly isActionScript3: boolean;
    readonly backgroundColor: string | null;
}

export interface FlashEvents {
    metadata(metadata: FlashMetadata): void;
    /** The movie plays or is paused (from the view's buttons, or Ruffle's own menu). */
    state(playing: boolean): void;
    /** Esc pressed inside the movie (frames keep their keys). */
    escape(): void;
}

export interface FlashHandle {
    play(): void;
    pause(): void;
    /** 0–1. */
    setVolume(volume: number): void;
    /** Plays the movie again from its start. */
    restart(): void;
    /** The movie takes the keyboard. */
    focus(): void;
    readonly frame: HTMLIFrameElement;
    destroy(): void;
}

/**
 * Plays the movie at `url` (an app's flash link, absolute) in a new frame inside `container`; resolves once Ruffle
 * has it, rejects when it can't.
 */
export type FlashLoader = (container: HTMLElement, url: string, volume: number, events: FlashEvents) => Promise<FlashHandle>;

/** Ruffle and its frame: replaced in the tests (jsdom runs no WebAssembly). */
export const FLASH_LOADER = new InjectionToken<FlashLoader>('FLASH_LOADER', { providedIn: 'root', factory: () => openFlash });

/** Ruffle and the movie may take this long to arrive (the WebAssembly is ~5 MB the first time). */
export const LOAD_TIMEOUT_MS = 120_000;

/**
 * The frame's page (`flash/player.html`): sandboxed without `allow-same-origin` (the server's policy says so too), so
 * it has no origin: neither Ruffle nor a movie can send the session's cookie or reach this page.
 */
export function createFrame(): HTMLIFrameElement {
    const frame = document.createElement('iframe');
    frame.className = 'flash-frame';
    frame.setAttribute('sandbox', 'allow-scripts allow-pointer-lock');
    frame.setAttribute('allow', 'autoplay; fullscreen');
    frame.setAttribute('referrerpolicy', 'no-referrer');
    frame.title = 'Flash movie';
    frame.src = new URL('flash/player.html', document.baseURI).href;
    return frame;
}

/** The frame's messages, from that frame only (its origin is "null": the source tells it apart). */
export const openFlash: FlashLoader = (container, url, volume, events) => new Promise<FlashHandle>((resolve, reject) => {
    const frame = createFrame();
    const send = (message: object) => frame.contentWindow?.postMessage(message, '*');
    let settled = false;
    const timeout = setTimeout(() => fail(new Error('Ruffle did not start in time')), LOAD_TIMEOUT_MS);

    const handle: FlashHandle = {
        frame,
        play: () => send({ type: 'play' }),
        pause: () => send({ type: 'pause' }),
        setVolume: value => send({ type: 'volume', value }),
        restart: () => send({ type: 'load', url, volume }),
        focus: () => {
            frame.focus();
            send({ type: 'focus' });
        },
        destroy: () => {
            clearTimeout(timeout);
            window.removeEventListener('message', onMessage);
            // Removed at once: the movie's sound stops with it.
            frame.remove();
        },
    };

    function fail(error: Error): void {
        if (settled)
            return;
        settled = true;
        handle.destroy();
        reject(error);
    }

    function onMessage(event: MessageEvent): void {
        if (event.source !== frame.contentWindow || !event.data || typeof event.data !== 'object')
            return;
        const message = event.data as { type?: unknown; [key: string]: unknown };
        switch (message.type) {
            case 'ready':
                send({ type: 'load', url, volume });
                break;
            case 'loaded':
                if (!settled) {
                    settled = true;
                    clearTimeout(timeout);
                    resolve(handle);
                }
                break;
            case 'failed':
                fail(new Error(String(message['message'] ?? 'Ruffle could not play it')));
                break;
            case 'metadata':
                events.metadata(message['metadata'] as FlashMetadata);
                break;
            case 'state':
                events.state(message['playing'] === true);
                break;
            case 'key':
                if (message['key'] === 'Escape')
                    events.escape();
                break;
        }
    }

    window.addEventListener('message', onMessage);
    container.appendChild(frame);
});
