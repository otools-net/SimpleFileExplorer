import { describe, expect, it } from 'vitest';
import { ApiError } from '../../core/api.service';
import { encodingName, lineEndingName, textFailure } from './text-document';
import { editorLabel, editorPhrases, failureMessage, textEditTexts, textViewTexts } from './text-texts';

describe('text texts', () => {
    it('tells which bytes of a file read in parts are shown', () => {
        expect(textViewTexts.partRange(0, 262_144, 50_000_000)).toBe('0 B – 256 KB of 47.7 MB');
    });

    it('names encodings and line endings, the code itself when it has no name', () => {
        expect(encodingName('utf-8-bom')).toBe('UTF-8 with BOM');
        expect(encodingName('windows-1252')).toBe('Windows-1252');
        expect(encodingName('koi8-r')).toBe('koi8-r');
        expect(lineEndingName('crlf')).toBe('CRLF');
        expect(lineEndingName('mixed')).toBe('Mixed line endings');
    });

    it('says why a file could not be shown, and when downloading it helps', () => {
        expect(failureMessage(413, '')).toBe('This file is too large to be shown as text (more than 10 MB).');
        expect(failureMessage(404, '')).toBe("This file isn't there any more.");
        expect(failureMessage(403, '')).toBe("You don't have the right to open this file.");
        expect(failureMessage(0, 'offline')).toBe('Could not read the file: offline');
        expect(textFailure(new ApiError(415, 'not text', null, 'not-text'))).toEqual({
            message: "This file is not text, so it can't be shown here.",
            offerDownload: true,
        });
    });

    it('says what Save does, and why a copy was not saved', () => {
        expect(textEditTexts.saveHint(false)).toBe('Save your changes over the file (Ctrl+S).');
        expect(textEditTexts.saveHint(true)).toBe('Saving…');
        expect(textEditTexts.copyFailed(null)).toBe('Could not save the copy.');
        expect(textEditTexts.copyFailed('disk full')).toBe('Could not save the copy: disk full');
        expect(textEditTexts.saved(true)).toBe('Saved. The version before is in the trash.');
        expect(textEditTexts.saved(false)).toBe('Saved.');
        expect(textEditTexts.saveFailed(null)).toBe('Could not save the file.');
        expect(textEditTexts.saveFailed('disk full')).toBe('Could not save the file: disk full');
    });

    it("gives CodeMirror's panels their words in sentence case", () => {
        const phrases = editorPhrases();
        expect(phrases['next']).toBe('Next');
        expect(phrases['by word']).toBe('Whole words');
        expect(phrases['regexp']).toBe('Regular expression');
        expect(editorLabel('notes.txt')).toBe('Text of “notes.txt”');
    });
});
