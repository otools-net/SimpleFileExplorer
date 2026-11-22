import { icu } from '../../core/i18n/icu';
import { extensionNote, ignoredNote } from './search-notes';
import { phrase, Segment, slot, term } from './search-segments';

/*
 * The search query parsed. The parser mirrors SearchQuery.Parse (Sfe.Core/Search/SearchQuery.cs) quirk for
 * quirk, so the sentence says what the server will actually do: keep the two in step.
 */

const SIZE_PATTERN = /^(<=|>=|<|>|=)?(\d+(?:\.\d+)?)(b|kb|mb|gb|tb)?$/i;
const SIZE_UNITS: Record<string, number> = { b: 1, kb: 1024, mb: 1024 ** 2, gb: 1024 ** 3, tb: 1024 ** 4 };
const PIXELS_PATTERN = /^(<=|>=|<|>|=)?(\d{1,9})$/;
const RESOLUTION_PATTERN = /^(<=|>=|<|>|=)?(\d{1,9})x(\d{1,9})$/i;
const DURATION_PATTERN = /^(<=|>=|<|>|=)?(?:(\d{1,9}(?:\.\d+)?)(s|m|h)?|(\d{1,6}(?::\d{1,2}){1,2}))$/i;
const DURATION_UNITS: Record<string, [seconds: number, label: () => string]> = {
    s: [1, () => $localize`{count, plural, one {# second} other {# seconds}}`],
    m: [60, () => $localize`{count, plural, one {# minute} other {# minutes}}`],
    h: [3600, () => $localize`{count, plural, one {# hour} other {# hours}}`],
};
const LEVELS: Record<string, boolean> = { hr: true, lr: false };
/** The values of `type:` (SearchQuery.TryParseKind) and how a sentence names them. */
const KINDS: Record<string, () => string> = {
    image: () => $localize`:type of files in a search:photos`,
    video: () => $localize`:type of files in a search:videos`,
    audio: () => $localize`:type of files in a search:audio files`,
    other: () => $localize`:type of files in a search:other files`,
};
const DIMENSION_PREFIXES = ['w:', 'h:', 'r:'];
const TAG_PREFIXES = ['t:', 'tag:'];
const UNTAGGED = 'special:untagged';
const LOOKS_LIKE_EXTENSION = /\.[a-z][a-z0-9]{1,3}$/i; // "clip.mp4": names are stored without it

interface Word {
    value: string;
    token: string;
}

export type Clause =
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

export interface Group {
    clauses: Clause[];
}

