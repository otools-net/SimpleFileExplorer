import { ConnectionIndexStatus } from '../core/connection.models';
import { plural } from '../core/format';
import { formatRelative } from '../tasks/tasks';

/**
 * Where the index of a connection stands in this space, in words, for its Manage panel: "Not indexed here yet…",
 * "Listing: 1,200 files…", "1,204 files and 37 folders · synced 5 minutes ago", and what went wrong last.
 */
export function describeIndex(status: ConnectionIndexStatus, now = Date.now()): string {
    if (!status.indexed) return 'Not indexed in this space yet: it is when someone first opens the connection here.';
    const parts: string[] = [];
    if (status.running) parts.push(status.activity ?? 'Syncing…');
    else if (status.listing) parts.push('A full listing is under way; it goes on at the next sync.');
    if (status.lastSync) {
        const counts = `${plural(status.files, 'file')} and ${plural(status.folders, 'folder')}`;
        parts.push(`${counts} · synced ${formatRelative(status.lastSync, now)}`);
    }
    if (status.tooLong > 0) parts.push(`${plural(status.tooLong, 'item')} left out (path too long)`);
    if (status.lastError) parts.push(`Last sync stopped: ${status.lastError}`);
    return parts.join(' · ');
}
