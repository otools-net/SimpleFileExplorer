import { formatDate, formatSize } from '../core/format';
import { icu } from '../core/i18n/icu';
import { DuplicateResults, MissingFileInfo, SimilarVideoResults } from './tracking.models';

/** What a panel's summary box shows: its state (the dot's colour), a sentence, and details below. */
export interface StatusText {
    state: string | null;
    text: string;
    detail: string;
}

const WAITING: StatusText = { state: null, text: '…', detail: '' };

/** Joins whole sentences into one paragraph. */
function sentences(parts: (string | null | false)[]): string {
    return parts.filter(Boolean).join(' ');
}

/** The summary of the duplicates panel, from its first page (null while it loads). */
export function duplicatesStatus(result: DuplicateResults | null, failure: string | null): StatusText {
    if (failure)
        return { state: 'unavailable', text: $localize`Could not look for duplicates: ${failure}:reason:`, detail: '' };
    const summary = result?.summary;
    if (!result || !summary) return WAITING;
    const where = result.path ? $localize`in “${result.path}:folder:”` : $localize`in the library`;
    return {
        state: summary.groups ? 'found' : 'idle',
        text: summary.groups
            ? icu(
                  $localize`{groups, plural, one {# group of identical files {where}: {size} could be freed.} \
other {# groups of identical files {where}: {size} could be freed.}}`,
                  { groups: summary.groups, where, size: formatSize(summary.wastedBytes) },
              )
            : $localize`No duplicates ${where}:where:.`,
        detail: sentences([
            summary.groups > 0 &&
                icu($localize`{count, plural, one {# file in all.} other {# files in all.}}`, { count: summary.files }),
            result.minimumSize > 0 && $localize`Files under ${formatSize(result.minimumSize)}:size: are left out.`,
            summary.unfingerprinted > 0 && unfingerprinted(summary.unfingerprinted, result.fingerprinting),
            !result.fingerprinting &&
                $localize`Scans don't fingerprint files (Scanning › Indexing › Fingerprint files): \
new files are not compared.`,
        ]),
    };
}

function unfingerprinted(count: number, fingerprinting: boolean): string {
    return fingerprinting
        ? icu(
              $localize`{count, plural, one {# indexed file without a fingerprint can't be compared until it is \
scanned again.} other {# indexed files without a fingerprint can't be compared until they are scanned again.}}`,
              { count },
          )
        : icu(
              $localize`{count, plural, one {# indexed file without a fingerprint can't be compared.} \
other {# indexed files without a fingerprint can't be compared.}}`,
              { count },
          );
}

/** The summary of the similar videos panel, from the last answer (null while it loads). */
export function similarStatus(result: SimilarVideoResults | null, failure: string | null): StatusText {
    if (failure)
        return {
            state: 'unavailable',
            text: $localize`Could not read the similar videos: ${failure}:reason:`,
            detail: '',
        };
    if (!result) return WAITING;
    const { status: run, summary } = result;
    const running = run.state === 'running';
    return {
        state: running ? 'running' : run.state === 'failed' ? 'unavailable' : summary?.groups ? 'found' : 'idle',
        text: similarText(result),
        detail: sentences([
            summary && comparedCount(summary.compared, summary.videos),
            summary && $localize`Listed from ${summary.minimumPercent}:percent:% match.`,
            run.state === 'cancelled' &&
                $localize`The last comparison was cancelled: these are the groups of the one before.`,
            !!summary &&
                run.state === 'completed' &&
                !!run.finished &&
                $localize`Compared ${formatDate(run.finished)}:date:.`,
            !result.trickplayEnabled &&
                $localize`Trickplay is off (Trickplay › Settings): videos get no new fingerprints.`,
        ]),
    };
}

function similarText(result: SimilarVideoResults): string {
    const { status: run, summary } = result;
    if (run.state === 'running')
        return run.total
            ? icu(
                  $localize`Comparing videos… {checked, number} of \
{total, plural, one {# video} other {# videos}} read.`,
                  {
                      checked: run.checked,
                      total: run.total,
                  },
              )
            : $localize`Comparing videos…`;
    if (run.state === 'failed') return $localize`The comparison failed: ${run.message ?? ''}:reason:`;
    if (!summary) return $localize`The videos have not been compared since SFE started.`;
    const where = result.path ? $localize`with a video in “${result.path}:folder:”` : $localize`in the library`;
    return summary.groups
        ? icu(
              $localize`{groups, plural, one {# group of similar videos {where}: {size} more than the biggest \
of each.} other {# groups of similar videos {where}: {size} more than the biggest of each.}}`,
              { groups: summary.groups, where, size: formatSize(summary.wastedBytes) },
          )
        : $localize`No similar videos ${where}:where:.`;
}

/** How many indexed videos the last comparison took in, and why not all. */
function comparedCount(compared: number, videos: number): string {
    const count = icu(
        $localize`{compared, number} of {videos, plural, one {# indexed video} other {# indexed videos}} compared.`,
        { compared, videos },
    );
    return compared < videos ? sentences([count, $localize`The others have no trickplay yet.`]) : count;
}

/** The summary of the missing files panel. */
export function missingStatus(total: number, enabled: boolean, failure: string | null): StatusText {
    if (failure)
        return {
            state: 'unavailable',
            text: $localize`Could not read the missing files: ${failure}:reason:`,
            detail: '',
        };
    return {
        state: !enabled ? 'unavailable' : total ? 'found' : 'idle',
        text: total
            ? icu($localize`{count, plural, one {# tagged file is missing.} other {# tagged files are missing.}}`, {
                  count: total,
              })
            : $localize`No tagged file is missing.`,
        detail: enabled
            ? $localize`Relinking runs after every scan; “Relink now” runs it again, for instance \
after scanning by hand.`
            : $localize`Keeping the tags of moved files is turned off (see the Settings tab): \
files no longer found lose their tags.`,
    };
}

/** Where a missing file was, its size and since when it is missing. */
export function missingWas(item: MissingFileInfo): string {
    const slash = item.path.lastIndexOf('/');
    const size = formatSize(item.size);
    const since = formatDate(item.missingSince);
    return slash > 0
        ? $localize`Was in “${item.path.slice(0, slash)}:folder:” · ${size}:size: · missing since ${since}:date:`
        : $localize`Was at the top of the library · ${size}:size: · missing since ${since}:date:`;
}

/** The line under a list cut short: "And 3 more files." */
export function moreFiles(count: number): string {
    return icu($localize`{count, plural, one {And # more file.} other {And # more files.}}`, { count });
}

export function moreVideos(count: number): string {
    return icu($localize`{count, plural, one {And # more video.} other {And # more videos.}}`, { count });
}

/**
 * The toast after a copy was removed: moved to the trash (or deleted for good when there is no trash), and which copy
 * stays, with its tags when the copy removed gave it some.
 */
export function removedText(name: string, trashed: boolean, keptPath: string, withTags: boolean): string {
    const gone = trashed ? $localize`“${name}:name:” is in the trash.` : $localize`“${name}:name:” is deleted.`;
    const kept = withTags
        ? $localize`“${keptPath}:path:” is kept, with its tags.`
        : $localize`“${keptPath}:path:” is kept.`;
    return sentences([gone, kept]);
}

/** Why a removed copy couldn't be put back: the server's reason when it gave one. */
export function notPutBack(name: string, reason: string | null): string {
    return reason
        ? $localize`Could not put “${name}:name:” back: ${reason}:reason:`
        : $localize`Could not put “${name}:name:” back.`;
}
