/*
 * Per-browser preferences in localStorage. Storage can be unavailable (private mode, blocked site data): reads fall
 * back to the default and writes are dropped.
 */

export function readPreference(key: string): string | null;
export function readPreference(key: string, fallback: string): string;
export function readPreference(key: string, fallback: string | null = null): string | null {
    try {
        return localStorage.getItem(key) || fallback;
    } catch {
        return fallback;
    }
}

export function writePreference(key: string, value: string): void {
    try {
        localStorage.setItem(key, value);
    } catch {
        // The preference just won't persist.
    }
}
