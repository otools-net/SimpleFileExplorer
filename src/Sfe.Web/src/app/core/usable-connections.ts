import { inject, Injectable, signal } from '@angular/core';
import { Api } from './api.service';
import { ConfigService } from './config.service';
import { UsableConnection } from './connection.models';
import { Rights } from './rights';

/**
 * The connections the session may use in its library (`GET api/connections`, `usable`), for the places of moves and
 * copies: the folder picker lists them, the operations panel names them. Read once, again when asked `fresh`.
 */
@Injectable({ providedIn: 'root' })
export class UsableConnections {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private asked: Promise<UsableConnection[]> | null = null;

    /** As last read; empty until then. */
    readonly list = signal<UsableConnection[]>([]);

    /** The server has connections and the session may use them. */
    enabled(): boolean {
        return Boolean(this.config.config()?.connections) && this.config.can(Rights.useConnections);
    }

    /** The connections (read again with `fresh`); none without the right, or when they can't be read. */
    load(fresh = false): Promise<UsableConnection[]> {
        if (!this.enabled()) return Promise.resolve([]);
        if (!this.asked || fresh)
            this.asked = this.api.connections().then(
                (view) => {
                    this.list.set(view.usable);
                    return view.usable;
                },
                () => [],
            );
        return this.asked;
    }

    /** The name of the connection `id`: the one listed, else as read (asked for the first time if need be). */
    name(id: number): string {
        const listed = this.config.place();
        if (listed?.id === id && listed.name) return listed.name;
        const known = this.list().find((connection) => connection.id === id);
        if (!known) void this.load();
        return known?.name ?? 'a connection';
    }
}
