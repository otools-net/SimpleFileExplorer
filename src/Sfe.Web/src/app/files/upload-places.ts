import { ConfigService } from '../core/config.service';
import { LibraryRoute } from '../library/library-route';

/**
 * Where uploads go (agents/docs/features/uploads.md): the place listed when they start, kept by their batch while the
 * person goes elsewhere. A connection's id (`?c=`), its files reached through `api/connections/{id}/files/uploads…`;
 * null for the library's. Unfinished uploads are listed again after a reload, the library's and each connection's.
 */
export function uploadConnection(config: ConfigService): number | null {
    return config.place()?.id ?? null;
}

/** The listing shows the folder `target` of the place `connection` (not a search): files uploaded there show in it. */
export function showsTarget(route: LibraryRoute, target: string, connection: number | null): boolean {
    return !route.q && route.path === target && (route.c ?? null) === connection;
}
