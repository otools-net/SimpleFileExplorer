import { applyEdits, format, Node, ParseError, parseTree, printParseErrorCode } from 'jsonc-parser';

/** A container shows this many of its members, then a row to show more. */
export const CHUNK = 200;
/** "Expand all", and what opens at once on a small file: at most this many rows. */
export const EXPAND_LIMIT = 5000;
/** The most matches of a filter (the rest is said, not shown). */
export const MATCH_LIMIT = 2000;
const PREVIEW_LENGTH = 80;
const VALUE_LENGTH = 400;

/** A place in the source where it isn't JSON. */
export interface JsonProblem {
    readonly message: string;
    readonly offset: number;
    readonly length: number;
    readonly line: number;
    readonly column: number;
}

/**
 * The parsed document: the tree as far as it parses (`jsonc-parser`: in the order written, numbers as written), what
 * is wrong, and whether it is strict JSON (no comments, no trailing commas).
 */
export interface JsonDocument {
    readonly text: string;
    readonly root: Node | null;
    readonly problems: readonly JsonProblem[];
    readonly strict: boolean;
}

/** A line of the tree: a member (object property, array item, the root) or the row asking for more members. */
export interface JsonRow {
    /** Its path in the document, `/`-joined with the members' keys escaped (JSON Pointer). */
    readonly id: string;
    readonly depth: number;
    readonly node: Node;
    /** Its name in its parent: a property's key, an item's index; null for the root. */
    readonly key: string | null;
    readonly index: boolean;
    readonly container: boolean;
    readonly expanded: boolean;
    /** A row standing for the members not shown yet of `node` (`shown` of `total`). */
    readonly more?: { readonly shown: number; readonly total: number };
}

const PROBLEMS: Readonly<Record<string, string>> = {
    InvalidSymbol: 'Unexpected character',
    InvalidNumberFormat: 'Not a valid number',
    PropertyNameExpected: 'A property name (in double quotes) is expected',
    ValueExpected: 'A value is expected',
    ColonExpected: 'A colon is expected',
    CommaExpected: 'A comma is expected',
    CloseBraceExpected: 'A closing } is expected',
    CloseBracketExpected: 'A closing ] is expected',
    EndOfFileExpected: 'Nothing more is expected',
    InvalidCommentToken: 'Comments are not JSON',
    UnexpectedEndOfComment: 'A comment is not closed',
    UnexpectedEndOfString: 'A string is not closed',
    UnexpectedEndOfNumber: 'A number is cut short',
    InvalidUnicode: 'Not a valid \\u escape',
    InvalidEscapeCharacter: 'Not a valid escape',
    InvalidCharacter: 'A character not allowed in a string',
};

function problemsOf(text: string, errors: readonly ParseError[]): JsonProblem[] {
    const starts = [0];
    for (let at = text.indexOf('\n'); at >= 0; at = text.indexOf('\n', at + 1))
        starts.push(at + 1);
    return errors.map(error => {
        let low = 0;
        let high = starts.length - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (starts[middle] <= error.offset)
                low = middle;
            else
                high = middle - 1;
        }
        const code = printParseErrorCode(error.error);
        return { message: PROBLEMS[code] ?? code, offset: error.offset, length: error.length, line: low + 1, column: error.offset - starts[low] + 1 };
    });
}

/**
 * Parses `text`: strictly first; when that fails, again allowing comments and trailing commas (`jsonc`, `appsettings`),
 * which are then not problems but make it not strict.
 */
export function parseJson(text: string): JsonDocument {
    const strictErrors: ParseError[] = [];
    const strictRoot = parseTree(text, strictErrors, { disallowComments: true, allowTrailingComma: false }) ?? null;
    if (!strictErrors.length)
        return { text, root: strictRoot, problems: [], strict: true };
    const errors: ParseError[] = [];
    const root = parseTree(text, errors, { disallowComments: false, allowTrailingComma: true }) ?? null;
    return { text, root, problems: problemsOf(text, errors), strict: false };
}

export function isContainer(node: Node): boolean {
    return node.type === 'object' || node.type === 'array';
}

