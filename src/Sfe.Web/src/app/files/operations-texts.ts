import { formatDate, formatSize, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { ClashSide } from '../core/models';
import { baseName } from '../core/paths';
import { FileClipboard } from './file-clipboard';
import { quoted } from './file-dialog-texts';
import { FileJobKind, FileJobView } from './file.models';

/**
 * The sentences of the operations panel and of the toasts ending an operation (moves, copies, compressions,
 * extractions, transfers between places), as whole messages marked for translation: counts are ICU plurals, the kind
 * of operation an ICU `select`, the place a phrase of its own (agents/docs/development/translations.md).
 */

/** Where an operation goes, quoted: the folder's name, or the place's for its top folder. */
export function whereOf(target: string, place: string): string {
    return quoted(baseName(target) || place);
}

/** "to “Holidays”", or "from Dropbox › Trips to Library › Photos" for a transfer between two places. */
export function destination(where: string, journey: { from: string; to: string } | null): string {
    return journey
        ? $localize`:where an operation goes, between two places:from ${journey.from}:from: to ${journey.to}:to:`
        : $localize`:where an operation goes:to ${where}:where:`;
}

/** The title of a move or copy in the panel; `to`: its destination phrase. */
export function transferTitle(job: FileJobView, to: string): string {
    const values = { kind: job.kind, to, done: job.done, total: job.total };
    if (job.state === 'cancelled')
        return icu(
            $localize`{kind, select, \
move {Stopped: {done} of {total, plural, one {# item} other {# items}} moved {to}} \
other {Stopped: {done} of {total, plural, one {# item} other {# items}} copied {to}}}`,
            values,
        );
    if (job.state === 'completed') {
        const done = job.results.filter((result) => result.status === 'done').length;
        return icu(
            $localize`{kind, select, \
move {{done, plural, one {# item} other {# items}} moved {to}} \
other {{done, plural, one {# item} other {# items}} copied {to}}}`,
            { ...values, done },
        );
    }
    return icu(
        $localize`{kind, select, \
move {Moving {total, plural, one {# item} other {# items}} {to}} \
other {Copying {total, plural, one {# item} other {# items}} {to}}}`,
        values,
    );
}

/** The title of a compression in the panel. */
export function compressTitle(job: FileJobView, where: string): string {
    const zip = job.results[0]?.newPath;
    if (job.state === 'cancelled') return $localize`Stopped: no zip made in ${where}:where:`;
    if (job.state === 'completed')
        return zip ? $localize`${quoted(baseName(zip))}:zip: made in ${where}:where:` : $localize`No zip made`;
    return $localize`Compressing into ${where}:where:`;
}

/** The title of an extraction in the panel. */
export function extractTitle(job: FileJobView, where: string): string {
    const archive = quoted(baseName(job.results[0]?.path ?? job.current ?? ''));
    if (job.state === 'cancelled') return $localize`Stopped: nothing extracted to ${where}:where:`;
    if (job.state === 'completed')
        return job.results[0]?.status === 'done'
            ? $localize`${archive}:archive: extracted to ${where}:where:`
            : $localize`${archive}:archive: not extracted`;
    return job.current
        ? $localize`Extracting ${quoted(baseName(job.current))}:archive: to ${where}:where:`
        : $localize`Extracting to ${where}:where:`;
}

/** "12 MB of 40 MB". */
export function sizeOf(done: number, total: number): string {
    return $localize`:bytes done of a total:${formatSize(done)}:done: of ${formatSize(total)}:total:`;
}

/** "3 of 10". */
export function countOf(done: number, total: number): string {
    return $localize`:items done of a total:${done}:done: of ${total}:total:`;
}

/** The line under an operation's progress bar; `left`: the time left, when known. */
export function jobStatus(job: FileJobView, left: string | null): string {
    if (job.state === 'waiting') return $localize`Waiting for your answer…`;
    if (job.kind === 'extract')
        return job.bytesTotal
            ? joinParts([$localize`${sizeOf(job.bytesDone, job.bytesTotal)}:size: read`, left])
            : $localize`Reading the archive…`;
    if (job.kind === 'compress')
        return job.bytesDone
            ? joinParts([$localize`${sizeOf(job.bytesDone, job.bytesTotal)}:size: compressed`, left])
            : $localize`Waiting for its turn…`;
    return joinParts([
        countOf(job.done, job.total),
        job.kind === 'copy' && job.bytesTotal ? sizeOf(job.bytesDone, job.bytesTotal) : null,
        job.current ? baseName(job.current) : null,
    ]);
}

/**
 * "about 4 min left", from the speed since `start` (when and how many bytes were done then); null for the first 10
 * seconds, or while nothing moves.
 */
export function timeLeft(
    start: { at: number; bytes: number },
    now: number,
    done: number,
    total: number,
): string | null {
    const seconds = (now - start.at) / 1000;
    const speed = (done - start.bytes) / seconds;
    if (seconds < 10 || speed <= 0 || done >= total) return null;
    const left = (total - done) / speed;
    if (left < 60) return $localize`less than a minute left`;
    const minutes = Math.round(left / 60);
    if (minutes < 60) return $localize`about ${minutes}:minutes: min left`;
    const hours = Math.floor(minutes / 60);
    const rest = String(minutes % 60).padStart(2, '0');
    return $localize`about ${hours}:hours: h ${rest}:minutes: min left`;
}

/** One side of a name clash: "folder, modified 1 Jan 2026", "3 MB, modified …". */
export function clashSide(side: ClashSide): string {
    const what = side.isFolder ? $localize`:one side of a name clash:folder` : formatSize(side.size);
    return side.modified ? $localize`${what}:what:, modified ${formatDate(side.modified)}:date:` : what;
}

/** What the panel says of the items cut or copied (Ctrl+X, Ctrl+C), until pasted. */
export function clipboardText(clipboard: FileClipboard): string {
    return icu(
        $localize`{kind, select, \
move {{count, plural, =1 {“{name}” cut. Open a folder to paste it into.} \
other {# items cut. Open a folder to paste them into.}}} \
other {{count, plural, =1 {“{name}” copied. Open a folder to paste it into.} \
other {# items copied. Open a folder to paste them into.}}}}`,
        { kind: clipboard.kind, count: clipboard.items.length, name: clipboard.items[0]?.name ?? '' },
    );
}

/** The toast after Ctrl+X or Ctrl+C. */
export function keptToast(kind: FileJobKind, count: number): string {
    return icu(
        $localize`{kind, select, \
move {{count, plural, one {# item cut. Paste it into a folder with Ctrl+V or the folder's menu.} \
other {# items cut. Paste them into a folder with Ctrl+V or the folder's menu.}}} \
other {{count, plural, one {# item copied. Paste it into a folder with Ctrl+V or the folder's menu.} \
other {# items copied. Paste them into a folder with Ctrl+V or the folder's menu.}}}}`,
        { kind, count },
    );
}

/** The toast of a move or copy done: "2 items moved to “Holidays”." */
export function transferredToast(kind: FileJobKind, count: number, where: string): string {
    return icu(
        $localize`{kind, select, \
move {{count, plural, one {# item moved to {where}.} other {# items moved to {where}.}}} \
other {{count, plural, one {# item copied to {where}.} other {# items copied to {where}.}}}}`,
        { kind, count, where },
    );
}

/** The toast of a move or copy stopped: what was done before. */
export function stoppedToast(kind: FileJobKind, count: number, where: string): string {
    return icu(
        $localize`{kind, select, \
move {{count, plural, =0 {Stopped. Nothing was moved to {where}.} \
one {Stopped. # item was moved to {where} before.} other {Stopped. # items were moved to {where} before.}}} \
other {{count, plural, =0 {Stopped. Nothing was copied to {where}.} \
one {Stopped. # item was copied to {where} before.} other {Stopped. # items were copied to {where} before.}}}}`,
        { kind, count, where },
    );
}

/** The error toast of items a move or copy could not do: the first one's reason (else its name) after the count. */
export function notDoneToast(kind: FileJobKind, failed: readonly { path: string; message?: string | null }[]): string {
    const first =
        failed[0].message ??
        (kind === 'move'
            ? $localize`Could not move ${quoted(baseName(failed[0].path))}:name:.`
            : $localize`Could not copy ${quoted(baseName(failed[0].path))}:name:.`);
    if (failed.length === 1) return first;
    const count = icu(
        $localize`{kind, select, \
move {{count, plural, one {Could not move # item.} other {Could not move # items.}}} \
other {{count, plural, one {Could not copy # item.} other {Could not copy # items.}}}}`,
        { kind, count: failed.length },
    );
    return `${count} ${first}`;
}

/** The error toast of an operation that could not start: "Could not move: …". */
export function couldNotStart(kind: FileJobKind | 'retry', reason: string): string {
    return icu(
        $localize`{kind, select, \
move {Could not move: {reason}} copy {Could not copy: {reason}} \
extract {Could not extract: {reason}} compress {Could not compress: {reason}} \
other {Could not try again: {reason}}}`,
        { kind, reason },
    );
}

/** Names already taken in a connection's folder, after a move or copy there (the toast offers Keep both, Replace). */
export function takenToast(kind: 'move' | 'copy', paths: readonly string[], where: string): string {
    return icu(
        $localize`{kind, select, \
move {{count, plural, =1 {“{name}” is already in {where}, so it was not moved.} \
other {# names are already in {where}, so those items were not moved.}}} \
other {{count, plural, =1 {“{name}” is already in {where}, so it was not copied.} \
other {# names are already in {where}, so those items were not copied.}}}}`,
        { kind, count: paths.length, name: baseName(paths[0] ?? ''), where },
    );
}

/** The hint of a toast's Open: "Open “Holidays”." */
export function openHint(where: string): string {
    return $localize`Open ${where}:where:.`;
}
