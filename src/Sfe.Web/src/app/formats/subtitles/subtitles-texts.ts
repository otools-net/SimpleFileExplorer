import { icu } from '../../core/i18n/icu';

/** How a subtitle file is shown: its cues, its source, or both side by side. */
export type SubtitlesMode = 'cues' | 'source' | 'split';

/** The subtitles view's choices of what to show: a short name (the buttons, the "⋯" menu) and a hint. */
export function subtitlesModes(): readonly { id: SubtitlesMode; label: string; title: string }[] {
    return [
        {
            id: 'cues',
            label: $localize`:shows the cues of a subtitle file:Cues`,
            title: $localize`Show what is said, and when.`,
        },
        {
            id: 'source',
            label: $localize`:shows a document's text as written:Source`,
            title: $localize`Show the file's text as written.`,
        },
        {
            id: 'split',
            label: $localize`:shows a document and its text side by side:Split`,
            title: $localize`Show the cues and the file's text side by side.`,
        },
    ];
}

/**
 * The line under the toolbar: the cues counted (those the filter shows, when it holds something), the length of
 * the subtitles and the cues starting before the one before them has ended ("3 cues · 8 s · 1 overlapping").
 */
export function cueCounts(total: number, shown: number | null, length: string | null, overlaps: number): string {
    const parts = [
        shown === null
            ? icu($localize`{total, plural, one {# cue} other {# cues}}`, { total })
            : icu($localize`{total, plural, one {{shown} of # cue} other {{shown} of # cues}}`, {
                  total,
                  shown: shown.toLocaleString(),
              }),
    ];
    if (length) parts.push(length);
    const overlapping = $localize`:cues that overlap the one before:{overlaps, plural, other {# overlapping}}`;
    if (overlaps) parts.push(icu(overlapping, { overlaps }));
    return parts.join(' · ');
}

/** The cue selected, in the line under the toolbar: its number, when it starts and how long it lasts. */
export function selectedCueLine(cue: string, start: string, length: string): string {
    return $localize`Cue ${cue}:cue: · ${start}:start: · ${length}:length:`;
}

/** The first problem of a file that isn't valid, and how many others there are. */
export function problemLine(line: number, message: string, others: number): string {
    return icu(
        $localize`{others, plural, =0 {Line {line}: {message}.} one {Line {line}: {message} (and 1 more problem).} \
other {Line {line}: {message} (and # more problems).}}`,
        { others, line: String(line), message },
    );
}

/** The cue list's name, for screen readers. */
export function cueListLabel(name: string): string {
    return $localize`Cues of “${name}:file:”`;
}

/** The hint of a cue that starts before the one before it has ended. */
export function overlapHint(): string {
    return $localize`This cue starts before the one before it has ended.`;
}

/** What the list says when it has no cue to show: none matches the filter, or the file has none. */
export function noCues(query: string): string {
    return query ? $localize`No cue holds “${query}:filter:”.` : $localize`This file has no cues.`;
}

/** The hint of Play, opening the video the subtitles belong to at the cue selected. */
export function playHint(video: string): string {
    return $localize`Play “${video}:video:” from this cue, in a new tab.`;
}

/** The toast refusing Shift times when the box holds no number of seconds (or 0). */
export function shiftRefused(): string {
    return $localize`Could not shift the times: type the seconds to move the cues by, 2.5 for later or -2.5 \
for earlier.`;
}

/** The toast after Shift times moved every cue by `milliseconds` (later when positive). */
export function shifted(milliseconds: number): string {
    return icu(
        $localize`{direction, select, later {Every cue moved {seconds, number} s later. Ctrl+Z undoes it.} \
other {Every cue moved {seconds, number} s earlier. Ctrl+Z undoes it.}}`,
        { direction: milliseconds > 0 ? 'later' : 'earlier', seconds: Math.abs(milliseconds / 1000) },
    );
}

/** The texts the subtitles view's template asks for. */
export const subtitlesTexts = { selectedCueLine, problemLine, cueListLabel, overlapHint, noCues, playHint };
