import { ApiError } from '../../core/api.service';
import { formatSize, joinParts } from '../../core/format';
import { TextFile } from '../../core/models';

const ENCODINGS: Readonly<Record<string, string>> = {
    'utf-8': 'UTF-8', 'utf-8-bom': 'UTF-8 with BOM', 'utf-16le': 'UTF-16 LE', 'utf-16be': 'UTF-16 BE', 'windows-1252': 'Windows-1252',
};
const LINE_ENDINGS: Readonly<Record<string, string>> = { lf: 'LF', crlf: 'CRLF', cr: 'CR', mixed: 'Mixed line endings' };

/** The status bar's left part: the language (or format), encoding, line endings and size of a text file. */
export function textDetails(file: TextFile, language: string | null): string {
    return joinParts([language ?? 'Plain text', ENCODINGS[file.encoding] ?? file.encoding, LINE_ENDINGS[file.lineEnding] ?? file.lineEnding, formatSize(file.size)], '  ·  ');
}

/** Why a text file could not be shown (`api/files/text`), and whether downloading it is the way out. */
export function textFailure(error: unknown): { message: string; offerDownload: boolean } {
    const status = error instanceof ApiError ? error.status : 0;
    return {
        offerDownload: status === 413 || status === 415 || status === 500,
        message: status === 413 ? 'This file is too large to be shown as text (more than 10 MB).'
            : status === 415 ? 'This file is not text: it can only be downloaded.'
                : status === 404 ? 'This file is not there any more.'
                    : status === 403 ? 'You may not open this file.'
                        : `It could not be read: ${error instanceof Error ? error.message : String(error)}`,
    };
}
