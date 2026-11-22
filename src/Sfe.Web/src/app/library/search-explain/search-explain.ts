import { formatDuration, formatSize } from '../../core/format';
import { icu } from '../../core/i18n/icu';
import { Clause, Group, parseQuery } from './search-query';
import { list, not, OPS, phrase, Segment, slot, term, text } from './search-segments';

export { parseQuery, splitOutsideQuotes, type ParsedQuery } from './search-query';
export type { Segment, TermType } from './search-segments';

/*
 * The search query put into words (the parsing is in search-query.ts). Each sentence is one translated message; the
 * searched values and logical words go into its slots (`phrase`), so a language can put them where it needs.
 */

/** How a size compares, around the size typed (`size`). */
const SIZE_PHRASES: Record<string, () => string> = {
    '<': () => $localize`smaller than ${slot('size')}:size:`,
    '<=': () => $localize`of at most ${slot('size')}:size:`,
    '>': () => $localize`larger than ${slot('size')}:size:`,
    '>=': () => $localize`of at least ${slot('size')}:size:`,
    '=': () => $localize`of exactly ${slot('size')}:size:`,
    '': () => $localize`of about ${slot('size')}:size:`,
};
/** How a width or height compares, around the pixels typed (`pixels`). */
const PIXEL_PHRASES: Record<string, () => string> = {
    '<': () => $localize`less than ${slot('pixels')}:pixels:`,
    '<=': () => $localize`at most ${slot('pixels')}:pixels:`,
    '>': () => $localize`more than ${slot('pixels')}:pixels:`,
    '>=': () => $localize`at least ${slot('pixels')}:pixels:`,
    '=': () => $localize`exactly ${slot('pixels')}:pixels:`,
    '': () => $localize`exactly ${slot('pixels')}:pixels:`,
};
/** How a duration compares, around the duration typed (`duration`). */
const DURATION_PHRASES: Record<string, () => string> = {
    '<': () => $localize`shorter than ${slot('duration')}:duration:`,
    '<=': () => $localize`lasting at most ${slot('duration')}:duration:`,
    '>': () => $localize`longer than ${slot('duration')}:duration:`,
    '>=': () => $localize`lasting at least ${slot('duration')}:duration:`,
    '=': () => $localize`lasting exactly ${slot('duration')}:duration:`,
    '': () => $localize`lasting about ${slot('duration')}:duration:`,
};

/** The query explained: a sentence, followed by a list when there are several alternatives. */
export interface Explanation {
    sentence: Segment[];
    alternatives: Segment[][];
    notes: Segment[][];
}

/* ---------- Wording ---------- */

/** The query put into words, searching in `path` ('' = the whole library). */
export function explain(query: string, path: string): Explanation {
    const { fullPath, groups, notes } = parseQuery(query);
    notes.push(...clauseNotes(groups));
    if (!groups.length) return { sentence: [text($localize`Nothing to search for yet.`)], alternatives: [], notes };
    const folder = { ...term(path.split('/').pop() ?? '', 'folder'), title: path } as Segment;
    const phrases = groups.map((group) => groupPhrase(group, fullPath));
    if (phrases.length === 1) {
        const sentence = path
            ? phrase($localize`Finds files in ${slot('folder')}:folder: and its sub-folders ${slot('what')}:what:.`, {
                  folder,
                  what: phrases[0],
              })
            : phrase($localize`Finds files in the whole library ${slot('what')}:what:.`, { what: phrases[0] });
        return { sentence, alternatives: [], notes };
    }
    const sentence = path
        ? phrase($localize`Finds files in ${slot('folder')}:folder: and its sub-folders:`, { folder })
        : [text($localize`Finds files in the whole library:`)];
    const alternatives = phrases.map((words, i) => [i === 0 ? OPS.either() : OPS.or(), text(' '), ...words]);
    return { sentence, alternatives, notes };
}

/** What some kinds of terms need said about them (once per search). */
function clauseNotes(groups: Group[]): Segment[][] {
    const has = (test: (clause: Clause) => boolean) => groups.some((group) => group.clauses.some(test));
    const resolution = (clause: Clause) => clause.kind === 'dimension' || clause.kind === 'level';
    const notes: Segment[][] = [];
    if (has((clause) => clause.kind === 'tag'))
        notes.push([
            text($localize`A tag also matches by its shorthand or aliases, and includes the tags inheriting from it.`),
        ]);
    if (has((clause) => clause.kind === 'type'))
        notes.push([
            text($localize`The type of a file comes from its extension, \
by the lists of Administration › Scanning › File types.`),
        ]);
    if (has((clause) => clause.kind === 'level'))
        notes.push([
            text($localize`High and low resolution compare the shorter side with the thresholds of the HR/LR marks, \
one for photos and one for videos (Administration › Interface › Browsing).`),
        ]);
    if (has((clause) => resolution(clause) && !clause.negated))
        notes.push([text($localize`A resolution only finds photos and videos whose resolution a scan has read.`)]);
    if (has((clause) => resolution(clause) && clause.negated))
        notes.push(
            phrase(
                $localize`An excluded resolution also finds the files whose resolution isn't known \
(not photos or videos, or not read yet): add ${slot('term')}:term: to leave them out.`,
                { term: term('w:>0', 'size') },
            ),
        );
    if (has((clause) => clause.kind === 'duration' && !clause.negated))
        notes.push([text($localize`A duration only finds videos and audio files whose duration a scan has read.`)]);
    if (has((clause) => clause.kind === 'duration' && clause.negated))
        notes.push(
            phrase(
                $localize`An excluded duration also finds the files whose duration isn't known \
(not videos nor audio, or not read yet): add ${slot('term')}:term: to leave them out.`,
                { term: term('d:>0', 'size') },
            ),
        );
    return notes;
}

