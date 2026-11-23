import { formatSize } from '../../core/format';

/**
 * The text view's texts (agents/docs/features/text-viewer.md): the names and hints of its toolbar, shared by its
 * buttons and its "⋯" entries, and the sentences of its status bar. Pure functions.
 */
export const textViewTexts = {
    plainText: () => $localize`:a text file with no language highlighted:Plain text`,
    parts: () => $localize`Parts of the file`,
    partsHint: () => $localize`Show another part of the file. It is too large to be read whole.`,
    start: () => $localize`:the first part of a large file:Start`,
    startHint: () => $localize`Show the start of the file.`,
    earlier: () => $localize`:the part of a large file before the one shown:Earlier`,
    earlierHint: () => $localize`Show the part before this one.`,
    later: () => $localize`:the part of a large file after the one shown:Later`,
    laterHint: () => $localize`Show the part after this one.`,
    end: () => $localize`:the last part of a large file:End`,
    endHint: () => $localize`Show the end of the file.`,
    follow: () => $localize`:keep showing the end of a file as it grows:Follow`,
    followHint: () =>
        $localize`Show the lines added to the end of the file as they are written, as for a log. Click again to stop.`,
    wrap: () => $localize`Wrap long lines`,
    wrapHint: () => $localize`Continue long lines on the next line instead of letting them go off the side.`,
    findHint: () => $localize`Find words in the text (Ctrl+F).`,
    goToLine: () => $localize`Go to line`,
    goToLineHint: () => $localize`Go to a line by its number (Ctrl+Alt+G).`,
    /** The bytes shown of a file read in parts ("1.2 MB – 1.5 MB of 480 MB"). */
    partRange: (from: number, to: number, size: number) =>
        $localize`:bytes shown of a file read in parts:${formatSize(from)}:from: – ${formatSize(to)}:to: of \
${formatSize(size)}:size:`,
};

/** The editing buttons' hints (agents/docs/features/text-editor.md). */
export const textEditTexts = {
    saveHint: (saving: boolean) => (saving ? $localize`Saving…` : $localize`Save your changes over the file (Ctrl+S).`),
    /** The toast after a save over the file; `trashed`: the version before went to the trash. */
    saved: (trashed: boolean) =>
        trashed ? $localize`Saved. The version before is in the trash.` : $localize`:the file was saved:Saved.`,
    /** Why the file wasn't saved: the server's reason when it gave one. */
    saveFailed: (reason: string | null) =>
        reason ? $localize`Could not save the file: ${reason}:reason:` : $localize`Could not save the file.`,
    /** Why a copy wasn't saved: the server's reason when it gave one. */
    copyFailed: (reason: string | null) =>
        reason ? $localize`Could not save the copy: ${reason}:reason:` : $localize`Could not save the copy.`,
};

/** Names of encodings and line endings that are words, not codes ("UTF-8", "CRLF" are the same in every language). */
export function encodingNames(): Record<string, string> {
    return {
        'utf-8': 'UTF-8',
        'utf-8-bom': $localize`:a text encoding:UTF-8 with BOM`,
        'utf-16le': 'UTF-16 LE',
        'utf-16be': 'UTF-16 BE',
        'windows-1252': 'Windows-1252',
    };
}

export function lineEndingNames(): Record<string, string> {
    return { lf: 'LF', crlf: 'CRLF', cr: 'CR', mixed: $localize`Mixed line endings` };
}

/** Why a text file could not be shown, by the status the server answered (0: no answer). */
export function failureMessage(status: number, reason: string): string {
    switch (status) {
        case 413:
            return $localize`This file is too large to be shown as text (more than 10 MB).`;
        case 415:
            return $localize`This file is not text, so it can't be shown here.`;
        case 404:
            return $localize`This file isn't there any more.`;
        case 403:
            return $localize`You don't have the right to open this file.`;
        default:
            return $localize`Could not read the file: ${reason}:reason:`;
    }
}

/**
 * The words of CodeMirror's own panels (find, go to line) and of what it tells screen readers, by its English phrase
 * (`EditorState.phrases`); `$` is the number CodeMirror puts in.
 */
export function editorPhrases(): Record<string, string> {
    return {
        Find: $localize`:find field of the text:Find`,
        Replace: $localize`:replace field of the text:Replace`,
        next: $localize`:find the next match:Next`,
        previous: $localize`:find the previous match:Previous`,
        all: $localize`:select all the matches:All`,
        'match case': $localize`Match case`,
        regexp: $localize`Regular expression`,
        'by word': $localize`Whole words`,
        replace: $localize`:replace the match:Replace`,
        'replace all': $localize`Replace all`,
        close: $localize`Close`,
        'current match': $localize`Current match`,
        'on line': $localize`:a match found on line 12:on line`,
        'replaced match on line $': $localize`Replaced the match on line $`,
        'replaced $ matches': $localize`Matches replaced: $`,
        'Go to line': $localize`Go to line`,
        go: $localize`:go to the line typed:Go`,
        'Control character': $localize`Control character`,
        'Selection deleted': $localize`Selection deleted`,
    };
}

/** The editor's name for screen readers. */
export function editorLabel(name: string): string {
    return $localize`Text of “${name}:name:”`;
}
