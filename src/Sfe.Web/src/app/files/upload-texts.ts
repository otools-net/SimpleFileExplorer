import { formatDate, formatSize, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { baseName } from '../core/paths';
import { quoted } from './file-dialog-texts';
import { UploadInfo } from './file.models';
import { countOf, sizeOf } from './operations-texts';
import type { UploadItem } from './uploads';

/**
 * The sentences of uploads (the uploads in the operations panel, their toasts, Administration › File management ›
 * Uploads), as whole messages marked for translation (agents/docs/development/translations.md).
 */

/** "42%". */
function percent(value: number): string {
    return $localize`:a percentage:${value}:percent:%`;
}

/** How far an upload is, in whole percents (100 for an empty file). */
function percentDone(sent: number, size: number): number {
    return size ? Math.floor((sent * 100) / size) : 100;
}

/** The title of an upload batch in the panel; `where`: its folder, quoted. */
export function uploadTitle(active: boolean, files: number, done: number, failed: number, where: string): string {
    if (active)
        return icu($localize`Uploading {files, plural, one {# file} other {# files}} to {where}`, { files, where });
    return icu(
        $localize`{failed, plural, \
=0 {{done, plural, one {# file} other {# files}} uploaded to {where}} \
other {{done, plural, one {# file} other {# files}} uploaded to {where}, {failed, number} failed}}`,
        { done, failed, where },
    );
}

/** The line under a batch's progress bar: files and bytes sent, then "paused" or the speed. */
export function uploadStatus(
    files: { done: number; total: number },
    bytes: { sent: number; total: number },
    paused: boolean,
    speed: number | null,
): string {
    return joinParts([
        countOf(files.done, files.total),
        sizeOf(bytes.sent, bytes.total),
        paused
            ? $localize`:an upload batch:paused`
            : speed !== null
              ? $localize`:a speed:${formatSize(speed)}:size:/s`
              : null,
    ]);
}

/** Where a file of the panel is: "42%", "waiting", "paused at 42%", its error… */
export function uploadState(item: UploadItem): string {
    const done = percent(item.size ? Math.floor((item.sent * 100) / item.size) : 0);
    switch (item.state) {
        case 'uploading':
            return item.size ? done : '…';
        case 'queued':
            return $localize`:an upload:waiting`;
        case 'asking':
            return $localize`:an upload:name taken`;
        case 'paused':
            return $localize`:an upload:paused at ${done}:percent:`;
        case 'interrupted':
            return $localize`:an upload:${done}:percent:, interrupted`;
        case 'failed':
            return item.message ?? $localize`:an upload:failed`;
        case 'done':
            return $localize`:an upload:done`;
        case 'skipped':
            return $localize`:an upload:skipped`;
        case 'cancelled':
            return $localize`:an upload:cancelled`;
    }
}

/** The aria-label of a file's Cancel button. */
export function cancelFileLabel(name: string): string {
    return $localize`Cancel ${quoted(name)}:name:`;
}

/** The toast of files left out for their size; `max`: the largest size allowed, in MB. */
export function tooLargeToast(names: readonly string[], max: number): string {
    return icu(
        $localize`{count, plural, \
=1 {“{name}” is larger than the {max, number} MB a file may have here, so it was left out.} \
other {# files are larger than the {max, number} MB a file may have here, so they were left out.}}`,
        { count: names.length, name: baseName(names[0] ?? ''), max },
    );
}

/** The toast of a batch done: "3 files uploaded to “Media”." */
export function uploadedToast(count: number, where: string): string {
    return icu($localize`{count, plural, one {# file uploaded to {where}.} other {# files uploaded to {where}.}}`, {
        count,
        where,
    });
}

/** The reason of an upload the server could not save, when it gives none. */
export function notSaved(): string {
    return $localize`It could not be saved.`;
}

/** An unfinished upload in the administration's list: how far, who sends it, when it last received something. */
export function describeUpload(item: UploadInfo): string {
    const done = percent(percentDone(item.received, item.size));
    return joinParts([
        $localize`${sizeOf(item.received, item.size)}:size: (${done}:percent:)`,
        $localize`Sent by ${item.ownerName}:account:, last received ${formatDate(item.lastActivity)}:date:.`,
    ]);
}

/** The hint of the link to an upload's folder. */
export function uploadFolderHint(target: string): string {
    return target ? $localize`Open “${target}:folder:”.` : $localize`Open the top of the library.`;
}
