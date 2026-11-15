import { signal } from '@angular/core';
import { ApiError } from '../../core/api.service';
import { LibraryItem } from '../../core/models';
import { SearchCoverage, SearchResults } from '../library.models';

/** A place a search runs in: the library (`c` null) or a connection. */
export interface EverywhereLocation {
    c: number | null;
    name: string;
}

/** A page of one location's results, and how much of the location its index holds (connections). */
export type EverywherePage = SearchResults & { coverage?: SearchCoverage };

/** Reads a page of a location's results: the first one (`after` null), or the one after `after`. */
export type EverywhereFetch = (
    location: EverywhereLocation,
    after: string | null,
    signal: AbortSignal,
) => Promise<EverywherePage>;

/** One location's results as the page shows them: loaded apart, so a slow or failing location holds no other. */
export interface EverywhereGroup {
    location: EverywhereLocation;
    status: 'loading' | 'ready' | 'error';
    items: LibraryItem[];
    /** Matches counted with the first page; `capped`: at least that many. */
    total: number | null;
    capped: boolean;
    /** The cursor of the next page; null on the last one. */
    next: string | null;
    coverage: SearchCoverage | null;
    /** What went wrong (`error` status), or why more could not be loaded. */
    error: string | null;
    /** A next page is being read. */
    loadingMore: boolean;
}

/**
 * The search of every location, each apart: one request per location, each group paged alone ("more" on demand).
 * `start` begins a new search (the previous one's requests cancelled).
 */
export class EverywhereSearch {
    readonly groups = signal<EverywhereGroup[]>([]);

    private requests = new AbortController();

    constructor(private readonly fetch: EverywhereFetch) {}

    /** Searches `locations` at once, each group filled as its answer comes. */
    start(locations: EverywhereLocation[]): void {
        this.stop();
        this.groups.set(locations.map((location) => emptyGroup(location)));
        for (const location of locations) void this.first(location);
    }

    /** Cancels the requests under way. */
    stop(): void {
        this.requests.abort();
        this.requests = new AbortController();
    }

    /** Reads the group's first page again (after an error). */
    retry(location: EverywhereLocation): Promise<void> {
        this.update(location, () => emptyGroup(location));
        return this.first(location);
    }

    /** Appends the group's next page. */
    async more(location: EverywhereLocation): Promise<void> {
        const group = this.find(location);
        if (!group?.next || group.loadingMore) return;
        const signal = this.requests.signal;
        this.update(location, (g) => ({ ...g, loadingMore: true, error: null }));
        try {
            const page = await this.fetch(location, group.next, signal);
            if (signal.aborted) return;
            this.update(location, (g) => ({
                ...g,
                items: [...g.items, ...page.items.filter((item) => !g.items.some((had) => had.path === item.path))],
                next: page.hasMore ? page.next : null,
                loadingMore: false,
            }));
        } catch (error) {
            if (!signal.aborted) this.update(location, (g) => ({ ...g, loadingMore: false, error: messageOf(error) }));
        }
    }

    private async first(location: EverywhereLocation): Promise<void> {
        const signal = this.requests.signal;
        try {
            const page = await this.fetch(location, null, signal);
            if (signal.aborted) return;
            this.update(location, (g) => ({
                ...g,
                status: 'ready',
                items: page.items,
                total: page.total ?? page.items.length,
                capped: Boolean(page.totalCapped),
                next: page.hasMore ? page.next : null,
                coverage: page.coverage ?? null,
            }));
        } catch (error) {
            if (!signal.aborted) this.update(location, (g) => ({ ...g, status: 'error', error: messageOf(error) }));
        }
    }

    private find(location: EverywhereLocation): EverywhereGroup | undefined {
        return this.groups().find((group) => group.location.c === location.c);
    }

    private update(location: EverywhereLocation, change: (group: EverywhereGroup) => EverywhereGroup): void {
        this.groups.update((groups) => groups.map((g) => (g.location.c === location.c ? change(g) : g)));
    }
}

function emptyGroup(location: EverywhereLocation): EverywhereGroup {
    return {
        location,
        status: 'loading',
        items: [],
        total: null,
        capped: false,
        next: null,
        coverage: null,
        error: null,
        loadingMore: false,
    };
}

/** What a failed request says, in words for the group. */
function messageOf(error: unknown): string {
    if (error instanceof ApiError) {
        if (error.status === 429) return 'Busy for now: try again in a moment.';
        if (error.status === 0) return 'The server could not be reached.';
        return error.message || `The search failed (${error.status}).`;
    }
    return String(error);
}
