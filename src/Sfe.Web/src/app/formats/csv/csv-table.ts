import { parse } from 'papaparse';

/** The separators offered (`auto`: papaparse guesses among them). */
export type CsvDelimiter = 'auto' | ',' | ';' | '\t' | '|';
export const DELIMITERS: readonly { id: CsvDelimiter; label: string }[] = [
    { id: 'auto', label: 'Guess' },
    { id: ',', label: 'Comma' },
    { id: ';', label: 'Semicolon' },
    { id: '\t', label: 'Tab' },
    { id: '|', label: 'Pipe' },
];
/** How many rows decide the columns' widths and whether they hold numbers. */
const SAMPLE_ROWS = 500;
const MIN_WIDTH = 3;
const MAX_WIDTH = 40;
/** A cell shows at most this many characters (the whole is copied and in the status bar). */
export const CELL_LENGTH = 500;

export interface CsvProblem {
    readonly message: string;
    /** The record it is in (1-based, the header included). */
    readonly record: number;
}

export interface CsvTable {
    /** Every record, the header row included, as written (cells not trimmed). */
    readonly rows: readonly (readonly string[])[];
    readonly delimiter: string;
    readonly problems: readonly CsvProblem[];
}

export interface CsvColumn {
    readonly name: string;
    /** Its width, in characters (`ch`). */
    readonly width: number;
    /** Most of its cells are numbers: right-aligned, sorted as numbers. */
    readonly numeric: boolean;
}

export type SortDirection = 'ascending' | 'descending';

/** The extension says which separator: tab for `.tsv` / `.tab`, pipe for `.psv`, otherwise guessed. */
export function delimiterFor(extension: string): CsvDelimiter {
    return extension === 'tsv' || extension === 'tab' ? '\t' : extension === 'psv' ? '|' : 'auto';
}

/** Parses `text` (papaparse: quoted fields, separators and line breaks inside quotes, any line ending). */
export function parseCsv(text: string, delimiter: CsvDelimiter = 'auto'): CsvTable {
    const result = parse<string[]>(text, {
        delimiter: delimiter === 'auto' ? '' : delimiter,
        delimitersToGuess: [',', ';', '\t', '|'],
        skipEmptyLines: true,
    });
    const problems = result.errors
        // One column (or nothing to guess from): read with commas, nothing is wrong.
        .filter(error => error.code !== 'UndetectableDelimiter')
        .map(error => ({ message: error.message, record: (error.row ?? 0) + 1 }));
    return { rows: result.data, delimiter: result.meta.delimiter, problems };
}

/** A spreadsheet's name of the column at `index`: A … Z, AA, AB… */
export function columnLetters(index: number): string {
    let name = '';
    for (let rest = index + 1; rest > 0; rest = Math.floor((rest - 1) / 26))
        name = String.fromCharCode(65 + ((rest - 1) % 26)) + name;
    return name;
}

const NUMBER = /^[+-]?(\d{1,3}([ ,.' ]\d{3})*|\d+)([.,]\d+)?%?$/;

/** The cell reads as a number (`1,234.5`, `-3`, `12%`, `1 234,5`). */
export function isNumber(cell: string): boolean {
    return NUMBER.test(cell.trim());
}

/** A cell's value as a number, for sorting (`1,234.5` → 1234.5, `1.234,5` → 1234.5); NaN when it isn't one. */
export function numberOf(cell: string): number {
    if (!isNumber(cell))
        return NaN;
    let text = cell.trim().replace(/%$/, '').replace(/[ ' ]/g, '');
    const comma = text.lastIndexOf(',');
    const dot = text.lastIndexOf('.');
    if (comma >= 0 && dot >= 0)
        // Both: the last one is the decimal mark.
        text = comma > dot ? text.replaceAll('.', '').replace(',', '.') : text.replaceAll(',', '');
    else if (comma >= 0)
        // Commas alone: thousands when every group after one has three digits (1,234,567), else the decimal mark.
        text = /^[+-]?\d{1,3}(,\d{3})+$/.test(text) ? text.replaceAll(',', '') : text.replace(',', '.');
    else if (/^[+-]?\d{1,3}(\.\d{3}){2,}$/.test(text))
        // Several dots: thousands (1.234.567).
        text = text.replaceAll('.', '');
    return Number(text);
}

/**
 * The columns: names from the header row (empty or repeated names get the column's letters), widths from the longest
 * cells of the first rows, numeric when nine in ten of their non-empty cells are numbers.
 */
export function columnsOf(rows: readonly (readonly string[])[], header: boolean): CsvColumn[] {
    let count = 0;
    for (const row of rows)
        count = Math.max(count, row.length);
    const names = header ? rows[0] ?? [] : [];
    const records = rows.slice(header ? 1 : 0, (header ? 1 : 0) + SAMPLE_ROWS);
    const seen = new Set<string>();
    const columns: CsvColumn[] = [];
    for (let index = 0; index < count; index++) {
        let name = (names[index] ?? '').trim();
        if (!name || seen.has(name))
            name = columnLetters(index);
        seen.add(name);
        let longest = name.length + 2;
        let filled = 0;
        let numbers = 0;
        for (const record of records) {
            const cell = record[index] ?? '';
            longest = Math.max(longest, cell.length);
            if (cell.trim()) {
                filled++;
                if (isNumber(cell))
                    numbers++;
            }
        }
        columns.push({ name, width: Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, longest)) + 1, numeric: filled > 0 && numbers >= filled * 0.9 });
    }
    return columns;
}

/** The records (positions in `rows`) holding `query` in any cell, any case; all of them without one. */
export function filterRecords(rows: readonly (readonly string[])[], first: number, query: string): number[] {
    const needle = query.trim().toLowerCase();
    const found: number[] = [];
    for (let index = first; index < rows.length; index++) {
        if (!needle || rows[index].some(cell => cell.toLowerCase().includes(needle)))
            found.push(index);
    }
    return found;
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

/**
 * `records` sorted by `column` (numbers as numbers in a numeric column, text as people sort it, empty cells last
 * either way); equal ones keep the file's order.
 */
export function sortRecords(records: readonly number[], rows: readonly (readonly string[])[], column: number, numeric: boolean, direction: SortDirection): number[] {
    const sign = direction === 'ascending' ? 1 : -1;
    const keys = new Map<number, number>();
    if (numeric)
        for (const index of records)
            keys.set(index, numberOf(rows[index][column] ?? ''));
    return [...records].sort((a, b) => {
        const left = rows[a][column] ?? '';
        const right = rows[b][column] ?? '';
        if (!left.trim() || !right.trim())
            return !left.trim() && !right.trim() ? a - b : !left.trim() ? 1 : -1;
        let order = 0;
        if (numeric) {
            const x = keys.get(a)!;
            const y = keys.get(b)!;
            order = Number.isNaN(x) || Number.isNaN(y) ? collator.compare(left, right) : x - y;
        } else
            order = collator.compare(left, right);
        return order === 0 ? a - b : order * sign;
    });
}

/** A cell as shown: at most `CELL_LENGTH` characters, line breaks inside it as ↵. */
export function shownCell(cell: string | undefined): string {
    if (!cell)
        return '';
    const short = cell.length > CELL_LENGTH ? `${cell.slice(0, CELL_LENGTH)}…` : cell;
    return short.replace(/\r?\n/g, ' ↵ ');
}

/** The separator in words (the status bar). */
export function delimiterName(delimiter: string): string {
    return DELIMITERS.find(choice => choice.id === delimiter)?.label.toLowerCase() ?? JSON.stringify(delimiter);
}
