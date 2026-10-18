import { parseText, parseVTTTimestamp, tokenizeVTTCue, VTTCue, VTTNode } from 'media-captions';

export type SubtitleType = 'srt' | 'vtt';

/** A run of a cue's text, as it is shown: italic, bold, underlined, or who says it (`<v Ann>`). */
export interface CueRun {
    readonly text: string;
    readonly italic?: boolean;
    readonly bold?: boolean;
    readonly underline?: boolean;
    readonly voice?: string;
}

export interface SubtitleCue {
    /** Its position among the cues (0-based). */
    readonly index: number;
    /** Its id or number as written (SRT numbers, VTT ids), '' without one. */
    readonly id: string;
    /** Seconds. */
    readonly start: number;
    readonly end: number;
    /** Its text without marks, lines joined by a space (filter, copy). */
    readonly plain: string;
    readonly runs: readonly CueRun[];
    /** The line of its timing (`-->`), 1-based; null when it couldn't be told. */
    readonly line: number | null;
    /** It starts before the cue before it has ended. */
    readonly overlaps: boolean;
}

export interface SubtitleProblem {
    readonly message: string;
    readonly line: number;
}

export interface SubtitleDocument {
    readonly cues: readonly SubtitleCue[];
    readonly problems: readonly SubtitleProblem[];
}

/** `.srt` is SubRip, anything else (`.vtt`) WebVTT. */
export function subtitleTypeOf(name: string): SubtitleType {
    return name.toLowerCase().endsWith('.srt') ? 'srt' : 'vtt';
}

/** A timing line: `00:01:02,500 --> 00:01:04,000` (SRT) or `01:02.500 --> 01:04.000 align:start` (VTT). */
const TIMING = /^\s*((?:\d+:)?\d{1,2}:\d{1,2}[.,]\d{1,3})\s*-->/;

/**
 * Parses `text` (media-captions: the WebVTT parsing rules, SRT read as they are), then finds each cue's timing line,
 * so that the source can be shown at it: the next timing line starting when the cue starts.
 */
export async function parseSubtitles(text: string, type: SubtitleType): Promise<SubtitleDocument> {
    const result = await parseText(text, { type, errors: true });
    const lines = text.split(/\r\n|\r|\n/);
    let next = 0;
    let previousEnd = -Infinity;
    const cues = result.cues.map((cue, index): SubtitleCue => {
        let line: number | null = null;
        for (let at = next; at < lines.length; at++) {
            const timing = TIMING.exec(lines[at]);
            if (timing && Math.abs((parseVTTTimestamp(timing[1].replace(',', '.')) ?? -1) - cue.startTime) < 0.0005) {
                line = at + 1;
                next = at + 1;
                break;
            }
        }
        const overlaps = cue.startTime < previousEnd - 0.0005;
        previousEnd = Math.max(previousEnd, cue.endTime);
        return { index, id: cue.id ?? '', start: cue.startTime, end: cue.endTime, plain: plainOf(cue), runs: runsOf(cue), line, overlaps };
    });
    const problems = result.errors.map(error => ({ message: error.message, line: error.line }));
    return { cues, problems };
}

function plainOf(cue: VTTCue): string {
    return cue.text.replace(/<[^>]*>/g, '').replace(/\s*\n\s*/g, ' ').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&').trim();
}

/** The cue's text as runs (tags not known to WebVTT, such as SRT's `<font>`, keep their text only). */
function runsOf(cue: VTTCue): CueRun[] {
    const runs: CueRun[] = [];
    const visit = (nodes: readonly VTTNode[], style: Omit<CueRun, 'text'>) => {
        for (const node of nodes) {
            if (node.type === 'text')
                runs.push({ text: node.data.replace(/\s*\n\s*/g, ' / '), ...style });
            else if (node.type === 'v') {
                if (node.voice)
                    runs.push({ text: `${node.voice}: `, voice: node.voice });
                visit(node.children, style);
            } else if (node.type !== 'rt' && node.type !== 'timestamp')
                visit(node.children, {
                    ...style,
                    ...(node.type === 'i' && { italic: true }),
                    ...(node.type === 'b' && { bold: true }),
                    ...(node.type === 'u' && { underline: true }),
                });
        }
    };
    try {
        visit(tokenizeVTTCue(cue), {});
    } catch {
        return [{ text: plainOf(cue) }];
    }
    return runs;
}

