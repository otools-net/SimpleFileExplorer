import { ClientConfig } from './config.models';
import { ConnectionPlace } from './connection.models';
import { Rights } from './rights';

/** What a connection's files offer in phase 3 of the plan: browsing, viewing, downloading. */
const PLACE_RIGHTS: ReadonlySet<string> = new Set([Rights.browse, Rights.viewFiles, Rights.download]);

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
    return {
        ...config,
        rootName: place.name || config.rootName,
        rights: config.rights.filter((right) => !FILE_RIGHTS.test(right) || PLACE_RIGHTS.has(right)),
        features: { thumbnails: place.thumbnails, trickplay: false },
        dataServer: null,
        externalPlayer: null,
        streaming: null,
        folderPictures: 0,
        animatedHover: false,
    };
}