/** The members of an object (property nodes' values) or an array, with their keys. */
export function membersOf(node: Node): { key: string; index: boolean; node: Node }[] {
    const children = node.children ?? [];
    if (node.type === 'array')
        return children.map((child, index) => ({ key: String(index), index: true, node: child }));
    if (node.type !== 'object')
        return [];
    const members: { key: string; index: boolean; node: Node }[] = [];
    for (const property of children) {
        const [name, value] = property.children ?? [];
        if (name && value)
            members.push({ key: String(name.value), index: false, node: value });
    }
    return members;
}

function childId(parent: string, key: string): string {
    return `${parent}/${key.replaceAll('~', '~0').replaceAll('/', '~1')}`;
}

export interface RowOptions {
    /** Ids of the containers open. */
    readonly expanded: ReadonlySet<string>;
    /** How many members a container shows, when more than `CHUNK`. */
    readonly shown?: ReadonlyMap<string, number>;
    /** Only these ids (a filter's matches and the containers holding them); those holding them are open. */
    readonly keep?: ReadonlySet<string> | null;
}

/** The rows shown: only open containers' members, `CHUNK` at a time; nothing proportional to what is closed. */
export function rowsOf(root: Node | null, options: RowOptions): JsonRow[] {
    const rows: JsonRow[] = [];
    if (!root)
        return rows;
    const { expanded, shown, keep } = options;
    // `filtered`: only kept members; below a match that holds none, all of them (what the match is).
    const visit = (node: Node, id: string, key: string | null, index: boolean, depth: number, filtered: boolean) => {
        const container = isContainer(node);
        const holds = filtered && !!keep && holdsKept(id, keep);
        const open = container && (expanded.has(id) || holds);
        rows.push({ id, depth, node, key, index, container, expanded: open });
        if (!open)
            return;
        const limit = shown?.get(id) ?? CHUNK;
        let count = 0;
        let total = 0;
        for (const member of membersOf(node)) {
            const memberId = childId(id, member.key);
            if (holds && !keep!.has(memberId))
                continue;
            total++;
            if (count < limit) {
                count++;
                visit(member.node, memberId, member.key, member.index, depth + 1, holds);
            }
        }
        if (total > count)
            rows.push({ id: `${id}#more`, depth: depth + 1, node, key: null, index: false, container: false, expanded: false, more: { shown: count, total } });
    };
    visit(root, '', null, false, 0, !!keep);
    return rows;
}

/** A kept container is open when something under it matched (not only itself). */
function holdsKept(id: string, keep: ReadonlySet<string>): boolean {
    return keep.has(`${id}\u0000holds`);
}

/**
 * The ids of the containers to open so that everything shows, breadth first, until about `limit` rows: a small file
 * opens whole, a large one as far as that goes.
 */
export function expandAll(root: Node | null, limit = EXPAND_LIMIT, from = ''): Set<string> {
    const open = new Set<string>();
    if (!root)
        return open;
    let start: Node | null = root;
    for (const key of from ? from.slice(1).split('/') : []) {
        const name = key.replaceAll('~1', '/').replaceAll('~0', '~');
        start = start ? membersOf(start).find(member => member.key === name)?.node ?? null : null;
    }
    if (!start)
        return open;
    let rows = 1;
    let level: { node: Node; id: string }[] = [{ node: start, id: from }];
    while (level.length && rows < limit) {
        const next: { node: Node; id: string }[] = [];
        for (const { node, id } of level) {
            if (!isContainer(node) || rows >= limit)
                continue;
            open.add(id);
            const members = membersOf(node);
            rows += Math.min(members.length, CHUNK);
            for (const member of members.slice(0, CHUNK))
                next.push({ node: member.node, id: childId(id, member.key) });
        }
        level = next;
    }
    return open;
}

/**
 * What a filter keeps: members whose key or value holds `query` (any case), and the containers they are in (those
 * marked as holding a match). `count` is every match, `limited` when more than `MATCH_LIMIT` were found.
 */
