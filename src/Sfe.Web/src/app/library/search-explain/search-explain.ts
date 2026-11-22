import { formatDuration, formatSize } from '../../core/format';
import { Clause, Group, parseQuery } from './search-query';
import { list, not, op, Segment, term, text } from './search-segments';

export { parseQuery, splitOutsideQuotes, type ParsedQuery } from './search-query';
export type { Segment, TermType } from './search-segments';

/* The search query put into words (the parsing is in search-query.ts). */

const SIZE_PHRASES: Record<string, string> = {
    '<': 'smaller than',
    '<=': 'of at most',
    '>': 'larger than',
    '>=': 'of at least',
    '=': 'of exactly',
    '': 'of about',
};
const PIXEL_PHRASES: Record<string, string> = {
    '<': 'less than',
    '<=': 'at most',
    '>': 'more than',
    '>=': 'at least',
    '=': 'exactly',
    '': 'exactly',
};
const DURATION_PHRASES: Record<string, string> = {
    '<': 'shorter than',
    '<=': 'lasting at most',
    '>': 'longer than',
    '>=': 'lasting at least',
    '=': 'lasting exactly',
    '': 'lasting about',
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
    if (groups.some((group) => group.clauses.some((clause) => clause.kind === 'tag')))
        notes.push([text('A tag also matches by its shorthand or aliases, and includes the tags inheriting from it.')]);
    if (groups.some((group) => group.clauses.some((clause) => clause.kind === 'type')))
        notes.push([
            text(
                'The type of a file comes from its extension, by the lists of Administration › Scanning › File types.',
            ),
        ]);
    const resolution = (clause: Clause) => clause.kind === 'dimension' || clause.kind === 'level';
    if (groups.some((group) => group.clauses.some((clause) => clause.kind === 'level')))
        notes.push([
            text(
                'High and low resolution compare the shorter side with the thresholds of the HR/LR marks, ' +
                    'one for photos and one for videos (Administration › Interface › Browsing).',
            ),
        ]);
    if (groups.some((group) => group.clauses.some((clause) => resolution(clause) && !clause.negated)))
        notes.push([text('A resolution only finds photos and videos whose resolution a scan has read.')]);
    if (groups.some((group) => group.clauses.some((clause) => resolution(clause) && clause.negated)))
        notes.push([
            text(
                'An excluded resolution also finds the files whose resolution is not known (not photos or videos, ' +
                    'or not read yet): add ',
            ),
            term('w:>0', 'size'),
            text(' to leave them out.'),
        ]);
    if (groups.some((group) => group.clauses.some((clause) => clause.kind === 'duration' && !clause.negated)))
        notes.push([text('A duration only finds videos and audio files whose duration a scan has read.')]);
    if (groups.some((group) => group.clauses.some((clause) => clause.kind === 'duration' && clause.negated)))
        notes.push([
            text(
                'An excluded duration also finds the files whose duration is not known (not videos nor audio, ' +
                    'or not read yet): add ',
            ),
            term('d:>0', 'size'),
            text(' to leave them out.'),
        ]);
    if (!groups.length) return { sentence: [text('Nothing to search for yet.')], alternatives: [], notes };
    const scope = path
        ? [
              text('in '),
              { ...term(path.split('/').pop() ?? '', 'folder'), title: path } as Segment,
              text(' and its sub-folders'),
          ]
        : [text('in the whole library')];
    const intro = [text('Finds files '), ...scope];
    const phrases = groups.map((group) => groupPhrase(group, fullPath));
    return phrases.length === 1
        ? { sentence: [...intro, text(' '), ...phrases[0], text('.')], alternatives: [], notes }
        : {
              sentence: [...intro, text(':')],
              alternatives: phrases.map((phrase, i) => [op(i === 0 ? 'either' : 'or'), text(' '), ...phrase]),
              notes,
          };
}

/** The clauses of one alternative, joined by AND. */
function groupPhrase(group: Group, fullPath: boolean): Segment[] {
    return group.clauses.flatMap((clause, i) => [
        ...(i ? [text(' '), op('and'), text(' ')] : []),
        ...clausePhrase(clause, fullPath),
    ]);
}

function clausePhrase(clause: Clause, fullPath: boolean): Segment[] {
    switch (clause.kind) {
        case 'text': {
            const where: Segment[] = [text(' in their '), { kind: 'strong', text: fullPath ? 'full path' : 'name' }];
            const included = list(
                clause.include.map((w) => term(w.value, 'text', false, w.token)),
                'and',
            );
            const excluded = list(
                clause.exclude.map((w) => term(w.value, 'text', true, w.token)),
                'or',
            );
            if (!clause.exclude.length) return [text('with '), ...included, ...where];
            if (!clause.include.length) return [op('without', true), text(' '), ...excluded, ...where];
            return [text('with '), ...included, text(' but '), op('without', true), text(' '), ...excluded, ...where];
        }
        case 'extension':
            // Excluded: neither of them, "without the extension .mp4 or .mkv".
            return [
                ...(clause.negated ? [op('without', true), text(' ')] : [text('with ')]),
                text('the extension '),
                ...list(
                    clause.values.map((e) => term(e.value, 'extension', clause.negated, e.token)),
                    'or',
                ),
            ];
        case 'type':
            // Excluded: none of them, "that are not photos or videos".
            return [
                text('that are '),
                ...not(clause.negated),
                ...list(
                    clause.values.map((k) => term(k.value, 'extension', clause.negated, k.token)),
                    'or',
                ),
            ];
        case 'size': {
            const title = clause.op
                ? clause.token
                : `${clause.token}: from ${formatSize(clause.min)} to ${formatSize(clause.max)}`;
            return [
                ...not(clause.negated),
                text(`${SIZE_PHRASES[clause.op]} `),
                { ...term(clause.label, 'size', clause.negated), title } as Segment,
            ];
        }
        case 'dimension': {
            const side = (pixels: number, word: string): Segment[] => [
                text(`${PIXEL_PHRASES[clause.op]} `),
                { ...term(`${pixels} pixels`, 'size', clause.negated), title: clause.token } as Segment,
                text(` ${word}`),
            ];
            if (clause.width !== null && clause.height !== null)
                return [
                    ...not(clause.negated),
                    ...(clause.negated ? [op('both'), text(' ')] : []),
                    ...side(clause.width, 'wide'),
                    text(' '),
                    op('and'),
                    text(' '),
                    ...side(clause.height, 'high'),
                ];
            return [
                ...not(clause.negated),
                ...(clause.width !== null ? side(clause.width, 'wide') : side(clause.height!, 'high')),
            ];
        }
        case 'level':
            return [
                ...not(clause.negated),
                text('in '),
                {
                    ...term(clause.high ? 'high resolution' : 'low resolution', 'size', clause.negated),
                    title: clause.token,
                } as Segment,
            ];
        case 'duration': {
            const title =
                clause.min === clause.max
                    ? clause.token
                    : `${clause.token}: from ${formatDuration(clause.min)} to ${formatDuration(clause.max)}`;
            return [
                ...not(clause.negated),
                text(`${DURATION_PHRASES[clause.op]} `),
                { ...term(clause.label, 'size', clause.negated), title } as Segment,
            ];
        }
        case 'tag':
            return clause.negated
                ? [op('not', true), text(' tagged '), term(clause.name, 'tag', true, clause.token)]
                : [text('tagged '), term(clause.name, 'tag', false, clause.token)];
        case 'untagged':
            // special:untagged is "no tag at all", -special:untagged "at least one".
            return clause.negated ? [text('with at least one tag')] : [op('without', true), text(' any tag')];
    }
}
