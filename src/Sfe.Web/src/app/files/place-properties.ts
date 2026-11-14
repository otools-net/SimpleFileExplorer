import { Api } from '../core/api.service';
import { ClientConfig } from '../core/config.models';
import { ConnectionPlace, PlaceProperties } from '../core/connection.models';
import { LibraryItem } from '../core/models';
import { parentPath } from '../core/paths';

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

/** "Folder", or the file's extension and kind ("JPG image"). */
export function itemKind(item: LibraryItem): string {
    if (item.kind === 'directory') return 'Folder';
    const extension = item.extension ? `${item.extension.replace(/^\./, '').toUpperCase()} ` : '';
    const kinds: Record<string, string> = { image: 'image', video: 'video', audio: 'audio', document: 'document' };
    return `${extension}${kinds[item.kind] ?? 'file'}`;
}

/** The folder an item is in, from `root` (the library's or the connection's name). */
export function folderOf(path: string, root: string, itself: string): string {
    if (!path) return itself;
    const parent = parentPath(path);
    return parent ? `${root} / ${parent.split('/').join(' / ')}` : root;
}
