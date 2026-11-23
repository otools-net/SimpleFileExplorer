import { DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { Api, API_BASE, TAB_ID } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { KeepAliveState } from './library.models';
import { Rights } from '../core/rights';
import { ListingStore } from './listing.store';
import { FolderWriting, WritingProgress } from '../files/writing-progress';

/** Changes close together (a move touches two folders, a folder upload many) make one reload. */
const SETTLE = 400;
/** Nothing from the server for this long (it sends a `ping` every 25 s at the latest): the connection is lost. */
export const SILENCE = 60_000;
/** A drop shorter than this isn't shown: the feed ends every 10 minutes and the browser opens it again after 5 s. */
export const LOST_AFTER = 10_000;
/** The browser gave up (an error status, e.g. a proxy's 502 while the server is down): opened again after this. */
export const RETRY = 5_000;
/**
 * The tab hidden this long (another app, the screen off): the feed is closed, as its `ping` every 25 s kept a phone's
 * radio waking all day. Opened again when the tab shows; a quick switch to another app and back keeps it.
 */
export const HIDDEN_GRACE = 30_000;

export interface FolderChange {
    path: string;
    origin: string | null;
    /** The connection whose folder it is; absent for the library's. */
    connection?: number;
    /** Changed outside SFE (another program, the storage service's site), in a folder some tab watches. */
    outside?: boolean;
}

/**
 * The change feed (agents/docs/features/change-feed.md): the server tells each open listing that a folder was changed
 * through SFE elsewhere (another tab, another person, a move or copy job), or outside SFE in the folder this tab shows
 * (the library's or a connection's, named to the server: `watchFolder`); the folder listed is then refreshed in place.
 * The browser opens the feed again by itself when it ends or drops. It also tells how keep alive goes (`keepAlive`)
 * and whether the server answers (`serverLost`), for the indicator of the ⋮ menu, that the trash changed
 * (`trashChanges`), for the sidebar's Trash link, and how far items being written are (`WritingProgress`).
 */
@Injectable({ providedIn: 'root' })
export class ChangeFeed {
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly progress = inject(WritingProgress);
    private readonly api = inject(Api);
    /** The folder last named to the server for this feed (`watchedKey`); `undefined`: none since it opened. */
    private watched: string | null | undefined;
    private source: EventSource | null = null;
    private timer: ReturnType<typeof setTimeout> | undefined;
    private lostTimer: ReturnType<typeof setTimeout> | undefined;
    private watchdog: ReturnType<typeof setTimeout> | undefined;
    private retryTimer: ReturnType<typeof setTimeout> | undefined;
    private hiddenTimer: ReturnType<typeof setTimeout> | undefined;
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
        // The folder shown is followed for changes made outside SFE: named on each move (and when the feed opens).
        effect(() => {
            this.store.route();
            this.config.config();
            untracked(() => this.watch());
        });
        // A page left for another (kept by the browser for Back) gives its connection up: browsers allow 6 per server
        // over HTTP/1.1, and kept pages holding theirs made the next pages wait. Opened again when the page is back.
        const hidden = () => {
            clearTimeout(this.hiddenTimer);
            this.parked ||= this.source !== null;
            this.stop();
        };
        const shown = () => {
            if (this.parked) this.start();
            this.parked = false;
        };
        // Hidden for a while: closed too. What changed meanwhile wasn't told: the folder and the trash are read again.
        const visibility = () => {
            clearTimeout(this.hiddenTimer);
            if (!document.hidden) {
                if (this.parked && this.missed) shown();
            } else if (this.source) {
                this.hiddenTimer = setTimeout(() => {
                    this.missed = true;
                    hidden();
                }, HIDDEN_GRACE);
            }
        };
        window.addEventListener('pagehide', hidden);
        window.addEventListener('pageshow', shown);
        document.addEventListener('visibilitychange', visibility);
        inject(DestroyRef).onDestroy(() => {
            window.removeEventListener('pagehide', hidden);
            window.removeEventListener('pageshow', shown);
            document.removeEventListener('visibilitychange', visibility);
            clearTimeout(this.hiddenTimer);
            this.stop();
        });
    }

    /** The feed was open when the page was left (`pagehide`) or hidden for a while: opened again when it shows. */
    private parked = false;
    /** Closed while the tab was hidden: changes may have been missed, read again once open. */
    private missed = false;

    /** Opens the feed (once), where the browser has EventSource and the session may browse. */
    start(): void {
        if (this.source || typeof EventSource === 'undefined' || !this.config.can(Rights.browse)) return;
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
        this.watched = undefined;
        this.serverLost.set(false);
    }

    private open(): void {
        const source = (this.source = new EventSource(`${API_BASE}files/changes?tab=${TAB_ID}`));
        source.addEventListener('open', () => this.opened());
        source.addEventListener('error', () => this.dropped(source));
        source.addEventListener('ping', () => this.heard());
        source.addEventListener('changed', (event) => {
            this.heard();
            this.changed(JSON.parse((event as MessageEvent<string>).data) as FolderChange);
        });
        source.addEventListener('writing', (event) => {
            this.heard();
            this.writing(JSON.parse((event as MessageEvent<string>).data) as FolderWriting);
        });
        source.addEventListener('trash', () => {
            this.heard();
            this.trashChanges.update((n) => n + 1);
        });
        source.addEventListener('keepAlive', (event) => {
            this.heard();
            this.keepAlive.set((JSON.parse((event as MessageEvent<string>).data) as { state: KeepAliveState }).state);
        });
        this.heard();
    }

    /**
     * Open again: back to normal; after a loss, the folder listed is read again (changes made meanwhile weren't told).
     */
    private opened(): void {
        // A feed opened again follows nothing until told.
        this.watched = undefined;
        this.watch();
        clearTimeout(this.lostTimer);
        this.lostTimer = undefined;
        this.heard();
        if (this.missed) {
            this.missed = false;
            this.trashChanges.update((n) => n + 1);
            void this.store.refresh();
        } else if (this.serverLost()) {
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

    /**
     * A change of the folder listed (the library's, or the connection's shown), made elsewhere: refreshed once the
     * changes settle. Listeners hear the library's folders only.
     */
    changed(change: FolderChange): void {
        if (change.outside && !this.live()) return;
        const connection = change.connection ?? null;
        if (change.origin !== TAB_ID && connection === null) this.listeners.forEach((listener) => listener(change));
        const route = this.store.route();
        if (change.origin === TAB_ID || route.q || change.path !== route.path) return;
        if (connection !== (route.c ?? null)) return;
        this.settle();
    }

    /**
     * Items being written in a library folder (any tab's, any person's it may see): their progress kept for the bars;
     * the folder listed is refreshed when one isn't listed yet or one ended (it took its name, or went).
     */
    writing(change: FolderWriting): void {
        const { started, ended } = this.progress.update(change);
        const route = this.store.route();
        if (route.q || route.c != null || change.path !== route.path) return;
        const listed = new Set(this.store.items().map((item) => item.name));
        if (ended.length > 0 || started.some((name) => !listed.has(name))) this.settle();
    }

    /** Names the folder shown to the server, when the feed is open and it is another. */
    private watch(): void {
        if (!this.source || this.source.readyState !== EventSource.OPEN) return;
        const folder = this.shownFolder();
        const key = folder && `${folder.connection ?? ''}:${folder.path}`;
        if (key === this.watched || (key === null && this.watched === undefined)) return;
        this.watched = key;
        this.api.watchFolder(folder?.path ?? null, folder?.connection ?? null).catch(() => {
            // Named again on the next move.
            if (this.watched === key) this.watched = undefined;
        });
    }

    /**
     * The folder listed (the library's, or a connection's: `connection`), when changes made outside SFE are followed;
     * null otherwise.
     */
    private shownFolder(): { path: string; connection: number | null } | null {
        const route = this.store.route();
        return this.live() && !route.q ? { path: route.path, connection: route.c ?? null } : null;
    }

    /** The user follows changes made outside SFE (`liveChanges`, on unless switched off). */
    private live(): boolean {
        return this.config.config()?.liveChanges !== false;
    }

    /** The folder listed is refreshed once the changes settle. */
    private settle(): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => void this.store.refresh(), SETTLE);
    }
}