export function filterTree(root: Node | null, query: string): { keep: Set<string>; count: number; limited: boolean } {
    const keep = new Set<string>();
    const needle = query.trim().toLowerCase();
    let count = 0;
    if (!root || !needle)
        return { keep, count, limited: false };
    const visit = (node: Node, id: string, key: string | null): boolean => {
        if (count >= MATCH_LIMIT)
            return false;
        let found = key !== null && key.toLowerCase().includes(needle);
        if (!found && !isContainer(node))
            found = String(node.value).toLowerCase().includes(needle);
        if (found)
            count++;
        let holds = false;
        for (const member of membersOf(node)) {
            if (visit(member.node, childId(id, member.key), member.key))
                holds = true;
        }
        if (holds)
            keep.add(`${id}\u0000holds`);
        if (found || holds)
            keep.add(id);
        return found || holds;
    };
    visit(root, '', null);
    return { keep, count, limited: count >= MATCH_LIMIT };
}

const IDENTIFIER = /^[A-Za-z_$][\w$]*$/;

/** The member's path as code writes it: `$.store.books[0]["first name"]`. */
export function pathLabel(id: string): string {
    if (id.endsWith('#more'))
        id = id.slice(0, -5);
    let label = '$';
    if (!id)
        return label;
    for (const part of id.slice(1).split('/')) {
        const key = part.replaceAll('~1', '/').replaceAll('~0', '~');
        label += /^\d+$/.test(key) ? `[${key}]` : IDENTIFIER.test(key) ? `.${key}` : `[${JSON.stringify(key)}]`;
    }
    return label;
}

/** A member's own text in the source (numbers and strings exactly as written), at most `max` characters. */
export function sourceOf(node: Node, text: string, max = VALUE_LENGTH): string {
    const value = text.slice(node.offset, node.offset + Math.min(node.length, max + 1));
    return value.length > max ? `${value.slice(0, max)}…` : value;
}

/** What a closed container holds, in a line: `{ "id": 4, "name": "Ann", … }`, `[ 1, 2, 3 ]`. */
export function previewOf(node: Node, text: string, max = PREVIEW_LENGTH): string {
    const array = node.type === 'array';
    let preview = '';
    for (const member of membersOf(node)) {
        const value = isContainer(member.node) ? (member.node.type === 'array' ? '[…]' : '{…}') : sourceOf(member.node, text, 30);
        const part = array ? value : `${JSON.stringify(member.key)}: ${value}`;
        if (preview.length + part.length > max) {
            preview += preview ? ', …' : '…';
            break;
        }
        preview += preview ? `, ${part}` : part;
    }
    return array ? `[ ${preview} ]` : `{ ${preview} }`;
}

/** How many members, in words: `3 keys`, `1 item`. */
export function sizeOf(node: Node): string {
    const count = node.type === 'array' ? node.children?.length ?? 0 : membersOf(node).length;
    const word = node.type === 'array' ? 'item' : 'key';
    return `${count.toLocaleString()} ${word}${count === 1 ? '' : 's'}`;
}

/** The colour class of a value (`tok-*`, as the source's highlighting). */
export function valueClass(node: Node): string {
    switch (node.type) {
        case 'string': return 'tok-string';
        case 'number': return 'tok-number';
        case 'boolean': return 'tok-bool';
        case 'null': return 'tok-keyword';
        default: return 'json-punctuation';
    }
}

/** The indentation the source uses (a tab, or its first indented line's spaces; two when none). */
export function indentOf(text: string): { insertSpaces: boolean; tabSize: number } {
    const match = /\n([ \t]+)\S/.exec(text);
    if (match?.[1].startsWith('\t'))
        return { insertSpaces: false, tabSize: 4 };
    const size = match ? match[1].length : 2;
    return { insertSpaces: true, tabSize: size >= 1 && size <= 8 ? size : 2 };
}

/** The changes laying the source out again (indented as it is, comments kept), as `{ offset, length, content }`. */
export function formatEdits(text: string): { offset: number; length: number; content: string }[] {
    return format(text, undefined, { ...indentOf(text), eol: '\n', insertFinalNewline: text.endsWith('\n') });
}

/** The source laid out again (for tests and copies). */
export function formatted(text: string): string {
    return applyEdits(text, formatEdits(text));
}