export interface ParsedQuery {
    fullPath: boolean;
    groups: Group[];
    /** The parts that don't do what they may seem to. */
    notes: Segment[][];
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
    if (fullPath) query = query.replaceAll('*', '');
    else if (query.includes('*'))
        notes.push(
            phrase(
                $localize`A ${slot('star')}:star: only means “search the whole path” at the very start of the search; \
here it is searched as a character.`,
                { star: term('*', 'text') },
            ),
        );
    if ((query.match(/"/g) || []).length % 2)
        notes.push(phrase($localize`A double quote isn't closed: everything after it is one term.`));

    const groups = splitOutsideQuotes(query, ',')
        .map((group) => parseGroup(group, fullPath, notes))
        .filter((group) => group.clauses.length);
    return { fullPath, groups, notes };
}

export function splitOutsideQuotes(value: string, separator: string): string[] {
    const parts: string[] = [];
    let start = 0;
    let quoted = false;
    for (let i = 0; i <= value.length; i++) {
        if (i < value.length && value[i] === '"') quoted = !quoted;
        if (i < value.length && (quoted || value[i] !== separator)) continue;
        const part = value.slice(start, i).trim();
        if (part) parts.push(part);
        start = i + 1;
    }
    return parts;
}

const unquote = (value: string) => value.replaceAll('"', '');
const hasPrefix = (token: string, prefix: string) => token.slice(0, prefix.length).toLowerCase() === prefix;

function parseGroup(group: string, fullPath: boolean, notes: Segment[][]): Group {
    const clauses: Clause[] = [];
    let words: Extract<Clause, { kind: 'text' }> | null = null;
    // The included extensions or types (OR-ed) are one clause, the excluded ones another.
    type Listed = Extract<Clause, { kind: 'extension' | 'type' }>;
    const lists: Record<string, Listed | null> = {};

    const addWord = (word: string, excluded: boolean, token: string) => {
        if (!words) clauses.push((words = { kind: 'text', include: [], exclude: [] }));
        (excluded ? words.exclude : words.include).push({ value: word, token });
    };
    const addListed = (kind: Listed['kind'], value: string, token: string, negated: boolean) => {
        const key = `${kind}:${negated}`;
        let list = lists[key];
        if (!list) clauses.push((list = lists[key] = { kind, values: [], negated } as Listed));
        list.values.push({ value, token });
    };

    for (const token of splitOutsideQuotes(group, ' ')) {
        const negated = token[0] === '-';
        const body = negated ? token.slice(1) : token;
        const tag = parseTag(body, negated, token);
        if (tag) {
            clauses.push(tag);
            continue;
        }
        if (TAG_PREFIXES.some((prefix) => hasPrefix(body, prefix))) notes.push(ignoredNote('tag', token, negated));

        // A "-" before a size, resolution or extension excludes what the term finds (SearchGroup.ExcludedFilters).
        if (hasPrefix(body, 's:')) {
            const size = parseSize(body.slice(2), token, negated);
            if (size) clauses.push(size);
            else notes.push(ignoredNote('size', token, negated));
        } else if (hasPrefix(body, 'type:')) {
            const value = body.slice(5).toLowerCase();
            if (value in KINDS) addListed('type', KINDS[value](), token, negated);
            else notes.push(ignoredNote('type', token, negated));
        } else if (hasPrefix(body, 'd:')) {
            const duration = parseDuration(body.slice(2), token, negated);
            if (duration) clauses.push(duration);
            else notes.push(ignoredNote('duration', token, negated));
        } else if (hasPrefix(body, 'r:') && body.slice(2).toLowerCase() in LEVELS) {
            clauses.push({ kind: 'level', high: LEVELS[body.slice(2).toLowerCase()], token, negated });
        } else if (DIMENSION_PREFIXES.some((prefix) => hasPrefix(body, prefix))) {
            const dimension = parseDimension(body, token, negated);
            if (dimension) clauses.push(dimension);
            else notes.push(ignoredNote('resolution', token, negated));
        } else if (hasPrefix(body, 'e:')) {
            const extension = body.slice(2).replace(/^\.+/, '');
            if (extension) addListed('extension', `.${extension}`, token, negated);
            else notes.push(ignoredNote('extension', token, negated));
        } else {
            const plain = plainWord(token, body, negated, fullPath);
            if (plain) addWord(plain.word, negated, token);
            if (plain?.note) notes.push(plain.note);
        }
    }
    return { clauses };
}

/** A plain word of the search ("-" excludes it), and a note when it looks like a name with its extension. */
function plainWord(token: string, body: string, negated: boolean, fullPath: boolean) {
    const word = unquote(negated ? body : token);
    if (!word) return null;
    const extension = !negated && !fullPath && LOOKS_LIKE_EXTENSION.exec(word)?.[0];
    return { word, note: extension ? extensionNote(word, extension) : null };
}

/** Like SearchQuery.TryParseTag: `special:untagged` is "no tag" and its negation "any tag". */
function parseTag(token: string, negated: boolean, typed: string): Clause | null {
    if (token.toLowerCase() === UNTAGGED) return { kind: 'untagged', negated, token: typed };
    const prefix = TAG_PREFIXES.find((p) => hasPrefix(token, p));
    const name = prefix ? unquote(token.slice(prefix.length)).trim() : '';
    return name ? { kind: 'tag', name, negated, token: typed } : null;
}

function parseSize(value: string, token: string, negated: boolean): Clause | null {
    const match = SIZE_PATTERN.exec(value);
    if (!match) return null;
    const op = match[1] || '';
    const unit = (match[3] || 'mb').toLowerCase();
    const number = Number(match[2]);
    const bytes = Math.round(number * SIZE_UNITS[unit]);
    // "s:700mb" is anything that rounds to 700 MB, as on the server (half a unit, integer division).
    const tolerance = op ? 0 : Math.floor(SIZE_UNITS[unit] / 2);
    const label =
        unit === 'b'
            ? icu($localize`{count, plural, one {# byte} other {# bytes}}`, { count: number })
            : `${match[2]} ${unit.toUpperCase()}`;
    return { kind: 'size', op, label, min: bytes - tolerance, max: bytes + tolerance, token, negated };
}

/** Like DimensionFilter.Parse: `w:`/`h:` take a number of pixels, `r:` width x height; no operator = exactly. */
function parseDimension(body: string, token: string, negated: boolean): Clause | null {
    const prefix = body.slice(0, 2).toLowerCase();
    const value = body.slice(2);
    if (prefix === 'r:') {
        const match = RESOLUTION_PATTERN.exec(value);
        return match
            ? {
                  kind: 'dimension',
                  op: match[1] || '',
                  width: Number(match[2]),
                  height: Number(match[3]),
                  token,
                  negated,
              }
            : null;
    }
    const match = PIXELS_PATTERN.exec(value);
    if (!match) return null;
    const pixels = Number(match[2]);
    return {
        kind: 'dimension',
        op: match[1] || '',
        width: prefix === 'w:' ? pixels : null,
        height: prefix === 'h:' ? pixels : null,
        token,
        negated,
    };
}

/** Like DurationFilter.TryParse: a number with s, m (default) or h, or a clock (m:ss, h:mm:ss); none = about. */
function parseDuration(value: string, token: string, negated: boolean): Clause | null {
    const match = DURATION_PATTERN.exec(value);
    if (!match) return null;
    const op = match[1] || '';
    let seconds: number, unit: number, label: string;
    if (match[4]) {
        seconds = match[4].split(':').reduce((total, part) => total * 60 + Number(part), 0);
        unit = 1;
        label = match[4];
    } else {
        const [size, message] = DURATION_UNITS[(match[3] || 'm').toLowerCase()];
        unit = size;
        seconds = Number(match[2]) * size;
        label = icu(message(), { count: Number(match[2]) });
    }
    // 'd:10m' is anything that rounds to ten minutes; '=' the same whole second, as on the server.
    const tolerance = !op ? unit / 2 : op === '=' ? 0.5 : 0;
    return { kind: 'duration', op, label, min: seconds - tolerance, max: seconds + tolerance, token, negated };
}
