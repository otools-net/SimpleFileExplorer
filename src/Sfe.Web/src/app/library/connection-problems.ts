import { ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionLevelName } from '../core/config.models';
import { Rights } from '../core/rights';

/** Why a connection's folder or search could not be shown (agents/docs/features/connection-files.md). */
export interface ConnectionProblem {
    heading: string;
    text: string;
    /** The administration page where the session signs in to it again; null when someone else must. */
    page: { section: string; page: string } | null;
}

/**
 * What to say when a connection's route refused because the connection can't be used (409 with a `type` word);
 * null for any other error. `level`: the connection's, when known (it decides who signs in again, and where).
 */
export function connectionProblemOf(
    error: ApiError | null,
    level: ConnectionLevelName | null,
    config: ConfigService,
): ConnectionProblem | null {
    switch (error?.type) {
        case 'connection-needs-reconnecting': {
            const page = reconnectPage(level, config);
            return {
                heading: $localize`This connection needs signing in again`,
                text: page
                    ? $localize`The storage no longer accepts its sign-in. Sign in again on the connections page.`
                    : $localize`The storage no longer accepts its sign-in. \
Ask whoever manages the connection to sign in again.`,
                page,
            };
        }
        case 'connection-unusable':
            return { heading: $localize`This connection isn't available`, text: error.message, page: null };
        case 'connection-not-supported':
            return { heading: $localize`This connection can't do that`, text: error.message, page: null };
        default:
            return null;
    }
}

/**
 * Where the session signs in again: its own connections' page for an account-level one (only its owner uses it), the
 * shared connections' page for who manages library- and server-level ones; null for anyone else.
 */
function reconnectPage(level: ConnectionLevelName | null, config: ConfigService): ConnectionProblem['page'] {
    if (level === 'account') return { section: 'account', page: 'connections' };
    const shared = level === 'server' ? Rights.serverConnections : Rights.libraryConnections;
    if (level !== null && config.can(shared)) return { section: 'connections', page: 'connections' };
    return null;
}
