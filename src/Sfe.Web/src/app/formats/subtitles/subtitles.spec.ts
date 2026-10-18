import { formatSpan, formatTime, parseShift, parseSubtitles, shiftEdits, subtitleTypeOf } from './subtitles';

const SRT = [
    '1',
    '00:00:01,000 --> 00:00:03,500',
    'Where are we <i>going</i>?',
    '',
    '2',
    '00:00:03,000 --> 00:00:05,000',
    '<b>Up</b> the ridge,',
    'then down.',
    '',
    '3',
    '01:02:03,450 --> 01:02:05,000',
    '<font color="#ff0">Late</font>',
    '',
].join('\r\n');

const VTT = [
    'WEBVTT',
    '',
    'intro',
    '00:01.000 --> 00:02.000 align:start',
    '<v Ann>Hello</v>',
    '',
    '00:02.500 --> 00:04.000',
    'Bye',
    '',
].join('\n');

function apply(text: string, edits: { offset: number; length: number; content: string }[]): string {
    let result = text;
    for (const edit of [...edits].reverse())
        result = result.slice(0, edit.offset) + edit.content + result.slice(edit.offset + edit.length);
    return result;
}

describe('subtitles', () => {
    it('reads SubRip cues with their times, marks and timing lines', async () => {
        const document = await parseSubtitles(SRT, 'srt');

        expect(document.cues.map(cue => [cue.start, cue.end, cue.line])).toEqual([[1, 3.5, 2], [3, 5, 6], [3723.45, 3725, 11]]);
        expect(document.cues[0].runs).toEqual([{ text: 'Where are we ' }, { text: 'going', italic: true }, { text: '?' }]);
        expect(document.cues[1].plain).toBe('Up the ridge, then down.');
        expect(document.cues[1].runs[0]).toEqual({ text: 'Up', bold: true });
        expect(document.cues[2].plain).toBe('Late');
        expect(document.cues.map(cue => cue.overlaps)).toEqual([false, true, false]);
    });

    it('reads WebVTT cues, with ids and voices', async () => {
        const document = await parseSubtitles(VTT, 'vtt');

        expect(document.cues.map(cue => [cue.id, cue.start, cue.end, cue.line])).toEqual([['intro', 1, 2, 4], ['', 2.5, 4, 7]]);
        expect(document.cues[0].runs).toEqual([{ text: 'Ann: ', voice: 'Ann' }, { text: 'Hello' }]);
        expect(document.problems).toEqual([]);
    });

    it('tells the lines where it isn\'t valid', async () => {
        const document = await parseSubtitles('WEBVTT\n\n00:01.000 --> nonsense\nText\n\n00:02.000 --> 00:03.000\nFine\n', 'vtt');

        expect(document.problems.length).toBeGreaterThan(0);
        expect(document.problems[0].line).toBe(3);
        expect(document.cues.map(cue => [cue.plain, cue.line])).toEqual([['Fine', 6]]);
    });

    it('shifts every time as it is written, never before zero', () => {
        expect(apply(SRT, shiftEdits(SRT, 1500)).split('\r\n').filter(line => line.includes('-->'))).toEqual([
            '00:00:02,500 --> 00:00:05,000', '00:00:04,500 --> 00:00:06,500', '01:02:04,950 --> 01:02:06,500',
        ]);
        expect(apply(VTT, shiftEdits(VTT, -1500)).split('\n').filter(line => line.includes('-->'))).toEqual([
            '00:00.000 --> 00:00.500 align:start', '00:01.000 --> 00:02.500',
        ]);
        expect(apply(VTT, shiftEdits(VTT, 3_600_000)).split('\n')[3]).toBe('01:00:01.000 --> 01:00:02.000 align:start');
        expect(shiftEdits('No times here\n', 1000)).toEqual([]);
    });

    it('reads shifts as people type them, and writes times', () => {
        expect(parseShift('-2.5')).toBe(-2500);
        expect(parseShift('+1,2 s')).toBe(1200);
        expect(parseShift('0.75')).toBe(750);
        expect(parseShift('soon')).toBeNull();
        expect(formatTime(3723.45)).toBe('1:02:03.450');
        expect(formatTime(61.5)).toBe('01:01.500');
        expect(formatSpan(2.54)).toBe('2.5 s');
        expect(formatSpan(5400)).toBe('1 h 30 min');
        expect(subtitleTypeOf('Film.en.SRT')).toBe('srt');
        expect(subtitleTypeOf('Film.vtt')).toBe('vtt');
    });
});
