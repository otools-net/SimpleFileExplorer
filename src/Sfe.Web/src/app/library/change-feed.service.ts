import { DestroyRef, inject, Injectable } from '@angular/core';
import { TAB_ID } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { ListingStore } from './listing.store';

/** Changes close together (a move touches two folders, a folder upload many) make one reload. */
const SETTLE = 400;

interface FolderChange {
    path: string;
    origin: string | null;
}

/**
 * The change feed (agents/docs/features/change-feed.md): the server tells each open listing that a folder was changed
 * through SFE elsewhere (another tab, another person, a move or copy job); the folder listed is then refreshed in
 * place. The browser opens the feed again by itself when it ends or drops.
 */
@Injectable({ providedIn: 'root' })
export class ChangeFeed {
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private source: EventSource | null = null;
    private timer: ReturnType<typeof setTimeout> | undefined;

    constructor() {
        inject(DestroyRef).onDestroy(() => this.stop());
    }

    /** Opens the feed (once), where the browser has EventSource and the session may browse. */
    start(): void {
        if (this.source || typeof EventSource === 'undefined' || !this.config.can(Rights.browse))
            return;
        this.source = new EventSource('api/files/changes');
        this.source.addEventListener('changed', event => this.changed(JSON.parse((event as MessageEvent<string>).data) as FolderChange));
    }

    stop(): void {
        clearTimeout(this.timer);
        this.source?.close();
        this.source = null;
    }

    /** A change of the folder listed, made elsewhere: refreshed once the changes settle. */
    changed(change: FolderChange): void {
        const route = this.store.route();
        if (change.origin === TAB_ID || route.q || change.path !== route.path)
            return;
        clearTimeout(this.timer);
        this.timer = setTimeout(() => void this.store.refresh(), SETTLE);
    }
}
