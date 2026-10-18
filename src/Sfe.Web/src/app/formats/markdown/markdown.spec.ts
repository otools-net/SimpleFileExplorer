import { highlightFences, MarkdownLinks, renderMarkdown, resolveLibraryPath, showExternalImages, slugify } from './markdown';

const links: MarkdownLinks = {
    file: path => `api/files/raw/${path}`,
    view: path => `view?path=${path}`,
    folder: path => `./?path=${path}`,
    viewable: path => /\.(md|txt)$/i.test(path),
};

/** Renders into a detached element, to look at it. */
function render(text: string, path = 'Docs/Guide/README.md'): { root: HTMLElement; externalImages: number; headings: ReturnType<typeof renderMarkdown>['headings'] } {
    const rendered = renderMarkdown(text, path, links);
    const root = document.createElement('div');
    root.append(rendered.fragment);
    return { root, externalImages: rendered.externalImages, headings: rendered.headings };
}

describe('markdown', () => {
    it('renders the usual Markdown: headings, emphasis, lists, tables, code', () => {
        const { root } = render('# Title\n\nSome *words* and **more**.\n\n- one\n- two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```js\nlet x = 1;\n```\n');

        expect(root.querySelector('h1')!.textContent).toBe('Title');
        expect(root.querySelector('em')!.textContent).toBe('words');
        expect(root.querySelectorAll('li').length).toBe(2);
        expect(root.querySelector('table td')!.textContent).toBe('1');
        expect(root.querySelector('pre > code.language-js')!.textContent).toBe('let x = 1;\n');
    });

    it('shows the HTML written in a document as text, never as elements', () => {
        const { root } = render('<script>alert(1)</script>\n\n<img src=x onerror="alert(1)">\n\n<svg onload="alert(1)"></svg> <iframe src="https://evil"></iframe>');

        expect(root.querySelector('script, img, svg, iframe')).toBeNull();
        expect(root.textContent).toContain('<script>alert(1)</script>');
        // As text it is fine ("onerror=" can be read); as an attribute it never is.
        expect([...root.querySelectorAll('*')].flatMap(element => [...element.attributes].map(attribute => attribute.name)).filter(name => name.startsWith('on'))).toEqual([]);
    });

    it('drops links that would run code or leave the library, and opens other sites apart', () => {
        const { root } = render([
            '[a](javascript:alert(1)) [b](JaVaScRiPt:alert(1)) [c](data:text/html,<b>x</b>) [d](vbscript:x) [e](file:///etc/passwd)',
            '[f](../../../../outside.md) [g](https://example/page) [h](mailto:me@example) [i](//example/x)',
        ].join('\n\n'));
        const anchors = [...root.querySelectorAll('a')];
        const href = (text: string) => anchors.find(a => a.textContent === text)?.getAttribute('href') ?? null;

        for (const text of ['a', 'b', 'c', 'd', 'e', 'f'])
            expect(href(text), text).toBeNull();
        const external = anchors.find(a => a.textContent === 'g')!;
        expect(external.getAttribute('href')).toBe('https://example/page');
        expect(external.target).toBe('_blank');
        expect(external.rel).toBe('noopener noreferrer nofollow');
        expect(anchors.find(a => a.textContent === 'i')!.target).toBe('_blank');
        expect(href('h')).toBe('mailto:me@example');
    });

    it('leads relative links to the library: documents in the viewer, other files as files, folders to their listing', () => {
        const { root } = render('[guide](../Other%20guide.md) [notes](./notes.txt#top) [archive](files/data.zip) [up](../) [root](/Read%20me.md)');
        const anchor = (text: string) => [...root.querySelectorAll('a')].find(a => a.textContent === text)!;

        expect(anchor('guide').getAttribute('href')).toBe('view?path=Docs/Other guide.md');
        expect(anchor('guide').dataset['path']).toBe('Docs/Other guide.md');
        expect(anchor('notes').dataset['path']).toBe('Docs/Guide/notes.txt');
        expect(anchor('archive').getAttribute('href')).toBe('api/files/raw/Docs/Guide/files/data.zip');
        expect(anchor('archive').dataset['path']).toBeUndefined();
        expect(anchor('up').getAttribute('href')).toBe('./?path=Docs');
        expect(anchor('root').dataset['path']).toBe('Read me.md');
    });

    it('takes plain names of files as text, not as web addresses', () => {
        const { root } = render('See notes.md, photo.jpg and nas.local; or https://example and www.example.');
        const hrefs = [...root.querySelectorAll('a')].map(a => a.getAttribute('href'));

        expect(hrefs).toEqual(['https://example']);
        expect(root.textContent).toContain('notes.md');
    });

    it('shows the library images through the app and holds back those of other sites', () => {
        const { root, externalImages } = render('![map](img/map.png) ![logo](https://tracker/pixel.png) ![x](javascript:alert(1)) ![y](../../../../../etc.png)');
        const images = [...root.querySelectorAll('img')];

        expect(images.find(image => image.alt === 'map')!.getAttribute('src')).toBe('api/files/raw/Docs/Guide/img/map.png');
        expect(externalImages).toBe(1);
        expect(root.innerHTML).not.toContain('<img src="https://tracker');
        expect(root.querySelector('.md-blocked-image')!.textContent).toBe('logo — image from tracker');
        expect(images.filter(image => image.hasAttribute('src')).length).toBe(1);

        showExternalImages(root);

        const shown = root.querySelector<HTMLImageElement>('img[alt="logo"]')!;
        expect(shown.getAttribute('src')).toBe('https://tracker/pixel.png');
        expect(shown.referrerPolicy).toBe('no-referrer');
        expect(root.querySelector('.md-blocked-image')).toBeNull();
    });

    it('names the headings for the outline and for the links to them', () => {
        const { root, headings } = render('# Getting started\n\n## Install\n\n## Install\n\nSee [the steps](#install).');

        expect(headings).toEqual([
            { id: 'md-getting-started', level: 1, text: 'Getting started' },
            { id: 'md-install', level: 2, text: 'Install' },
            { id: 'md-install-1', level: 2, text: 'Install' },
        ]);
        expect(root.querySelector('a')!.dataset['anchor']).toBe('md-install');
        expect(slugify('Ünïcödé & Co. 2!')).toBe('ünïcödé--co-2');
    });

    it('resolves paths inside the library only', () => {
        expect(resolveLibraryPath('a/b/doc.md', 'c.md')).toBe('a/b/c.md');
        expect(resolveLibraryPath('a/b/doc.md', '../../c.md')).toBe('c.md');
        expect(resolveLibraryPath('a/b/doc.md', '../../../c.md')).toBeNull();
        expect(resolveLibraryPath('a/b/doc.md', '..\\c.md?x=1#y')).toBe('a/c.md');
        expect(resolveLibraryPath('doc.md', '%E0%A4%A')).toBeNull();
    });

    it('highlights the code blocks whose language is named', async () => {
        const { root } = render('```js\nconst answer = 42; // yes\n```\n\n```nolanguage\nplain\n```');

        await highlightFences(root);

        expect(root.querySelector('code.language-js .tok-keyword')!.textContent).toBe('const');
        expect(root.querySelector('code.language-js .tok-comment')!.textContent).toBe('// yes');
        expect(root.querySelector('code.language-js')!.textContent).toBe('const answer = 42; // yes\n');
        expect(root.querySelector('code.language-nolanguage')!.children.length).toBe(0);
    });
});
