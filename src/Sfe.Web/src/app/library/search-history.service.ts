import { computed, inject, Injectable, signal } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { TextPart } from '../core/format';
import { readPreference, writePreference } from '../core/preferences';

const KEY = 'sfe.searchHistory';

/**
 * `entry` cut around the first place matching what is typed (case-insensitive, as `SearchHistory.suggest` matches),
 * marked so that the suggestion shows the typed characters in bold.
 */
export function matchedParts(entry: string, text: string): TextPart[] {
    const typed = text.trim().toLocaleLowerCase();
    const lower = entry.toLocaleLowerCase();
    // Lower-casing can change the length of a few letters (e.g. İ): the positions would then be wrong, mark nothing.
    const start = typed && lower.length === entry.length ? lower.indexOf(typed) : -1;
    if (start < 0)
        return [{ text: entry, mark: false }];
    const end = start + typed.length;
    return [
        { text: entry.slice(0, start), mark: false },
        { text: entry.slice(start, end), mark: true },
        { text: entry.slice(end), mark: false },
    ].filter(part => part.text);
}

function load(): string[] {
    try {
        const stored: unknown = JSON.parse(readPreference(KEY, '[]'));
        return Array.isArray(stored) ? stored.filter((entry): entry is string => typeof entry === 'string' && entry.length > 0) : [];
    } catch {
        return [];
    }
}

/**
 * The last searches of this browser, newest first, that the search box suggests again. The server only says how many
 * to keep (`config.searchHistory`, `Search:HistorySize`): each browser keeps its own, also for visitors without an account.
 * 0 turns it off: nothing is suggested or recorded, but what was kept stays until the size is set again or cleared.
 */
@Injectable({ providedIn: 'root' })
export class SearchHistory {
    private readonly config = inject(ConfigService);
    private readonly stored = signal(load());

    readonly size = computed(() => this.config.config()?.searchHistory ?? 0);
    readonly entries = computed(() => this.stored().slice(0, this.size()));

    /** Puts `query` first (moving it up when already kept) and forgets the oldest past the size. */
    record(query: string): void {
        const q = query.trim();
        const size = this.size();
        if (!q || !size)
            return;
        this.save([q, ...this.stored().filter(entry => entry !== q)].slice(0, size));
    }

    remove(query: string): void {
        this.save(this.stored().filter(entry => entry !== query));
    }

    clear(): void {
        this.save([]);
    }

    /**
     * The kept searches matching what is typed, like a web search box: all of them for an empty box, otherwise
     * those starting with the text, then those containing it (case-insensitive); the text itself is left out.
     */
    suggest(text: string): string[] {
        const typed = text.trim().toLocaleLowerCase();
        const entries = this.entries();
        if (!typed)
            return entries;
        const lower = entries.map(entry => entry.toLocaleLowerCase());
        const starting = entries.filter((_, i) => lower[i] !== typed && lower[i].startsWith(typed));
        const containing = entries.filter((_, i) => !lower[i].startsWith(typed) && lower[i].includes(typed));
        return [...starting, ...containing];
    }

    private save(entries: string[]): void {
        this.stored.set(entries);
        writePreference(KEY, JSON.stringify(entries));
    }
}
