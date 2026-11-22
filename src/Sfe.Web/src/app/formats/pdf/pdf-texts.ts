import { icu } from '../../core/i18n/icu';

/** The zooms of the list, in their order: fitted ones by name, the others as a scale. */
export const SCALE_VALUES: readonly string[] = [
    'auto',
    'page-width',
    'page-fit',
    '0.5',
    '0.75',
    '1',
    '1.25',
    '1.5',
    '2',
    '3',
];

/** A scale as a percentage ("150 %"). */
function percent(value: string): string {
    return $localize`:a zoom as a percentage:${Math.round(Number(value) * 100)}:percent: %`;
}

/**
 * The PDF viewer's texts that depend on its state: the zooms (a name and a hint each), the page count, the matches
 * found, why a document can't be shown. Pure functions.
 */
export const pdfTexts = {
    /** A zoom's name in the list and the menu. */
    scale: (value: string): string => {
        switch (value) {
            case 'auto':
                return $localize`:zoom that suits the window:Automatic`;
            case 'page-width':
                return $localize`:zoom fitting the width of a page:Page width`;
            case 'page-fit':
                return $localize`:zoom showing a whole page:Whole page`;
            default:
                return percent(value);
        }
    },
    /** A zoom's hint in the "⋯" menu. */
    scaleHint: (value: string): string => {
        switch (value) {
            case 'auto':
                return $localize`Show the pages at a size that suits the window.`;
            case 'page-width':
                return $localize`Make the pages as wide as the window.`;
            case 'page-fit':
                return $localize`Show a whole page in the window.`;
            default:
                return $localize`Show the pages at ${percent(value)}:percent: of their size.`;
        }
    },
    zoom: (label: string) => $localize`Zoom: ${label}:zoom:`,
    zoomHint: () => $localize`Choose how large the pages are shown.`,
    outline: () => $localize`Outline`,
    outlineHint: () => $localize`Show or hide the document's headings, to go to one.`,
    dark: () => $localize`Dark pages`,
    darkHint: () => $localize`Darken the pages, for reading at night.`,
    /** For the line at the bottom: "12 pages". */
    pageCount: (pages: number) => icu($localize`{pages, plural, one {# page} other {# pages}}`, { pages }),
    /** The matches of the words found: "3 of 7", or none. */
    matches: (current: number, total: number) =>
        icu($localize`:matches found in the document:{total, plural, =0 {No matches} other {{current, number} of #}}`, {
            current,
            total,
        }),
    notPdf: () => $localize`This file isn't a PDF that can be shown.`,
    locked: () => $localize`This document is protected by a password, and none was given.`,
    /** The reason as pdf.js gives it, ended by a stop when it has none. */
    couldNotShow: (reason: string) =>
        $localize`Could not show the document: ${/[.!?…]$/.test(reason) ? reason : `${reason}.`}:reason:`,
};
