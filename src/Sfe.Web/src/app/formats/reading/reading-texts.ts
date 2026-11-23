import { icu } from '../../core/i18n/icu';

/** The texts of the "Aa" panel's choices (agents/docs/features/reading-preferences.md). Pure functions. */
export const readingTexts = {
    /** The panel's name, for screen readers. */
    panel: () => $localize`Reading settings`,
    /** A choice of Width: the longest line in characters, 0 for the whole width. */
    width: (characters: number) =>
        characters
            ? icu($localize`{characters, plural, one {# character} other {# characters}}`, { characters })
            : $localize`Whole width`,
    /** A choice of Margins, in pixels; 0 for none. */
    margin: (pixels: number) =>
        pixels ? $localize`:margins in pixels:${pixels}:pixels: px` : $localize`:no margins:None`,
    /** The text's size, in pixels. */
    size: (pixels: number) => $localize`:text size in pixels:${pixels}:pixels: px`,
};
