import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionPlace } from '../core/connection.models';
import { DirectoryListing } from '../core/models';
import { setRawFormatsOnly } from '../formats/formats';
import { SortField } from './library.models';

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
    return page.listing;
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
    setRawFormatsOnly(place !== null);
}
