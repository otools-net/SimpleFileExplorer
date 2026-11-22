import { icu } from '../../core/i18n/icu';

/**
 * The JSON viewer's texts (agents/docs/features/json-viewer.md): its modes, the buttons' hints, what the filter found,
 * the members' sizes and where the source isn't JSON. Pure functions, shared by the toolbar and its "⋯" menu.
 */
export const jsonTexts = {
    modes: () =>
        [
            {
                id: 'tree',
                label: $localize`:show a JSON file as a tree:Tree`,
                title: $localize`Show the members as a tree, to open and close.`,
            },
            { id: 'source', label: $localize`Source`, title: $localize`Show the text as written in the file.` },
            { id: 'split', label: $localize`Split`, title: $localize`Show the tree and the text side by side.` },
        ] as const,
    format: () => $localize`Format`,
    formatHint: () => $localize`Lay the source out again, indented, with its comments kept (Ctrl+Z undoes it).`,
    openAll: () => $localize`Open all`,
    openAllHint: () => $localize`Open every member under the one selected (*).`,
    closeAll: () => $localize`Close all`,
    closeAllHint: () => $localize`Close every member.`,
    toggle: (expanded: boolean) =>
        expanded ? $localize`:close a JSON member:Close` : $localize`:open a JSON member:Open`,
    /** The tree's name for screen readers. */
    members: (name: string) => $localize`Members of “${name}:name:”`,
    /** What the strict parse calls a file with comments or trailing commas (the status bar). */
    kind: (strict: boolean) => (strict ? 'JSON' : $localize`JSON with comments`),
};

/** What the filter found: "1 match", "Nothing matches", "The first 2,000 matches" (more were found). */
export function matchCount(count: number, limited: boolean): string {
    if (limited) return icu($localize`The first {count, number} matches`, { count });
    return icu($localize`{count, plural, =0 {Nothing matches} one {# match} other {# matches}}`, { count });
}

/** How many members a container holds: "3 keys", "1 item". */
export function membersCount(count: number, array: boolean): string {
    return array
        ? icu($localize`{count, plural, one {# item} other {# items}}`, { count })
        : icu($localize`{count, plural, one {# key} other {# keys}}`, { count });
}

/** The row closing a long list: "Show 200 more", and "200 of 1,000 shown" beside it. */
export function showMoreLabel(count: number): string {
    return icu($localize`Show {count, number} more`, { count });
}

export function shownOf(shown: number, total: number): string {
    return icu($localize`{shown, number} of {total, number} shown`, { shown, total });
}

/** Where the source first isn't JSON, and how many other places are wrong. */
export function problemLine(problem: { line: number; column: number; message: string }, others: number): string {
    const { line, column, message: reason } = problem;
    return icu(
        $localize`{others, plural, =0 {Not JSON at line {line}, column {column}: {reason}.} \
one {Not JSON at line {line}, column {column}: {reason} (and # more).} \
other {Not JSON at line {line}, column {column}: {reason} (and # more).}}`,
        { others, line, column, reason },
    );
}

/** Why the source isn't JSON, by `jsonc-parser`'s error code (the code itself when it is a new one). */
export function problemReason(code: string): string {
    switch (code) {
        case 'InvalidSymbol':
            return $localize`An unexpected character`;
        case 'InvalidNumberFormat':
            return $localize`Not a valid number`;
        case 'PropertyNameExpected':
            return $localize`A property name, in double quotes, is expected`;
        case 'ValueExpected':
            return $localize`A value is expected`;
        case 'ColonExpected':
            return $localize`A colon is expected`;
        case 'CommaExpected':
            return $localize`A comma is expected`;
        case 'CloseBraceExpected':
            return $localize`A closing } is expected`;
        case 'CloseBracketExpected':
            return $localize`A closing ] is expected`;
        case 'EndOfFileExpected':
            return $localize`Nothing more is expected`;
        default:
            return moreReasons(code);
    }
}

function moreReasons(code: string): string {
    switch (code) {
        case 'InvalidCommentToken':
            return $localize`Comments aren't JSON`;
        case 'UnexpectedEndOfComment':
            return $localize`A comment isn't closed`;
        case 'UnexpectedEndOfString':
            return $localize`A string isn't closed`;
        case 'UnexpectedEndOfNumber':
            return $localize`A number is cut short`;
        case 'InvalidUnicode':
            return $localize`Not a valid Unicode escape`;
        case 'InvalidEscapeCharacter':
            return $localize`Not a valid escape`;
        case 'InvalidCharacter':
            return $localize`A character that isn't allowed in a string`;
        default:
            return code;
    }
}
