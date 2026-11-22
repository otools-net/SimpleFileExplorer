import { icu } from '../../core/i18n/icu';

/** How a Markdown file is shown: the document, its source, or both side by side. */
export type MarkdownMode = 'rendered' | 'source' | 'split';

/** The Markdown view's choices of what to show: a short name (the buttons, the "⋯" menu) and a hint. */
export function markdownModes(): readonly { id: MarkdownMode; label: string; title: string }[] {
    return [
        {
            id: 'rendered',
            label: $localize`:shows a Markdown document rendered:Read`,
            title: $localize`Show the document as it is meant to be read.`,
        },
        {
            id: 'source',
            label: $localize`:shows a document's text as written:Source`,
            title: $localize`Show the document's text as written.`,
        },
        {
            id: 'split',
            label: $localize`:shows a document and its text side by side:Split`,
            title: $localize`Show the document and its text side by side.`,
        },
    ];
}

/** The outline's button and its "⋯" entry. */
export const outlineTexts = {
    label: () => $localize`Outline`,
    hint: () => $localize`Show or hide the list of the document's headings, to go to one.`,
};

/** The reading settings' hint: prose has a width too. */
export function readingHint(): string {
    return $localize`Change the font, size, width and colours of the text.`;
}

/** Under the document: the images of other sites held back until asked for. */
export function externalImagesLine(count: number): string {
    return icu(
        $localize`{count, plural, one {# image from other sites isn't shown.} \
other {# images from other sites aren't shown.}}`,
        { count },
    );
}

/** What stands for an image of another site until it is shown: its description, when it has one, and the site. */
export function blockedImageText(alt: string, host: string): string {
    return alt ? $localize`${alt}:description: — image from ${host}:site:` : $localize`Image from ${host}:site:`;
}
