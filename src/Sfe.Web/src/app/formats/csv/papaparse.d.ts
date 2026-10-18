/**
 * The part of papaparse the table uses (agents/docs/features/csv-viewer.md). Not `@types/papaparse`: it references
 * `@types/node`, whose `setTimeout` (returning `Timeout`) then breaks every timer of the app.
 */
declare module 'papaparse' {
    export interface ParseConfig {
        /** '' guesses among `delimitersToGuess`. */
        delimiter?: string;
        delimitersToGuess?: string[];
        skipEmptyLines?: boolean | 'greedy';
    }

    export interface ParseError {
        type: string;
        code: string;
        message: string;
        /** The record (0-based) it is in. */
        row?: number;
    }

    export interface ParseResult<T> {
        data: T[];
        errors: ParseError[];
        meta: { delimiter: string; linebreak: string; aborted: boolean; truncated: boolean };
    }

    export function parse<T>(input: string, config?: ParseConfig): ParseResult<T>;
}
