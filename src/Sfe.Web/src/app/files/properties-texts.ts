import { formatSize } from '../core/format';
import { icu, textLocale } from '../core/i18n/icu';
import { LibraryItem } from '../core/models';
import { parentPath } from '../core/paths';
import { FolderTotals, PropertyJob } from './file.models';

/**
 * The sentences of the properties sheet (sizes, what a folder holds, a count on the disk, a checksum being computed),
 * as whole messages marked for translation (agents/docs/development/translations.md).
 */

/** The server's word for a job stopped by the user (`PropertyJobs`): not shown, the count says it. */
const STOPPED = 'Stopped.';

/** "512 bytes", "2.4 MB (2,500,000 bytes)". */
export function sizeText(bytes: number | null): string {
    if (bytes === null) return '';
    if (bytes < 1024) return icu($localize`{bytes, plural, one {# byte} other {# bytes}}`, { bytes });
    return icu($localize`{size} ({bytes, plural, one {# byte} other {# bytes}})`, { size: formatSize(bytes), bytes });
}

/** "12 files, 2 folders, 3 MB". */
export function holds(files: number, folders: number, bytes: number): string {
    return icu(
        $localize`:what a folder holds:{files, plural, one {# file} other {# files}}, \
{folders, plural, one {# folder} other {# folders}}, {size}`,
        { files, folders, size: formatSize(bytes) },
    );
}

/** What the index says a folder holds (null while it loads). */
export function totalsText(totals: FolderTotals | null): string {
    if (!totals) return $localize`In the index: …`;
    if (!totals.indexed && !totals.files && !totals.folders)
        return $localize`Not in the index yet. Count it on the disk to see what it holds.`;
    const what = holds(totals.files, totals.folders, totals.bytes);
    return $localize`In the index: ${what}:holds:, as of the last scan.`;
}

/** Where a count on the disk stands: what it found so far, or in all. */
export function countText(job: PropertyJob | null): string {
    if (!job) return '';
    if (job.error && job.error !== STOPPED) return job.error;
    const what = holds(job.files, job.folders, job.bytes);
    if (!job.done) return $localize`On the disk: ${what}:holds: so far…`;
    const found = job.capped
        ? $localize`On the disk: more than ${what}:holds:.`
        : job.error
          ? $localize`On the disk: ${what}:holds: when the count was stopped.`
          : $localize`On the disk: ${what}:holds:.`;
    if (!job.leftOut) return found;
    const leftOut = icu(
        $localize`{count, plural, one {# link or unreadable folder was left out.} \
other {# links or unreadable folders were left out.}}`,
        { count: job.leftOut },
    );
    return `${found} ${leftOut}`;
}

/** Where a checksum being computed stands. */
export function checksumText(job: PropertyJob | null): string {
    if (!job) return '';
    if (job.error) return job.error;
    const percent = job.total ? Math.floor((job.bytes * 100) / job.total) : 0;
    const read = formatSize(job.bytes);
    const total = formatSize(job.total);
    return $localize`Reading the file… ${percent}:percent:% (${read}:read: of ${total}:total:)`;
}

/** The job's error when the server no longer knows it (restarted, or forgotten after a while). */
export function jobForgotten(): string {
    return $localize`The server lost track of it. Start it again.`;
}

/** "Folder", or the file's extension and kind ("JPG image"). */
export function itemKind(item: LibraryItem): string {
    if (item.kind === 'directory') return $localize`Folder`;
    const kind = ['image', 'video', 'audio', 'document'].includes(item.kind) ? item.kind : 'file';
    const extension = item.extension?.replace(/^\./, '').toUpperCase();
    if (!extension)
        return icu(
            $localize`{kind, select, image {Image} video {Video} audio {Audio} document {Document} other {File}}`,
            { kind },
        );
    return icu(
        $localize`:a file's kind after its extension:{kind, select, image {{extension} image} video {{extension} video} \
audio {{extension} audio} document {{extension} document} other {{extension} file}}`,
        { kind, extension },
    );
}

/**
 * The folder an item is in, from `root` (the library's or the connection's name): "Media / 2024 / Beach". The item at
 * the top itself (an empty path) says so.
 */
export function folderOf(path: string, root: string, onPlace: boolean): string {
    if (!path)
        return onPlace ? $localize`This is the top of the connection.` : $localize`This is the top of the library.`;
    const parent = parentPath(path);
    return parent ? `${root} / ${parent.split('/').join(' / ')}` : root;
}

/** "hidden, read-only and a link to another place": the attributes the disk gives. */
export function attributesText(details: { hidden: boolean; readOnly: boolean; isLink: boolean }): string {
    const parts = [
        details.hidden && $localize`:a file attribute:hidden`,
        details.readOnly && $localize`:a file attribute:read-only`,
        details.isLink && $localize`:a file attribute:a link to another place`,
    ].filter((part): part is string => Boolean(part));
    return new Intl.ListFormat(textLocale(), { type: 'conjunction' }).format(parts);
}
