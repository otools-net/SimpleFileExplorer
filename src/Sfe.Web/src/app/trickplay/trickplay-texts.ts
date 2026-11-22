import { formatDate } from '../core/format';
import { icu, textLocale } from '../core/i18n/icu';
import { TrickplayStatus } from './trickplay.models';

/** What the trickplay task has done in its pass: "120 videos checked · 80 created · 2 failed". */
export function passCounts(status: TrickplayStatus): (string | null)[] {
    return [
        icu($localize`{count, plural, one {# video checked} other {# videos checked}}`, { count: status.checked }),
        icu($localize`:trickplay made for that many videos:{count, plural, one {# created} other {# created}}`, {
            count: status.generated,
        }),
        status.failed
            ? icu($localize`:trickplay failed for that many videos:{count, plural, one {# failed} other {# failed}}`, {
                  count: status.failed,
              })
            : null,
    ];
}

/** When the task last went over every trickplay folder, or null. */
export function lastCheck(status: TrickplayStatus): string | null {
    return status.lastPassFinished ? $localize`last complete check ${formatDate(status.lastPassFinished)}:date:` : null;
}

/** The videos being processed: "Working on “Trips/a.mp4” and “b.mp4”." */
export function workingOn(paths: string[]): string {
    const quoted = paths.map((path) => `“${path}”`);
    const videos = new Intl.ListFormat(textLocale(), { type: 'conjunction' }).format(quoted);
    return $localize`Working on ${videos}:videos:.`;
}

/** What waits: the videos opened in the player, then those the pass found (more to come while it looks). */
export function waiting(status: TrickplayStatus): (string | null)[] {
    return [
        status.queued
            ? icu($localize`{count, plural, one {# opened video waiting} other {# opened videos waiting}}`, {
                  count: status.queued,
              })
            : null,
        status.pending
            ? icu($localize`{more, select, yes {{count, number}+ to do} other {{count, number} to do}}`, {
                  count: status.pending,
                  more: status.discovering ? 'yes' : 'no',
              })
            : null,
    ];
}
