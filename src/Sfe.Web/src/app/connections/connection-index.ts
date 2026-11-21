import { ConnectionIndexStatus } from '../core/connection.models';
import { icu } from '../core/i18n/icu';
import { formatRelative } from '../tasks/tasks';

/**
 * Where the index of a connection stands in this space, in words, for its Manage panel: "Not indexed in this space
 * yet…", "Listing: 1,200 files…", "1,204 files and 37 folders, synced 5 minutes ago", and what went wrong last.
 */
export function describeIndex(status: ConnectionIndexStatus, now = Date.now()): string {
    if (!status.indexed)
        return $localize`Not indexed in this space yet. It will be when someone first opens the connection here.`;
    const parts: string[] = [];
    if (status.running) parts.push(status.activity ?? $localize`Syncing…`);
    else if (status.listing) parts.push($localize`A full listing is under way. It carries on at the next sync.`);
    if (status.lastSync)
        parts.push(
            icu(
                $localize`{files, plural, one {# file} other {# files}} and \
{folders, plural, one {# folder} other {# folders}}, synced {when}`,
                { files: status.files, folders: status.folders, when: formatRelative(status.lastSync, now) },
            ),
        );
    if (status.tooLong > 0)
        parts.push(
            icu(
                $localize`{count, plural, one {# item left out (its path is too long)} \
other {# items left out (their paths are too long)}}`,
                { count: status.tooLong },
            ),
        );
    if (status.lastError) parts.push($localize`The last sync stopped: ${status.lastError}:reason:`);
    return parts.join(' · ');
}
