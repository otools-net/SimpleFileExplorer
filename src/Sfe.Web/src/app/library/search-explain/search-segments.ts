/* The pieces a search explanation is made of, and the helpers putting them together. */

/** A piece of the explanation: plain or bold text, a searched value (`term`) or a logical word (`op`). */
export type Segment =
    | { kind: 'text'; text: string }
    | { kind: 'strong'; text: string }
    | { kind: 'term'; value: string; type: TermType; excluded: boolean; title: string | null }
    | { kind: 'op'; word: string; negation: boolean };

export type TermType = 'text' | 'tag' | 'extension' | 'size' | 'folder';

export const text = (value: string): Segment => ({ kind: 'text', text: value });

/** A searched value, coloured by kind; excluded ones are outlined with dashes. The tooltip shows what was typed. */
export function term(value: string, type: TermType, excluded = false, typed?: string): Segment {
    return { kind: 'term', value, type, excluded, title: typed && typed !== value ? `Typed as ${typed}` : null };
}

/** A logical word: AND, OR, EITHER; negations (NOT, WITHOUT) stand out in red. */
export function op(word: string, negation = false): Segment {
    return { kind: 'op', word, negation };
}

/** "NOT " before a negated clause. */
export function not(negated: boolean): Segment[] {
    return negated ? [op('not', true), text(' ')] : [];
}

/** "a", "a and b", "a, b and c". */
export function list(items: Segment[], word: string): Segment[] {
    return items.flatMap((item, i) => {
        if (i === 0) return [item];
        return i === items.length - 1 ? [text(' '), op(word), text(' '), item] : [text(', '), item];
    });
}
