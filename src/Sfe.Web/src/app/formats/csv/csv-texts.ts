import { icu } from '../../core/i18n/icu';
import type { SortDirection } from './csv-table';

/** The separators by a stable key (menu ids), each with its own messages. */
export type SeparatorKey = 'guess' | 'comma' | 'semicolon' | 'tab' | 'pipe';

/**
 * The CSV viewer's texts (agents/docs/features/csv-viewer.md): its modes, the header and separator choices, the
 * counts of the status bar, the cell selected, the sort hints and what is wrong in the file. Pure functions, shared by
 * the toolbar and its "⋯" menu.
 */
export const csvTexts = {
    modes: () =>
        [
            {
                id: 'table',
                label: $localize`:show a CSV file as a table:Table`,
                title: $localize`Show the rows and columns as a table.`,
            },
            { id: 'source', label: $localize`Source`, title: $localize`Show the text as written in the file.` },
            { id: 'split', label: $localize`Split`, title: $localize`Show the table and the text side by side.` },
        ] as const,
    header: () => $localize`:the first row names the columns:Header`,
    headerHint: () => $localize`Use the first row as the names of the columns.`,
    separator: () => $localize`:what separates the cells of a CSV file:Separator`,
    separatorHint: () => $localize`Choose what separates the cells.`,
    /** The cell selected, in the status bar: "Row 3 · km: 1,014". */
    /** The toast of Copy cell. */
    cellCopied: (row: number, column: string) =>
        icu($localize`Cell in row {row, number}, column “{column}” copied.`, { row, column }),
    cell: (row: number, column: string, value: string) =>
        icu($localize`Row {row, number} · {column}: {value}`, { row, column, value }),
    /** What the table says when no row shows: none holds what the filter has, or the file has none. */
    noMatch: (query: string) => $localize`No row holds “${query}:query:”.`,
    empty: () => $localize`This file has no rows.`,
};

/** A separator's name in the list ("Separator: guess" for the first, shown when the list is closed). */
export function separatorLabel(key: SeparatorKey, inList = false): string {
    switch (key) {
        case 'guess':
            return inList ? $localize`Separator: guess` : $localize`:guess the separator:Guess`;
        case 'comma':
            return $localize`Comma`;
        case 'semicolon':
            return $localize`Semicolon`;
        case 'tab':
            return $localize`:the tab character:Tab`;
        case 'pipe':
            return $localize`:the character |:Pipe`;
    }
}

/** A separator's hint in the "⋯" menu. */
export function separatorHint(key: SeparatorKey): string {
    switch (key) {
        case 'guess':
            return $localize`Guess the separator from the file.`;
        case 'comma':
            return $localize`Read the cells as separated by commas.`;
        case 'semicolon':
            return $localize`Read the cells as separated by semicolons.`;
        case 'tab':
            return $localize`Read the cells as separated by tabs.`;
        case 'pipe':
            return $localize`Read the cells as separated by pipes (|).`;
    }
}

/** The separator read, in the status bar. */
export function separatedBy(delimiter: string): string {
    switch (delimiter) {
        case ',':
            return $localize`comma separated`;
        case ';':
            return $localize`semicolon separated`;
        case '\t':
            return $localize`tab separated`;
        case '|':
            return $localize`pipe separated`;
        default:
            return $localize`separated by “${delimiter}:separator:”`;
    }
}

/** The status bar's counts: "4 rows × 3 columns · comma separated", "2 of 4 rows × …" while filtered. */
export function tableCounts(
    counts: { shown: number; total: number; columns: number; filtered: boolean },
    delimiter: string,
): string {
    const { shown, total, columns, filtered } = counts;
    return icu(
        $localize`{filtered, select, yes {{shown, number} of {total, plural, one {# row} other {# rows}}} \
other {{total, plural, one {# row} other {# rows}}}} × {columns, plural, one {# column} other {# columns}} \
· {separator}`,
        { filtered: filtered ? 'yes' : 'no', shown, total, columns, separator: separatedBy(delimiter) },
    );
}

/** The hint of a column's name: what a click on it does next (ascending, descending, then the file's order). */
export function sortHint(column: string, current: SortDirection | null): string {
    if (current === null) return $localize`Sort the rows by “${column}:column:”, smallest first.`;
    if (current === 'ascending') return $localize`Sort the rows by “${column}:column:”, largest first.`;
    return $localize`Show the rows in the order of the file.`;
}

/** Where the file is first wrong, and how many other records are. */
export function problemLine(problem: { record: number; message: string }, others: number): string {
    const { record, message: reason } = problem;
    return icu(
        $localize`{others, plural, =0 {Record {record, number}: {reason}.} \
one {Record {record, number}: {reason} (and # more).} other {Record {record, number}: {reason} (and # more).}}`,
        { others, record, reason },
    );
}

/** What papaparse found wrong, by its code (its own English message when it is a new one). */
export function problemReason(code: string, message: string): string {
    switch (code) {
        case 'MissingQuotes':
            return $localize`A quoted cell isn't closed`;
        case 'InvalidQuotes':
            return $localize`A quote is out of place in a quoted cell`;
        case 'TooFewFields':
            return $localize`Fewer cells than the header`;
        case 'TooManyFields':
            return $localize`More cells than the header`;
        default:
            return message;
    }
}
