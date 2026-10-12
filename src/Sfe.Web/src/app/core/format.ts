import { ItemKind } from './models';

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });
const timeFormat = new Intl.DateTimeFormat(undefined, { timeStyle: 'short' });
const numberFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });
const units = ['B', 'KB', 'MB', 'GB', 'TB'];

export function formatSize(bytes: number | null | undefined): string {
    if (bytes === null || bytes === undefined)
        return '';
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit++;
    }
    return `${unit === 0 ? value : numberFormat.format(value)} ${units[unit]}`;
}

/** "1920×1080"; empty when either side is unknown. */
export function formatResolution(width: number | null | undefined, height: number | null | undefined): string {
    return width && height ? `${width}×${height}` : '';
}

export function formatDate(value: string | null | undefined): string {
    return value ? dateFormat.format(new Date(value)) : '';
}

export function formatTime(value: string): string {
    return timeFormat.format(new Date(value));
}

/** Media position or length: "1:05", "1:02:03". */
export function formatDuration(seconds: number): string {
    if (!Number.isFinite(seconds) || seconds < 0)
        return '0:00';
    const total = Math.floor(seconds);
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor(total / 60) % 60;
    const secs = String(total % 60).padStart(2, '0');
    return hours ? `${hours}:${String(minutes).padStart(2, '0')}:${secs}` : `${minutes}:${secs}`;
}

/** Time taken: "45s", "3m 05s", "2h 10m". */
export function formatElapsed(seconds: number): string {
    const total = Math.max(0, Math.round(seconds));
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor(total / 60) % 60;
    return hours ? `${hours}h ${minutes}m` : minutes ? `${minutes}m ${String(total % 60).padStart(2, '0')}s` : `${total}s`;
}

export function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
    return `${count.toLocaleString()} ${count === 1 ? singular : pluralForm}`;
}

/** Joins the parts that are set, e.g. `[size, date]` → "3 MB · 1 Jan 2026". */
export function joinParts(parts: (string | null | undefined | false)[], separator = ' · '): string {
    return parts.filter(Boolean).join(separator);
}

export const kindIcon: Record<ItemKind, string> = {
    directory: 'folder',
    image: 'image',
    video: 'video',
    audio: 'audio',
    file: 'file',
};

export const MEDIA_KINDS: ReadonlySet<ItemKind> = new Set<ItemKind>(['image', 'video', 'audio']);

/**
 * Regex matching the plain terms of a search query: "," (OR) and " " (AND) separate terms,
 * except inside double quotes. Exclusions ("-x"), properties ("s:<1mb", "e:mp4", "t:tag") and the "*" marker are
 * not highlighted.
 */
export function highlightPattern(query: string | null | undefined): RegExp | null {
    const terms = ((query || '').replaceAll('*', '').match(/(?:[^\s,"]+|"[^"]*"?)+/g) || [])
        .filter(term => !term.startsWith('-') && !term.includes(':'))
        .map(term => term.replaceAll('"', ''))
        .filter(Boolean)
        .map(term => term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
    return terms.length ? new RegExp(`(${terms.join('|')})`, 'gi') : null;
}

export interface TextPart {
    text: string;
    mark: boolean;
}

/** `text` cut into the parts matching `pattern` (marked) and the others. */
export function highlightParts(text: string, pattern: RegExp | null): TextPart[] {
    if (!pattern)
        return [{ text, mark: false }];
    // split() with a capture group keeps the matches at the odd indexes
    return text.split(pattern)
        .map((part, index) => ({ text: part, mark: index % 2 === 1 }))
        .filter(part => part.text);
}
