import { ApiError } from '../../core/api.service';
import { formatSize, joinParts } from '../../core/format';
import { TextFile } from '../format.models';
import { encodingNames, failureMessage, lineEndingNames, textViewTexts } from './text-texts';

/** The name shown for an encoding ("UTF-8 with BOM"), the code itself when it has none. */
export function encodingName(encoding: string): string {
    return encodingNames()[encoding] ?? encoding;
}

/** The name shown for line endings ("CRLF", "Mixed line endings"). */
export function lineEndingName(ending: string): string {
    return lineEndingNames()[ending] ?? ending;
}

/** The status bar's left part: the language (or format), encoding, line endings and size of a text file. */
export function textDetails(file: TextFile, language: string | null): string {
    return joinParts(
        [
            language ?? textViewTexts.plainText(),
            encodingName(file.encoding),
            lineEndingName(file.lineEnding),
            formatSize(file.size),
        ],
        '  ·  ',
    );
}

/** Why a text file could not be shown (`api/files/text`), and whether downloading it is the way out. */
export function textFailure(error: unknown): { message: string; offerDownload: boolean } {
    const status = error instanceof ApiError ? error.status : 0;
    return {
        offerDownload: status === 413 || status === 415 || status === 500,
        message: failureMessage(status, error instanceof Error ? error.message : String(error)),
    };
}
