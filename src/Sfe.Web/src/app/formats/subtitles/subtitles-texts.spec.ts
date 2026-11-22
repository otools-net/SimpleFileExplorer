import {
    cueCounts,
    cueListLabel,
    noCues,
    overlapHint,
    playHint,
    problemLine,
    selectedCueLine,
    subtitlesModes,
} from './subtitles-texts';

describe('subtitles texts', () => {
    it('names the ways subtitles are shown, each with a hint sentence', () => {
        const modes = subtitlesModes();
        expect(modes.map((mode) => mode.id)).toEqual(['cues', 'source', 'split']);
        expect(modes.map((mode) => mode.label)).toEqual(['Cues', 'Source', 'Split']);
        for (const mode of modes) expect(mode.title).toMatch(/^Show .+\.$/);
    });

    it('counts the cues, those the filter shows, the length and the overlaps', () => {
        expect(cueCounts(1, null, null, 0)).toBe('1 cue');
        expect(cueCounts(3, null, '8 s', 1)).toBe('3 cues · 8 s · 1 overlapping');
        expect(cueCounts(1200, null, '1 h', 0)).toBe('1,200 cues · 1 h');
        expect(cueCounts(3, 1, '8 s', 0)).toBe('1 of 3 cues · 8 s');
        expect(cueCounts(1, 0, '2 s', 2)).toBe('0 of 1 cue · 2 s · 2 overlapping');
    });

    it('says which cue is selected, when and how long', () => {
        expect(selectedCueLine('3', '00:06.000', '2 s')).toBe('Cue 3 · 00:06.000 · 2 s');
    });

    it('gives the first problem and how many others there are', () => {
        expect(problemLine(3, 'Missing timestamp', 0)).toBe('Line 3: Missing timestamp.');
        expect(problemLine(3, 'Missing timestamp', 1)).toBe('Line 3: Missing timestamp (and 1 more problem).');
        expect(problemLine(1200, 'Bad {cue}', 4)).toBe('Line 1200: Bad {cue} (and 4 more problems).');
    });

    it('writes the list, overlap, empty list and Play texts as sentences', () => {
        expect(cueListLabel('Film.en.srt')).toBe('Cues of “Film.en.srt”');
        expect(overlapHint()).toBe('This cue starts before the one before it has ended.');
        expect(noCues('fire')).toBe('No cue holds “fire”.');
        expect(noCues('')).toBe('This file has no cues.');
        expect(playHint('Film.mkv')).toBe('Play “Film.mkv” from this cue, in a new tab.');
    });
});
