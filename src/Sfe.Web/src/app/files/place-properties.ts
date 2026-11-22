import { Api } from '../core/api.service';
import { ClientConfig } from '../core/config.models';
import { ConnectionPlace, PlaceProperties } from '../core/connection.models';

/*
 * Properties of a connection's item (`?c=`, agents/docs/features/connections.md): what its provider tells, read
 * through the connection's route; nothing of the library (index, tags, counts, checksums) applies there.
 */

/** `GET api/connections/{id}/files/properties`. */
export function placeProperties(api: Api, place: ConnectionPlace, path: string): Promise<PlaceProperties> {
    return api.connectionGet<PlaceProperties>(place.id, 'properties', { path });
}

/** The provider's name for people ("Dropbox"), or its kind when the configuration does not name it. */
export function providerName(config: ClientConfig | null, kind: string): string {
    return config?.connections?.kinds.find((k) => k.kind === kind)?.name ?? kind;
}
