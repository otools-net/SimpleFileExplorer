import { formatDuration, formatSize } from '../../core/format';

/*
 * The search query put into words. The parser mirrors SearchQuery.Parse (Sfe.Core/Search/SearchQuery.cs) quirk for
 * quirk, so the sentence says what the server will actually do: keep the two in step.
 */

const SIZE_PATTERN = /^(<=|>=|<|>|=)?(\d+(?:\.\d+)?)(b|kb|mb|gb|tb)?$/i;
const SIZE_UNITS: Record<string, number> = { b: 1, kb: 1024, mb: 1024 ** 2, gb: 1024 ** 3, tb: 1024 ** 4 };
const SIZE_PHRASES: Record<string, string> = { '<': 'smaller than', '<=': 'of at most', '>': 'larger than', '>=': 'of at least', '=': 'of exactly', '': 'of about' };
const PIXELS_PATTERN = /^(<=|>=|<|>|=)?(\d{1,9})$/;
const RESOLUTION_PATTERN = /^(<=|>=|<|>|=)?(\d{1,9})x(\d{1,9})$/i;
const PIXEL_PHRASES: Record<string, string> = { '<': 'less than', '<=': 'at most', '>': 'more than', '>=': 'at least', '=': 'exactly', '': 'exactly' };
const DURATION_PATTERN = /^(<=|>=|<|>|=)?(?:(\d{1,9}(?:\.\d+)?)(s|m|h)?|(\d{1,6}(?::\d{1,2}){1,2}))$/i;
const DURATION_UNITS: Record<string, [seconds: number, singular: string]> = { s: [1, 'second'], m: [60, 'minute'], h: [3600, 'hour'] };
const DURATION_PHRASES: Record<string, string> = { '<': 'shorter than', '<=': 'lasting at most', '>': 'longer than', '>=': 'lasting at least', '=': 'lasting exactly', '': 'lasting about' };
const LEVELS: Record<string, boolean> = { hr: true, lr: false };
/** The values of `type:` (SearchQuery.TryParseKind) and how a sentence names them. */
const KINDS: Record<string, string> = { image: 'photos', video: 'videos', audio: 'audio files', other: 'other files' };
const DIMENSION_PREFIXES = ['w:', 'h:', 'r:'];
const TAG_PREFIXES = ['t:', 'tag:'];
const UNTAGGED = 'special:untagged';
const LOOKS_LIKE_EXTENSION = /\.[a-z][a-z0-9]{1,3}$/i;   // "clip.mp4": names are stored without it

/** A piece of the explanation: plain or bold text, a searched value (`term`) or a logical word (`op`). */
export type Segment =
    | { kind: 'text'; text: string }
    | { kind: 'strong'; text: string }
    | { kind: 'term'; value: string; type: TermType; excluded: boolean; title: string | null }
    | { kind: 'op'; word: string; negation: boolean };

export type TermType = 'text' | 'tag' | 'extension' | 'size' | 'folder';

interface Word { value: string; token: string }

type Clause =
    | { kind: 'text'; include: Word[]; exclude: Word[] }
    /** The included extensions (OR-ed), or the excluded ones (each left out). */
    | { kind: 'extension'; values: Word[]; negated: boolean }
    /** `type:`, grouped like extensions: the included kinds (OR-ed), or the excluded ones. */
    | { kind: 'type'; values: Word[]; negated: boolean }
    | { kind: 'size'; op: string; label: string; min: number; max: number; token: string; negated: boolean }
    /** `w:`, `h:` or `r:` (both sides, compared alike); null for a side not compared. Negated = not the whole term. */
    | { kind: 'dimension'; op: string; width: number | null; height: number | null; token: string; negated: boolean }
    /** `r:hr` / `r:lr`: by the thresholds of the HR/LR marks. */
    | { kind: 'level'; high: boolean; token: string; negated: boolean }
    /** `d:`; min and max in seconds (an approximate duration is a range). */
    | { kind: 'duration'; op: string; label: string; min: number; max: number; token: string; negated: boolean }
    | { kind: 'tag'; name: string; negated: boolean; token: string }
    | { kind: 'untagged'; negated: boolean; token: string };

