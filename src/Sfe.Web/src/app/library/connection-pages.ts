import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionListing, ConnectionPlace } from '../core/connection.models';
import { DirectoryListing } from '../core/models';
import { setPlaceFormatsOnly } from '../formats/formats';
import { LibraryRoute } from './library-route';
import { ConnectionSearchResults, SearchAsk, SearchCoverage, SearchResults, SortField } from './library.models';

/**
 * A page of a connection's folder for the listing (`?c=`): the library's listing shape, and the connection made the
 * `place` of the configuration, so the files' addresses and what can be done there follow it.
 */
export async function connectionPage(
    api: Api,
    config: ConfigService,
    id: number,
    query: { path: string; offset: number; limit: number; sort: SortField; desc: boolean },
    signal: AbortSignal,
): Promise<DirectoryListing> {
    const page = await api.connectionBrowse(id, query, signal);
    usePlace(config, id, page);
    return page.listing;
}

/**
 * A page of the search of the route: in the connection listed (`c`) or the library; nothing for a search everywhere,
 * whose locations are searched apart (`EverywhereComponent`).
 */
export function searchPage(
    api: Api,
    config: ConfigService,
    route: LibraryRoute,
    ask: SearchAsk,
    signal: AbortSignal,
): Promise<SearchResults> {
    if (route.all)
        return Promise.resolve({ query: ask.q, path: '', items: [], limit: ask.limit, hasMore: false, next: null });
    return route.c ? connectionSearchPage(api, config, route.c, ask, signal) : api.search(ask, signal);
}

/**
 * A page of a search of a connection's files (`?c=&q=`), from the library's index: the library's search shape, the
 * connection made the `place` as for its folders. `coverage` says how much of it the index holds.
 */
export async function connectionSearchPage(
    api: Api,
    config: ConfigService,
    id: number,
    query: SearchAsk,
    signal: AbortSignal,
): Promise<SearchResults & { coverage: SearchCoverage }> {
    const page = await api.connectionSearch(id, query, signal);
    usePlace(config, id, page);
    return { ...page.results, coverage: page.coverage };
}

/** The connection a page came from made the configuration's place (unchanged when it is already). */
function usePlace(config: ConfigService, id: number, page: ConnectionListing | ConnectionSearchResults): void {
    const flag = (name: string) => page.capabilities[name] === true;
    const next: ConnectionPlace = {
        id,
        name: page.name,
        thumbnails: flag('thumbnails'),
        access: page.access,
        serverSideMove: flag('serverSideMove'),
        serverSideCopy: flag('serverSideCopy'),
        ownTrash: flag('ownTrash'),
        uploadsInParts: flag('uploadsInParts'),
    };
    const place = config.place();
    if (!place || (Object.keys(next) as (keyof ConnectionPlace)[]).some((key) => place[key] !== next[key]))
        setPlace(config, next);
}

/** The place of a page opened on a connection's file (`?c=` of the viewer and player pages) before it is listed. */
export function enterPlace(config: ConfigService, id: number | null): void {
    if (!id) {
        if (config.place()) setPlace(config, null);
    } else if (config.place()?.id !== id) {
        setPlace(config, {
            id,
            name: '',
            thumbnails: false,
            access: 'read',
            serverSideMove: false,
            serverSideCopy: false,
            ownTrash: false,
            uploadsInParts: false,
        });
    }
}

function setPlace(config: ConfigService, place: ConnectionPlace | null): void {
    config.place.set(place);
    setPlaceFormatsOnly(place !== null);
}