/** `1:02:03.450` (hours only when there are some, or when `hours`). */
export function formatTime(seconds: number, hours = false): string {
    const total = Math.max(0, Math.round(seconds * 1000));
    const h = Math.floor(total / 3_600_000);
    const m = Math.floor(total / 60_000) % 60;
    const s = Math.floor(total / 1000) % 60;
    const ms = total % 1000;
    const rest = `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}.${String(ms).padStart(3, '0')}`;
    return h || hours ? `${h}:${rest}` : rest;
}

/** A length of time in words: `2.5 s`, `1 min 4 s`, `1 h 32 min`. */
export function formatSpan(seconds: number): string {
    if (seconds < 60)
        return `${Math.round(seconds * 10) / 10} s`;
    if (seconds < 3600)
        return `${Math.floor(seconds / 60)} min ${Math.round(seconds % 60)} s`;
    return `${Math.floor(seconds / 3600)} h ${Math.floor((seconds % 3600) / 60)} min`;
}

/** A timing line's two times and what is between them: `(indent)(start)( --> )(end)`; VTT settings after them stay. */
const TIMING_TIMES = /^(\s*)((?:\d+:)?\d{1,2}:\d{1,2}[.,]\d{1,3})(\s*-->\s*)((?:\d+:)?\d{1,2}:\d{1,2}[.,]\d{1,3})/;
const TIME_PARTS = /^(?:(\d+):)?(\d{1,2}):(\d{1,2})([.,])(\d{1,3})$/;

/**
 * The changes moving every cue by `milliseconds` (earlier when negative, never before 0), written as each time was
 * (`,` or `.`, with or without hours): only the times of the timing lines change, nothing else of the file.
 */
export function shiftEdits(text: string, milliseconds: number): { offset: number; length: number; content: string }[] {
    const edits: { offset: number; length: number; content: string }[] = [];
    let offset = 0;
    for (const line of text.split(/(?<=\n)/)) {
        const timing = TIMING_TIMES.exec(line);
        if (timing) {
            const [, indent, start, arrow, end] = timing;
            edits.push({ offset: offset + indent.length, length: start.length, content: shiftTime(start, milliseconds) });
            edits.push({ offset: offset + indent.length + start.length + arrow.length, length: end.length, content: shiftTime(end, milliseconds) });
        }
        offset += line.length;
    }
    return edits;
}

function shiftTime(time: string, milliseconds: number): string {
    const [, h, m, s, separator, fraction] = TIME_PARTS.exec(time)!;
    const value = ((Number(h ?? 0) * 60 + Number(m)) * 60 + Number(s)) * 1000 + Number(fraction.padEnd(3, '0'));
    return writeTimestamp(Math.max(0, value + milliseconds), h !== undefined, separator, h?.length ?? 2);
}

function writeTimestamp(milliseconds: number, withHours: boolean, separator: string, hourDigits: number): string {
    const h = Math.floor(milliseconds / 3_600_000);
    const m = Math.floor(milliseconds / 60_000) % 60;
    const s = Math.floor(milliseconds / 1000) % 60;
    const ms = milliseconds % 1000;
    const rest = `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}${separator}${String(ms).padStart(3, '0')}`;
    // A VTT time without hours gets them once it passes an hour.
    return withHours || h > 0 ? `${String(h).padStart(Math.max(2, hourDigits), '0')}:${rest}` : rest;
}

/** A shift typed by a person, in seconds (`-2.5`, `+1,2`, `0.75`), as milliseconds; null when it isn't one. */
export function parseShift(value: string): number | null {
    const match = /^\s*([+-]?)\s*(\d+(?:[.,]\d{1,3})?)\s*s?\s*$/.exec(value);
    if (!match)
        return null;
    const milliseconds = Math.round(Number(match[2].replace(',', '.')) * 1000);
    return match[1] === '-' ? -milliseconds : milliseconds;
}