interface Group { clauses: Clause[] }

export interface ParsedQuery {
    fullPath: boolean;
    groups: Group[];
    /** The parts that don't do what they may seem to. */
    notes: Segment[][];
}

/** The query explained: a sentence, followed by a list when there are several alternatives. */
export interface Explanation {
    sentence: Segment[];
    alternatives: Segment[][];
    notes: Segment[][];
}

const text = (value: string): Segment => ({ kind: 'text', text: value });

/** A searched value, coloured by kind; excluded ones are outlined with dashes. The tooltip shows what was typed. */
function term(value: string, type: TermType, excluded = false, typed?: string): Segment {
    return { kind: 'term', value, type, excluded, title: typed && typed !== value ? `Typed as ${typed}` : null };
}

/** A logical word: AND, OR, EITHER; negations (NOT, WITHOUT) stand out in red. */
function op(word: string, negation = false): Segment {
    return { kind: 'op', word, negation };
}

/* ---------- Parsing (see SearchQuery.Parse) ---------- */

/**
 * A group lists its clauses in the order they were first typed: one `text` clause for all the words (included and
 * excluded), one `extension` clause for all the included extensions (they are OR-ed) and one for the excluded ones,
 * one clause per size, resolution and tag.
 */
export function parseQuery(input: string): ParsedQuery {
    let query = input.trim();
    const notes: Segment[][] = [];
    const fullPath = query.startsWith('*');
    if (fullPath)
        query = query.replaceAll('*', '');
    else if (query.includes('*'))
        notes.push([text('A '), term('*', 'text'), text(' only means “search the whole path” at the very start of the search; here it is searched as a character.')]);
    if ((query.match(/"/g) || []).length % 2)
        notes.push([text('A double quote is not closed: everything after it is one term.')]);

    const groups = splitOutsideQuotes(query, ',')
        .map(group => parseGroup(group, fullPath, notes))
        .filter(group => group.clauses.length);
    return { fullPath, groups, notes };
}

export function splitOutsideQuotes(value: string, separator: string): string[] {
    const parts: string[] = [];
    let start = 0;
    let quoted = false;
    for (let i = 0; i <= value.length; i++) {
        if (i < value.length && value[i] === '"')
            quoted = !quoted;
        if (i < value.length && (quoted || value[i] !== separator))
            continue;
        const part = value.slice(start, i).trim();
        if (part)
            parts.push(part);
        start = i + 1;
    }
    return parts;
}

const unquote = (value: string) => value.replaceAll('"', '');
const hasPrefix = (token: string, prefix: string) => token.slice(0, prefix.length).toLowerCase() === prefix;

function parseGroup(group: string, fullPath: boolean, notes: Segment[][]): Group {
    const clauses: Clause[] = [];
    let words: Extract<Clause, { kind: 'text' }> | null = null;
    const extensions: Record<'in' | 'out', Extract<Clause, { kind: 'extension' }> | null> = { in: null, out: null };
    const types: Record<'in' | 'out', Extract<Clause, { kind: 'type' }> | null> = { in: null, out: null };

    const addWord = (word: string, excluded: boolean, token: string) => {
        if (!words)
            clauses.push(words = { kind: 'text', include: [], exclude: [] });
        (excluded ? words.exclude : words.include).push({ value: word, token });
    };

    for (const token of splitOutsideQuotes(group, ' ')) {
        const negated = token[0] === '-';
        const body = negated ? token.slice(1) : token;
        const tag = parseTag(body, negated, token);
        if (tag) {
            clauses.push(tag);
            continue;
        }
        if (TAG_PREFIXES.some(prefix => hasPrefix(body, prefix)))
            notes.push([term(token, 'text'), text(' has no tag name: it is searched as text.')]);

        // A "-" before a size, resolution or extension excludes what the term finds (SearchGroup.ExcludedFilters).
        if (hasPrefix(body, 's:')) {
            const size = parseSize(body.slice(2), token, negated);
            if (size)
                clauses.push(size);
            else
                notes.push([term(token, 'size', negated), text(' is ignored: not a valid size (try '), term('s:>1gb', 'size'), text(' or '), term('s:<=10mb', 'size'), text(').')]);
        } else if (hasPrefix(body, 'type:')) {
            const value = body.slice(5).toLowerCase();
            if (!(value in KINDS)) {
                notes.push([term(token, 'extension', negated), text(' is ignored: not a type of file (try '), term('type:image', 'extension'), text(', '),
                    term('type:video', 'extension'), text(', '), term('type:audio', 'extension'), text(' or '), term('type:other', 'extension'), text(').')]);
                continue;
            }
            const key = negated ? 'out' : 'in';
            if (!types[key])
                clauses.push(types[key] = { kind: 'type', values: [], negated });
            types[key].values.push({ value: KINDS[value], token });
        } else if (hasPrefix(body, 'd:')) {
            const duration = parseDuration(body.slice(2), token, negated);
            if (duration)
                clauses.push(duration);
            else
                notes.push([term(token, 'size', negated), text(' is ignored: not a valid duration (try '), term('d:>10m', 'size'), text(' or '), term('d:<=1:30:00', 'size'), text(').')]);
        } else if (hasPrefix(body, 'r:') && body.slice(2).toLowerCase() in LEVELS) {
            clauses.push({ kind: 'level', high: LEVELS[body.slice(2).toLowerCase()], token, negated });
        } else if (DIMENSION_PREFIXES.some(prefix => hasPrefix(body, prefix))) {
            const dimension = parseDimension(body, token, negated);
            if (dimension)
                clauses.push(dimension);
            else
                notes.push([term(token, 'size', negated), text(' is ignored: not a valid resolution (try '), term('w:>=1920', 'size'), text(' or '), term('r:1920x1080', 'size'), text(').')]);
        } else if (hasPrefix(body, 'e:')) {
            const extension = body.slice(2).replace(/^\.+/, '');
            if (!extension) {
                notes.push([term(token, 'extension', negated), text(' is ignored: the extension is missing.')]);
                continue;
            }
            const key = negated ? 'out' : 'in';
            if (!extensions[key])
                clauses.push(extensions[key] = { kind: 'extension', values: [], negated });
            extensions[key].values.push({ value: `.${extension}`, token });
        } else if (negated) {
            const word = unquote(body);
            if (word)
                addWord(word, true, token);
        } else if (unquote(token)) {
            const word = unquote(token);
            addWord(word, false, token);
            const extension = !fullPath && LOOKS_LIKE_EXTENSION.exec(word)?.[0];
            if (extension)
                notes.push([text('Names are searched without their extension, so '), term(word, 'text'), text(' may find nothing: try '),
                    term([word.slice(0, -extension.length), `e:${extension.slice(1)}`].filter(Boolean).join(' '), 'text'), text('.')]);
        }
    }
    return { clauses };
}

/** Like SearchQuery.TryParseTag: `special:untagged` is "no tag" and its negation "any tag". */
function parseTag(token: string, negated: boolean, typed: string): Clause | null {
    if (token.toLowerCase() === UNTAGGED)
        return { kind: 'untagged', negated, token: typed };
    const prefix = TAG_PREFIXES.find(p => hasPrefix(token, p));
    const name = prefix ? unquote(token.slice(prefix.length)).trim() : '';
    return name ? { kind: 'tag', name, negated, token: typed } : null;
}

function parseSize(value: string, token: string, negated: boolean): Clause | null {
    const match = SIZE_PATTERN.exec(value);
    if (!match)
        return null;
    const op = match[1] || '';
    const unit = (match[3] || 'mb').toLowerCase();
    const number = Number(match[2]);
    const bytes = Math.round(number * SIZE_UNITS[unit]);
    // "s:700mb" is anything that rounds to 700 MB, as on the server (half a unit, integer division).
    const tolerance = op ? 0 : Math.floor(SIZE_UNITS[unit] / 2);
    const label = unit === 'b' ? `${match[2]} ${number === 1 ? 'byte' : 'bytes'}` : `${match[2]} ${unit.toUpperCase()}`;
    return { kind: 'size', op, label, min: bytes - tolerance, max: bytes + tolerance, token, negated };
}

/** Like DimensionFilter.Parse: `w:`/`h:` take a number of pixels, `r:` width x height; no operator = exactly. */
function parseDimension(body: string, token: string, negated: boolean): Clause | null {
    const prefix = body.slice(0, 2).toLowerCase();
    const value = body.slice(2);
    if (prefix === 'r:') {
        const match = RESOLUTION_PATTERN.exec(value);
        return match ? { kind: 'dimension', op: match[1] || '', width: Number(match[2]), height: Number(match[3]), token, negated } : null;
    }
    const match = PIXELS_PATTERN.exec(value);
    if (!match)
        return null;
    const pixels = Number(match[2]);
    return { kind: 'dimension', op: match[1] || '', width: prefix === 'w:' ? pixels : null, height: prefix === 'h:' ? pixels : null, token, negated };
}

/** Like DurationFilter.TryParse: a number with s, m (default) or h, or a clock (m:ss, h:mm:ss); none = about. */
function parseDuration(value: string, token: string, negated: boolean): Clause | null {
    const match = DURATION_PATTERN.exec(value);
    if (!match)
        return null;
    const op = match[1] || '';
    let seconds: number, unit: number, label: string;
    if (match[4]) {
        seconds = match[4].split(':').reduce((total, part) => total * 60 + Number(part), 0);
        unit = 1;
        label = match[4];
    } else {
        const [size, name] = DURATION_UNITS[(match[3] || 'm').toLowerCase()];
        unit = size;
        seconds = Number(match[2]) * size;
        label = `${match[2]} ${name}${Number(match[2]) === 1 ? '' : 's'}`;
    }
    // 'd:10m' is anything that rounds to ten minutes; '=' the same whole second, as on the server.
    const tolerance = !op ? unit / 2 : op === '=' ? 0.5 : 0;
    return { kind: 'duration', op, label, min: seconds - tolerance, max: seconds + tolerance, token, negated };
}

/* ---------- Wording ---------- */

/** The query put into words, searching in `path` ('' = the whole library). */
export function explain(query: string, path: string): Explanation {
    const { fullPath, groups, notes } = parseQuery(query);
    if (groups.some(group => group.clauses.some(clause => clause.kind === 'tag')))
        notes.push([text('A tag also matches by its shorthand or aliases, and includes the tags inheriting from it.')]);
    if (groups.some(group => group.clauses.some(clause => clause.kind === 'type')))
        notes.push([text('The type of a file comes from its extension, by the lists of Administration › Scanning › File types.')]);
    const resolution = (clause: Clause) => clause.kind === 'dimension' || clause.kind === 'level';
    if (groups.some(group => group.clauses.some(clause => clause.kind === 'level')))
        notes.push([text('High and low resolution compare the shorter side with the thresholds of the HR/LR marks, one for photos and one for videos (Administration › Interface › Browsing).')]);
    if (groups.some(group => group.clauses.some(clause => resolution(clause) && !clause.negated)))
        notes.push([text('A resolution only finds photos and videos whose resolution a scan has read.')]);
    if (groups.some(group => group.clauses.some(clause => resolution(clause) && clause.negated)))
        notes.push([text('An excluded resolution also finds the files whose resolution is not known (not photos or videos, or not read yet): add '),
            term('w:>0', 'size'), text(' to leave them out.')]);
    if (groups.some(group => group.clauses.some(clause => clause.kind === 'duration' && !clause.negated)))
        notes.push([text('A duration only finds videos and audio files whose duration a scan has read.')]);
    if (groups.some(group => group.clauses.some(clause => clause.kind === 'duration' && clause.negated)))
        notes.push([text('An excluded duration also finds the files whose duration is not known (not videos nor audio, or not read yet): add '),
            term('d:>0', 'size'), text(' to leave them out.')]);
    if (!groups.length)
        return { sentence: [text('Nothing to search for yet.')], alternatives: [], notes };
    const scope = path
        ? [text('in '), { ...term(path.split('/').pop() ?? '', 'folder'), title: path } as Segment, text(' and its sub-folders')]
        : [text('in the whole library')];
    const intro = [text('Finds files '), ...scope];
    const phrases = groups.map(group => groupPhrase(group, fullPath));
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
    return group.clauses.flatMap((clause, i) => [...(i ? [text(' '), op('and'), text(' ')] : []), ...clausePhrase(clause, fullPath)]);
}

function clausePhrase(clause: Clause, fullPath: boolean): Segment[] {
    switch (clause.kind) {
        case 'text': {
            const where: Segment[] = [text(' in their '), { kind: 'strong', text: fullPath ? 'full path' : 'name' }];
            const included = list(clause.include.map(w => term(w.value, 'text', false, w.token)), 'and');
            const excluded = list(clause.exclude.map(w => term(w.value, 'text', true, w.token)), 'or');
            if (!clause.exclude.length)
                return [text('with '), ...included, ...where];
            if (!clause.include.length)
                return [op('without', true), text(' '), ...excluded, ...where];
            return [text('with '), ...included, text(' but '), op('without', true), text(' '), ...excluded, ...where];
        }
        case 'extension':
            // Excluded: neither of them, "without the extension .mp4 or .mkv".
            return [...(clause.negated ? [op('without', true), text(' ')] : [text('with ')]), text('the extension '),
                ...list(clause.values.map(e => term(e.value, 'extension', clause.negated, e.token)), 'or')];
        case 'type':
            // Excluded: none of them, "that are not photos or videos".
            return [text('that are '), ...not(clause.negated), ...list(clause.values.map(k => term(k.value, 'extension', clause.negated, k.token)), 'or')];
        case 'size': {
            const title = clause.op ? clause.token : `${clause.token}: from ${formatSize(clause.min)} to ${formatSize(clause.max)}`;
            return [...not(clause.negated), text(`${SIZE_PHRASES[clause.op]} `), { ...term(clause.label, 'size', clause.negated), title } as Segment];
        }
        case 'dimension': {
            const side = (pixels: number, word: string): Segment[] =>
                [text(`${PIXEL_PHRASES[clause.op]} `), { ...term(`${pixels} pixels`, 'size', clause.negated), title: clause.token } as Segment, text(` ${word}`)];
            if (clause.width !== null && clause.height !== null)
                return [...not(clause.negated), ...(clause.negated ? [op('both'), text(' ')] : []),
                    ...side(clause.width, 'wide'), text(' '), op('and'), text(' '), ...side(clause.height, 'high')];
            return [...not(clause.negated), ...(clause.width !== null ? side(clause.width, 'wide') : side(clause.height!, 'high'))];
        }
        case 'level':
            return [...not(clause.negated), text('in '), { ...term(clause.high ? 'high resolution' : 'low resolution', 'size', clause.negated), title: clause.token } as Segment];
        case 'duration': {
            const title = clause.min === clause.max ? clause.token : `${clause.token}: from ${formatDuration(clause.min)} to ${formatDuration(clause.max)}`;
            return [...not(clause.negated), text(`${DURATION_PHRASES[clause.op]} `), { ...term(clause.label, 'size', clause.negated), title } as Segment];
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

/** "NOT " before a negated clause. */
function not(negated: boolean): Segment[] {
    return negated ? [op('not', true), text(' ')] : [];
}

/** "a", "a and b", "a, b and c". */
function list(items: Segment[], word: string): Segment[] {
    return items.flatMap((item, i) => {
        if (i === 0)
            return [item];
        return i === items.length - 1 ? [text(' '), op(word), text(' '), item] : [text(', '), item];
    });
}
