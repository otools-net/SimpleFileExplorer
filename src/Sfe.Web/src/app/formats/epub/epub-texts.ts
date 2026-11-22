import { EpubMode } from './epub-document';

/** The EPUB reader's texts that depend on its state: the layouts, how far through the book, why it didn't open. */
export const epubTexts = {
    /** The layouts of the bar, each with its name and a hint. */
    modes: (): readonly { id: EpubMode; label: string; title: string }[] => [
        {
            id: 'pages',
            label: $localize`:layout of a book, one page at a time:Page`,
            title: $localize`Show one page at a time, turned with ← and →.`,
        },
        {
            id: 'spread',
            label: $localize`:layout of a book, two pages side by side:Two pages`,
            title: $localize`Show two pages side by side, as an open book (one when the window is narrow).`,
        },
        {
            id: 'scroll',
            label: $localize`:layout of a book, one long page:Scroll`,
            title: $localize`Show the whole book as one long page to scroll.`,
        },
    ],
    /** How far through the book, as a percentage ("42 %"); "…" while its pages are counted. */
    progress: (percentage: number | null | undefined) =>
        percentage === null || percentage === undefined
            ? '…'
            : $localize`:how far through the book:${Math.round(percentage * 100)}:percent: %`,
    progressHint: (counting: boolean) =>
        counting ? $localize`Counting the pages of the book…` : $localize`How far through the book you are.`,
    /** The reason as epub.js gives it, ended by a stop when it has none. */
    couldNotOpen: (reason: string) =>
        $localize`Could not open the book: ${/[.!?…]$/.test(reason) ? reason : `${reason}.`}:reason:`,
};
