/**
 * Reading preferences of the text views (agents/docs/features/reading-preferences.md): one set per profile (code, prose)
 * plus a reader theme and a custom font name shared by both. Pure: stored and read by `ReadingService`.
 */

export type ReadingProfile = 'code' | 'prose';
export type ReaderTheme = 'app' | 'light' | 'sepia' | 'dark';

export interface ProfilePreferences {
    /** An id of `READER_FONTS`. */
    font: string;
    /** CSS pixels. */
    size: number;
    lineHeight: number;
    /** Longest line, in characters (`ch`); 0 = the whole width. */
    width: number;
    /** Room around the text, CSS pixels. */
    padding: number;
    wrap: boolean;
    lineNumbers: boolean;
}

export interface ReadingPreferences {
    theme: ReaderTheme;
    /** A font installed on the device, for the font `custom`. */
    customFont: string;
    code: ProfilePreferences;
    prose: ProfilePreferences;
}

export interface ReaderFont {
    id: string;
    label: string;
    /** The CSS `font-family` (`custom`: the custom name goes first). */
    family: string;
    /** One of the fonts of `reader-fonts.css`, downloaded when picked. */
    bundled: boolean;
}

export const READER_FONTS: readonly ReaderFont[] = [
    { id: 'literata', label: 'Literata (serif, for reading)', family: '"Literata", Georgia, serif', bundled: true },
    { id: 'atkinson', label: 'Atkinson Hyperlegible (sans-serif, very legible)', family: '"Atkinson Hyperlegible", system-ui, sans-serif', bundled: true },
    { id: 'jetbrains-mono', label: 'JetBrains Mono (monospace)', family: '"JetBrains Mono", ui-monospace, monospace', bundled: true },
    { id: 'system-serif', label: 'Serif of this device', family: '"Iowan Old Style", "Palatino Linotype", Palatino, Georgia, "Times New Roman", serif', bundled: false },
    { id: 'system-sans', label: 'Sans-serif of this device', family: 'var(--font)', bundled: false },
    { id: 'system-mono', label: 'Monospace of this device', family: 'var(--mono)', bundled: false },
    { id: 'custom', label: 'Another font of this device…', family: 'var(--mono)', bundled: false },
];

export const FONT_SIZES = { min: 10, max: 32 } as const;
export const LINE_HEIGHTS: readonly number[] = [1.2, 1.35, 1.5, 1.65, 1.8, 2];
/** 0 = the whole width. */
export const WIDTHS: readonly number[] = [0, 60, 72, 80, 100, 120];
export const PADDINGS: readonly number[] = [0, 8, 16, 24, 32, 48, 64];
export const READER_THEMES: readonly { id: ReaderTheme; label: string; title: string }[] = [
    { id: 'app', label: 'App', title: 'As the app: light or dark with its theme' },
    { id: 'light', label: 'Light', title: 'Dark text on white' },
    { id: 'sepia', label: 'Sepia', title: 'Brown text on warm paper' },
    { id: 'dark', label: 'Dark', title: 'Light text on dark grey' },
];

export const DEFAULT_READING: ReadingPreferences = {
    theme: 'app',
    customFont: '',
    code: { font: 'system-mono', size: 14, lineHeight: 1.5, width: 0, padding: 8, wrap: false, lineNumbers: true },
    prose: { font: 'literata', size: 17, lineHeight: 1.65, width: 72, padding: 32, wrap: true, lineNumbers: false },
};

export function fontOf(id: string): ReaderFont {
    return READER_FONTS.find(font => font.id === id) ?? READER_FONTS.find(font => font.id === 'system-mono')!;
}

/** The CSS font family of a profile's font: a custom name quoted first, then the profile's usual font. */
export function fontFamily(profile: ReadingProfile, preferences: ProfilePreferences, customFont: string): string {
    const font = fontOf(preferences.font);
    if (font.id !== 'custom')
        return font.family;
    const usual = fontOf(DEFAULT_READING[profile].font).family;
    const name = customFont.replace(/["\\;{}<>]/g, '').trim();
    return name ? `"${name}", ${usual}` : usual;
}

function pick<T>(value: unknown, allowed: readonly T[], fallback: T): T {
    return allowed.includes(value as T) ? value as T : fallback;
}

function sanitizeProfile(value: unknown, fallback: ProfilePreferences): ProfilePreferences {
    const raw = (value && typeof value === 'object' ? value : {}) as Partial<Record<keyof ProfilePreferences, unknown>>;
    const size = Number(raw.size);
    return {
        font: READER_FONTS.some(font => font.id === raw.font) ? raw.font as string : fallback.font,
        size: Number.isFinite(size) ? Math.min(FONT_SIZES.max, Math.max(FONT_SIZES.min, Math.round(size))) : fallback.size,
        lineHeight: pick(raw.lineHeight, LINE_HEIGHTS, fallback.lineHeight),
        width: pick(raw.width, WIDTHS, fallback.width),
        padding: pick(raw.padding, PADDINGS, fallback.padding),
        wrap: typeof raw.wrap === 'boolean' ? raw.wrap : fallback.wrap,
        lineNumbers: typeof raw.lineNumbers === 'boolean' ? raw.lineNumbers : fallback.lineNumbers,
    };
}

/** Preferences from anything (stored JSON of any version, an account's defaults): what is not valid is the default's. */
export function sanitizeReading(value: unknown, fallback: ReadingPreferences = DEFAULT_READING): ReadingPreferences {
    const raw = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
    return {
        theme: pick(raw['theme'], READER_THEMES.map(theme => theme.id), fallback.theme),
        customFont: typeof raw['customFont'] === 'string' ? raw['customFont'].slice(0, 100) : fallback.customFont,
        code: sanitizeProfile(raw['code'], fallback.code),
        prose: sanitizeProfile(raw['prose'], fallback.prose),
    };
}
