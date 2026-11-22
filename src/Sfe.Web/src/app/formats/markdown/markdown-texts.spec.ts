import { blockedImageText, externalImagesLine, markdownModes, outlineTexts, readingHint } from './markdown-texts';

describe('markdown texts', () => {
    it('names the ways a document is shown, each with a hint sentence', () => {
        const modes = markdownModes();
        expect(modes.map((mode) => mode.id)).toEqual(['rendered', 'source', 'split']);
        expect(modes.map((mode) => mode.label)).toEqual(['Read', 'Source', 'Split']);
        for (const mode of modes) expect(mode.title).toMatch(/^Show .+\.$/);
    });

    it('gives the outline and the reading settings hints as sentences', () => {
        expect(outlineTexts.label()).toBe('Outline');
        expect(outlineTexts.hint()).toBe("Show or hide the list of the document's headings, to go to one.");
        expect(readingHint()).toBe('Change the font, size, width and colours of the text.');
    });

    it('counts the images of other sites held back', () => {
        expect(externalImagesLine(1)).toBe("1 image from other sites isn't shown.");
        expect(externalImagesLine(3)).toBe("3 images from other sites aren't shown.");
        expect(externalImagesLine(1200)).toBe("1,200 images from other sites aren't shown.");
    });

    it('stands for an image held back by its description and its site', () => {
        expect(blockedImageText('logo', 'example.org')).toBe('logo — image from example.org');
        expect(blockedImageText('', 'example.org')).toBe('Image from example.org');
    });
});
