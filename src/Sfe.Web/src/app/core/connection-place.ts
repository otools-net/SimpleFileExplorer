import { ClientConfig } from './config.models';
import { ConnectionPlace } from './connection.models';
import { Rights } from './rights';

/**
 * What a connection's files always offer: browsing, viewing, downloading; the library's tags on them (kept by the
 * library's index of the connection, at any access: tagging changes no file there). No default tags of folders.
 */
const PLACE_RIGHTS: ReadonlySet<string> = new Set([
    Rights.browse,
    Rights.viewFiles,
    Rights.download,
    Rights.viewTags,
    Rights.assignTags,
    Rights.manageTags,
]);

/**
 * What they offer too: copies out of any connection; where the session may write there, new folders, renames,
 * deletes, moves and copies, uploads when the provider takes files in parts. No new files, extract, compress,
 * permanent delete or edits yet.
 */
function placeRights(place: ConnectionPlace): ReadonlySet<string> {
    // A copy only reads its source: copying out of a connection one reads (into the library) is allowed.
    if (place.access !== 'write') return new Set([...PLACE_RIGHTS, Rights.copyFiles]);
    const rights = new Set([...PLACE_RIGHTS, Rights.createFolders, Rights.renameFiles, Rights.deleteFiles]);
    // Between places the server streams the bytes; inside the connection it needs the provider's own move or copy
    // (`files/transfer` answers connection-not-supported without it).
    rights.add(Rights.moveFiles).add(Rights.copyFiles);
    if (place.uploadsInParts) rights.add(Rights.uploadFiles);
    return rights;
}

/** Rights about the files listed (`files.*`, tags, the index...): those a connection may lack. */
const FILE_RIGHTS = /^(files|tags|index|tracking|trickplay)\./;

/**
 * The configuration as the pages see it while the listing shows a connection (agents/docs/features/connections.md):
 * what its files can't do is hidden where the app already hides what a session may not do. No default tags, index, data
 * server, external player, trickplay, folder pictures or animated hover (they read the library); thumbnails when its
 * provider or the server makes them; videos converted as in the library (from the server's cache of connections).
 */
export function atPlace(config: ClientConfig, place: ConnectionPlace | null): ClientConfig {
    if (!place) return config;
    const kept = placeRights(place);
    return {
        ...config,
        rootName: place.name || config.rootName,
        rights: config.rights.filter((right) => !FILE_RIGHTS.test(right) || kept.has(right)),
        // The server makes thumbnails of pictures (and of videos played) when its provider doesn't.
        features: { thumbnails: place.thumbnails || config.features.thumbnails, trickplay: false },
        dataServer: null,
        externalPlayer: null,
        folderPictures: 0,
        animatedHover: false,
    };
}

/** The routes (below `API_BASE`) of a connection's files (`connections/{id}/`), or of the library's (empty). */
export function placeRoute(connection?: number | null): string {
    return connection ? `connections/${connection}/` : '';
}

/**
 * The routes (below `API_BASE`) of uploads into a connection's files (`connections/{id}/files/uploads…`), or into
 * the library's (`files/uploads…`): the same paths below, bodies and answers.
 */
export function uploadsRoute(connection?: number | null): string {
    return connection ? `connections/${connection}/files/uploads` : 'files/uploads';
}

/**
 * The routes (below `API_BASE`) of the tags of a connection's files (`connections/{id}/file-tags…`), or of the
 * library's (`file-tags…`): the same paths below, bodies and answers.
 */
export function tagsRoute(connection?: number | null): string {
    return connection ? `connections/${connection}/file-tags` : 'file-tags';
}
