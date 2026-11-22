/* The pieces a search explanation is made of, and the helpers putting them together. */

/** A piece of the explanation: plain or bold text, a searched value (`term`) or a logical word (`op`). */
export type Segment =
    | { kind: 'text'; text: string }
    | { kind: 'strong'; text: string }
    | { kind: 'term'; value: string; type: TermType; excluded: boolean; title: string | null }
    | { kind: 'op'; word: string; negation: boolean };

export type TermType = 'text' | 'tag' | 'extension' | 'size' | 'folder';

export const text = (value: string): Segment => ({ kind: 'text', text: value });

/**
 * A place in a translated sentence where pieces go: `$localize` takes it as a named placeholder
 * (`${slot('size')}:size:`), so translators move it with the words, then `phrase` puts the pieces there.
 */
export const slot = (name: string) => `\u0001${name}\u0001`;

/** A translated sentence as pieces: its text, and `pieces[name]` where `slot(name)` is. */
export function phrase(message: string, pieces: Record<string, Segment | Segment[]> = {}): Segment[] {
    return message.split(/\u0001(\w+)\u0001/).flatMap((part, i) => {
        if (i % 2) return pieces[part] ?? [];
        return part ? [text(part)] : [];
    });
}

/** A searched value, coloured by kind; excluded ones are outlined with dashes. The tooltip shows what was typed. */
export function term(value: string, type: TermType, excluded = false, typed?: string): Segment {
    const title = typed && typed !== value ? $localize`Typed as ${typed}:typed:` : null;
    return { kind: 'term', value, type, excluded, title };
}

/** A logical word: AND, OR, EITHER; negations (NOT, WITHOUT) stand out in red (shown in capitals). */
export function op(word: string, negation = false): Segment {
    return { kind: 'op', word, negation };
}

/** The logical words, shown in capitals in the explanation. */
export const OPS = {
    and: () => op($localize`:logical word of a search, shown in capitals:and`),
    or: () => op($localize`:logical word of a search, shown in capitals:or`),
    either: () => op($localize`:logical word of a search, first of several alternatives, shown in capitals:either`),
    both: () => op($localize`:logical word of a search, shown in capitals:both`),
    not: () => op($localize`:logical word of a search, shown in capitals:not`, true),
    without: () => op($localize`:logical word of a search, shown in capitals:without`, true),
};

/** "NOT " before a negated clause. */
export function not(negated: boolean): Segment[] {
    return negated ? [OPS.not(), text(' ')] : [];
}

/** "a", "a and b", "a, b and c" (`word`: AND or OR). */
export function list(items: Segment[], word: () => Segment): Segment[] {
    return items.flatMap((item, i) => {
        if (i === 0) return [item];
        return i === items.length - 1 ? [text(' '), word(), text(' '), item] : [text(', '), item];
    });
}