/** The clauses of one alternative, joined by AND. */
function groupPhrase(group: Group, fullPath: boolean): Segment[] {
    return group.clauses.flatMap((clause, i) => [
        ...(i ? [text(' '), OPS.and(), text(' ')] : []),
        ...clausePhrase(clause, fullPath),
    ]);
}

function clausePhrase(clause: Clause, fullPath: boolean): Segment[] {
    switch (clause.kind) {
        case 'text':
            return textPhrase(clause, fullPath);
        case 'extension': {
            // Excluded: neither of them, "without the extension .mp4 or .mkv".
            const values = list(
                clause.values.map((e) => term(e.value, 'extension', clause.negated, e.token)),
                OPS.or,
            );
            return clause.negated
                ? phrase($localize`${slot('without')}:without: the extension ${slot('values')}:values:`, {
                      without: OPS.without(),
                      values,
                  })
                : phrase($localize`with the extension ${slot('values')}:values:`, { values });
        }
        case 'type':
            // Excluded: none of them, "that are not photos or videos".
            return phrase($localize`that are ${slot('kinds')}:kinds:`, {
                kinds: [
                    ...not(clause.negated),
                    ...list(
                        clause.values.map((k) => term(k.value, 'extension', clause.negated, k.token)),
                        OPS.or,
                    ),
                ],
            });
        case 'size':
            return sizePhrase(clause);
        case 'dimension':
            return dimensionPhrase(clause);
        case 'level':
            return [
                ...not(clause.negated),
                ...phrase($localize`in ${slot('level')}:level:`, {
                    level: {
                        ...term(
                            clause.high ? $localize`high resolution` : $localize`low resolution`,
                            'size',
                            clause.negated,
                        ),
                        title: clause.token,
                    } as Segment,
                }),
            ];
        case 'duration':
            return durationPhrase(clause);
        case 'tag':
            return clause.negated
                ? phrase($localize`${slot('not')}:not: tagged ${slot('tag')}:tag:`, {
                      not: OPS.not(),
                      tag: term(clause.name, 'tag', true, clause.token),
                  })
                : phrase($localize`tagged ${slot('tag')}:tag:`, { tag: term(clause.name, 'tag', false, clause.token) });
        case 'untagged':
            // special:untagged is "no tag at all", -special:untagged "at least one".
            return clause.negated
                ? [text($localize`with at least one tag`)]
                : phrase($localize`${slot('without')}:without: any tag`, { without: OPS.without() });
    }
}

/** Words searched for and left out, in the name or the full path. */
function textPhrase(clause: Extract<Clause, { kind: 'text' }>, fullPath: boolean): Segment[] {
    const where: Segment = { kind: 'strong', text: fullPath ? $localize`full path` : $localize`name` };
    const included = list(
        clause.include.map((w) => term(w.value, 'text', false, w.token)),
        OPS.and,
    );
    const excluded = list(
        clause.exclude.map((w) => term(w.value, 'text', true, w.token)),
        OPS.or,
    );
    const without = OPS.without();
    if (!clause.exclude.length)
        return phrase($localize`with ${slot('included')}:included: in their ${slot('where')}:where:`, {
            included,
            where,
        });
    if (!clause.include.length)
        return phrase(
            $localize`${slot('without')}:without: ${slot('excluded')}:excluded: in their ${slot('where')}:where:`,
            {
                without,
                excluded,
                where,
            },
        );
    return phrase(
        $localize`with ${slot('included')}:included: but ${slot('without')}:without: ${slot('excluded')}:excluded: \
in their ${slot('where')}:where:`,
        { included, without, excluded, where },
    );
}

function sizePhrase(clause: Extract<Clause, { kind: 'size' }>): Segment[] {
    const title = clause.op
        ? clause.token
        : $localize`${clause.token}:typed:: from ${formatSize(clause.min)}:min: to ${formatSize(clause.max)}:max:`;
    return [
        ...not(clause.negated),
        ...phrase(SIZE_PHRASES[clause.op](), {
            size: { ...term(clause.label, 'size', clause.negated), title } as Segment,
        }),
    ];
}

function dimensionPhrase(clause: Extract<Clause, { kind: 'dimension' }>): Segment[] {
    const side = (count: number, wide: boolean): Segment[] => {
        const value = icu($localize`{count, plural, one {{typed} pixel} other {{typed} pixels}}`, {
            count,
            typed: String(count),
        });
        const pixels = { ...term(value, 'size', clause.negated), title: clause.token } as Segment;
        const amount = phrase(PIXEL_PHRASES[clause.op](), { pixels });
        return wide
            ? phrase($localize`:a width in a search:${slot('amount')}:amount: wide`, { amount })
            : phrase($localize`:a height in a search:${slot('amount')}:amount: high`, { amount });
    };
    if (clause.width !== null && clause.height !== null)
        return [
            ...not(clause.negated),
            ...(clause.negated ? [OPS.both(), text(' ')] : []),
            ...side(clause.width, true),
            text(' '),
            OPS.and(),
            text(' '),
            ...side(clause.height, false),
        ];
    return [
        ...not(clause.negated),
        ...(clause.width !== null ? side(clause.width, true) : side(clause.height!, false)),
    ];
}

function durationPhrase(clause: Extract<Clause, { kind: 'duration' }>): Segment[] {
    const title =
        clause.min === clause.max
            ? clause.token
            : $localize`${clause.token}:typed:: from ${formatDuration(clause.min)}:min: \
to ${formatDuration(clause.max)}:max:`;
    return [
        ...not(clause.negated),
        ...phrase(DURATION_PHRASES[clause.op](), {
            duration: { ...term(clause.label, 'size', clause.negated), title } as Segment,
        }),
    ];
}
