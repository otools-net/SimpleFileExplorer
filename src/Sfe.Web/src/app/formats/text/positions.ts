import { readPreference, writePreference } from '../../core/preferences';

/** Where each text file was last read, in this browser: `{ path: first line shown }`, the most recent last. */
export const POSITIONS_KEY = 'sfe.text.positions';

/** Files remembered; the oldest are forgotten beyond. */
export const MAX_POSITIONS = 100;

function read(): Record<string, number> {
    try {
        const value = JSON.parse(readPreference(POSITIONS_KEY) ?? '{}') as unknown;
        return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, number> : {};
    } catch {
        return {};
    }
}

/** The first line shown when `path` was last read here (null: its start). */
export function readPosition(path: string): number | null {
    const line = read()[path];
    return typeof line === 'number' && Number.isInteger(line) && line > 1 ? line : null;
}

/** Remembers `line` as where `path` is read (its start: forgotten). */
export function writePosition(path: string, line: number): void {
    const positions = read();
    delete positions[path];
    if (line > 1)
        positions[path] = line;
    const paths = Object.keys(positions);
    for (const old of paths.slice(0, Math.max(0, paths.length - MAX_POSITIONS)))
        delete positions[old];
    writePreference(POSITIONS_KEY, JSON.stringify(positions));
}
