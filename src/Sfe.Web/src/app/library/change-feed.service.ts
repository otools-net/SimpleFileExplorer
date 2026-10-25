import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { TAB_ID } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { KeepAliveState } from '../core/models';
import { Rights } from '../core/rights';
import { ListingStore } from './listing.store';

/** Changes close together (a move touches two folders, a folder upload many) make one reload. */
const SETTLE = 400;
/** Nothing from the server for this long (it sends a `ping` every 25 s at the latest): the connection is lost. */
export const SILENCE = 60_000;
/** A drop shorter than this isn't shown: the feed ends every 10 minutes and the browser opens it again after 5 s. */
export const LOST_AFTER = 10_000;
/** The browser gave up (an error status, e.g. a proxy's 502 while the server is down): opened again after this. */
export const RETRY = 5_000;

export interface FolderChange {
    path: string;
    origin: string | null;
}

/**
 * The change feed (agents/docs/features/change-feed.md): the server tells each open listing that a folder was changed
 * through SFE elsewhere (another tab, another person, a move or copy job); the folder listed is then refreshed in
 * place. The browser opens the feed again by itself when it ends or drops. It also tells how keep alive goes
 * (`keepAlive`) and whether the server answers (`serverLost`), for the indicator of the ⋮ menu, and that the trash
 * changed (`trashChanges`), for the sidebar's Trash link.
 */
@Injectable({ providedIn: 'root' })
export class ChangeFeed {
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private source: EventSource | null = null;
    private timer: ReturnType<typeof setTimeout> | undefined;
    private lostTimer: ReturnType<typeof setTimeout> | undefined;
    private watchdog: ReturnType<typeof setTimeout> | undefined;
    private retryTimer: ReturnType<typeof setTimeout> | undefined;
    private readonly listeners = new Set<(change: FolderChange) => void>();

    /** How keep alive goes in the space: sent first when the feed opens, then each time it changes. */
    readonly keepAlive = signal<KeepAliveState>('none');

    /** Counts the `trash` events: something came into or left the trash (sent to those who may delete). */
    readonly trashChanges = signal(0);

    /**
     * The server doesn't answer: the feed dropped and didn't come back within `LOST_AFTER`, or nothing came through it
     * for `SILENCE` (a server or network that hangs without closing the connection). Cleared when it opens again.
     */
    readonly serverLost = signal(false);

    constructor() {
        // A page left for another (kept by the browser for Back) gives its connection up: browsers allow 6 per server
        // over HTTP/1.1, and kept pages holding theirs made the next pages wait. Opened again when the page is back.
        const hidden = () => {
            this.parked = this.source !== null;
            this.stop();
        };
        const shown = () => {
            if (this.parked)
                this.start();
            this.parked = false;
        };
        window.addEventListener('pagehide', hidden);
        window.addEventListener('pageshow', shown);
        inject(DestroyRef).onDestroy(() => {
            window.removeEventListener('pagehide', hidden);
            window.removeEventListener('pageshow', shown);
            this.stop();
        });
    }

    /** The feed was open when the page was left (`pagehide`): opened again when it shows. */
    private parked = false;

    /** Opens the feed (once), where the browser has EventSource and the session may browse. */
    start(): void {
        if (this.source || typeof EventSource === 'undefined' || !this.config.can(Rights.browse))
            return;
        this.open();
    }

    stop(): void {
        clearTimeout(this.timer);
        clearTimeout(this.lostTimer);
        clearTimeout(this.watchdog);
        clearTimeout(this.retryTimer);
        this.lostTimer = this.retryTimer = undefined;
        this.source?.close();
        this.source = null;
        this.serverLost.set(false);
    }

    private open(): void {
        const source = this.source = new EventSource('api/files/changes');
        source.addEventListener('open', () => this.opened());
        source.addEventListener('error', () => this.dropped(source));
        source.addEventListener('ping', () => this.heard());
        source.addEventListener('changed', event => {
            this.heard();
            this.changed(JSON.parse((event as MessageEvent<string>).data) as FolderChange);
        });
        source.addEventListener('trash', () => {
            this.heard();
            this.trashChanges.update(n => n + 1);
        });
        source.addEventListener('keepAlive', event => {
            this.heard();
            this.keepAlive.set((JSON.parse((event as MessageEvent<string>).data) as { state: KeepAliveState }).state);
        });
        this.heard();
    }

    /** Open again: back to normal; after a loss, the folder listed is read again (changes made meanwhile weren't told). */
    private opened(): void {
        clearTimeout(this.lostTimer);
        this.lostTimer = undefined;
        this.heard();
        if (this.serverLost()) {
            this.serverLost.set(false);
            void this.store.refresh();
        }
    }

    private dropped(source: EventSource): void {
        this.lostTimer ??= setTimeout(() => this.serverLost.set(true), LOST_AFTER);
        // The browser tries again by itself, except after an error status: then here.
        if (source.readyState === EventSource.CLOSED && source === this.source) {
            clearTimeout(this.retryTimer);
            this.retryTimer = setTimeout(() => this.reopen(), RETRY);
        }
    }

    /** Something came: the watchdog starts again. */
    private heard(): void {
        clearTimeout(this.watchdog);
        this.watchdog = setTimeout(() => {
            this.serverLost.set(true);
            this.reopen();
        }, SILENCE);
    }

    private reopen(): void {
        this.retryTimer = undefined;
        this.source?.close();
        this.open();
    }

    /**
     * Tells `listener` of the folders changed elsewhere (not by this tab): an open file in the viewer checks whether it
     * is one of them. Returns the way to stop.
     */
    listen(listener: (change: FolderChange) => void): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }

    /** A change of the folder listed, made elsewhere: refreshed once the changes settle. */
    changed(change: FolderChange): void {
        if (change.origin !== TAB_ID)
            this.listeners.forEach(listener => listener(change));
        const route = this.store.route();
        if (change.origin === TAB_ID || route.q || change.path !== route.path)
            return;
        clearTimeout(this.timer);
        this.timer = setTimeout(() => void this.store.refresh(), SETTLE);
    }
}
