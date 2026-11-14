import { ClientConfig } from './config.models';
import { ConnectionPlace } from './connection.models';
import { Rights } from './rights';

/** What a connection's files always offer: browsing, viewing, downloading. */
const PLACE_RIGHTS: ReadonlySet<string> = new Set([Rights.browse, Rights.viewFiles, Rights.download]);

/**
 * What they offer too where the session may write there: new folders, renames, deletes; moves and copies when the
 * provider does them itself. No uploads, new files, extract, compress, permanent delete, edits or tags yet.
 */
function placeRights(place: ConnectionPlace): ReadonlySet<string> {
    if (place.access !== 'write') return PLACE_RIGHTS;
    const rights = new Set([...PLACE_RIGHTS, Rights.createFolders, Rights.renameFiles, Rights.deleteFiles]);
    if (place.serverSideMove) rights.add(Rights.moveFiles);
    if (place.serverSideCopy) rights.add(Rights.copyFiles);
    return rights;
}

/** Rights about the files listed (`files.*`, tags, the index...): those a connection may lack. */
const FILE_RIGHTS = /^(files|tags|index|tracking|trickplay)\./;

/**
 * The configuration as the pages see it while the listing shows a connection (agents/docs/features/connections.md):
 * what its files can't do is hidden where the app already hides what a session may not do. No tags, index, data
 * server, conversion, external player, trickplay, folder pictures or animated hover (they read the library); thumbnails
 * when its provider makes them.
 */
export function atPlace(config: ClientConfig, place: ConnectionPlace | null): ClientConfig {
    if (!place) return config;
    const kept = placeRights(place);
    return {
        ...config,
        rootName: place.name || config.rootName,
        rights: config.rights.filter((right) => !FILE_RIGHTS.test(right) || kept.has(right)),
        features: { thumbnails: place.thumbnails, trickplay: false },
        dataServer: null,
        externalPlayer: null,
        streaming: null,
        folderPictures: 0,
        animatedHover: false,
    };
}
