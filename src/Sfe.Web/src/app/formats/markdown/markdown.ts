import { highlightCode, classHighlighter } from '@lezer/highlight';
import DOMPurify from 'dompurify';
import markdownItFactory, { type MarkdownIt } from 'markdown-it';
import { languageForName } from '../text/languages';

/**
 * Markdown of the library, rendered safely (agents/docs/features/markdown-viewer.md). A library file is content of
 * strangers: no HTML of its own (`html: false`), the result sanitised (DOMPurify), and every link and image rewritten:
 * library paths through the app's routes, other sites in a new tab without a referrer, external images held back
 * until asked for (the app's CSP lets images of other sites load: a data server can be one).
 */

/** Where the links and images of a document lead. */
export interface MarkdownLinks {
    /** The file itself (images, files without a viewer). */
    file(path: string): string;
    /** A file in its viewer, on a page of its own. */
    view(path: string): string;
    /** A folder's listing. */
    folder(path: string): string;
    /** The file `path` has a viewer of the app. */
    viewable(path: string): boolean;
    /** False: relative links and pictures lead nowhere (a document inside an archive): text, no picture. */
    relative?: boolean;
}

export interface MarkdownHeading {
    id: string;
    level: number;
    text: string;
}

export interface RenderedMarkdown {
    fragment: DocumentFragment;
    headings: MarkdownHeading[];
    /** Images of other sites, not shown until `showExternalImages`. */
    externalImages: number;
}

/** The prefix of the headings' ids (a document's `#section` links point to `md-section`): no clash with the app's. */
export const HEADING_ID_PREFIX = 'md-';

let parser: MarkdownIt | null = null;

function markdownIt(): MarkdownIt {
    if (!parser) {
        parser = markdownItFactory({ html: false, linkify: true, typographer: false });
        // "notes.md" or "photo.jpg" in a sentence are names of files, not addresses of web sites.
        parser.linkify.set({ fuzzyLink: false, fuzzyEmail: false });
    }
    return parser;
}

/**
 * The library path that `href` (relative to the document at `documentPath`; `/…` from the root) leads to, decoded;
 * null when it leaves the library or isn't a path. Query and fragment are dropped.
 */
export function resolveLibraryPath(documentPath: string, href: string): string | null {
    const target = href.split(/[?#]/)[0];
    if (!target)
        return null;
    let decoded: string;
    try {
        decoded = decodeURIComponent(target);
    } catch {
        return null;
    }
    const parts = decoded.startsWith('/') ? [] : documentPath.split('/').slice(0, -1);
    for (const segment of decoded.split(/[\\/]/)) {
        if (segment === '' || segment === '.')
            continue;
        if (segment === '..') {
            if (!parts.length)
                return null;
            parts.pop();
        } else
            parts.push(segment);
    }
    return parts.join('/');
}

/** GitHub's way of naming a heading's anchor: lower case, letters and digits, spaces as dashes. */
export function slugify(text: string): string {
    return text.trim().toLowerCase().replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-');
}

const SCHEME = /^([a-z][a-z0-9+.-]*):/i;

/** A link or image to another site (http, https, or `//host/…`). */
function isExternal(href: string): boolean {
    return /^(?:https?:)?\/{2}/i.test(href);
}

/** Renders `text`, the content of the document at `documentPath`, into a fragment safe to insert. */
export function renderMarkdown(text: string, documentPath: string, links: MarkdownLinks): RenderedMarkdown {
    const html = markdownIt().render(text);
    const fragment = DOMPurify.sanitize(html, {
        RETURN_DOM_FRAGMENT: true,
        FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'iframe', 'object', 'embed'],
        FORBID_ATTR: ['style', 'id', 'name'],
    });

    const headings: MarkdownHeading[] = [];
    const used = new Map<string, number>();
    for (const heading of fragment.querySelectorAll<HTMLHeadingElement>('h1, h2, h3, h4, h5, h6')) {
        const base = slugify(heading.textContent ?? '') || 'section';
        const count = used.get(base) ?? 0;
        used.set(base, count + 1);
        const id = HEADING_ID_PREFIX + (count ? `${base}-${count}` : base);
        heading.id = id;
        headings.push({ id, level: Number(heading.tagName[1]), text: heading.textContent?.trim() ?? '' });
    }

    for (const anchor of fragment.querySelectorAll<HTMLAnchorElement>('a[href]'))
        rewriteLink(anchor, documentPath, links);

    let externalImages = 0;
    for (const image of fragment.querySelectorAll<HTMLImageElement>('img')) {
        const src = image.getAttribute('src') ?? '';
        image.loading = 'lazy';
        image.decoding = 'async';
        if (isExternal(src)) {
            externalImages++;
            image.replaceWith(blockedImage(image, src));
        } else if (src.startsWith('data:image/'))
            continue;
        else if (SCHEME.test(src) || !src)
            image.removeAttribute('src');
        else {
            const path = links.relative === false ? null : resolveLibraryPath(documentPath, src);
            if (path === null)
                image.removeAttribute('src');
            else
                image.setAttribute('src', links.file(path));
        }
    }
    return { fragment, headings, externalImages };
}

function rewriteLink(anchor: HTMLAnchorElement, documentPath: string, links: MarkdownLinks): void {
    const href = anchor.getAttribute('href') ?? '';
    if (href.startsWith('#')) {
        let target = href.slice(1);
        try {
            target = decodeURIComponent(target);
        } catch {
            // As written.
        }
        anchor.setAttribute('href', `#${HEADING_ID_PREFIX}${slugify(target)}`);
        anchor.dataset['anchor'] = HEADING_ID_PREFIX + slugify(target);
        return;
    }
    const scheme = SCHEME.exec(href)?.[1].toLowerCase();
    if (isExternal(href) || scheme === 'mailto') {
        anchor.target = '_blank';
        anchor.rel = 'noopener noreferrer nofollow';
        anchor.classList.add('external');
        return;
    }
    const path = scheme || links.relative === false ? null : resolveLibraryPath(documentPath, href);
    if (path === null) {
        // javascript:, file:, a path out of the library...: text, not a link.
        anchor.removeAttribute('href');
        return;
    }
    if (/[\\/]$/.test(href.split(/[?#]/)[0]))
        anchor.setAttribute('href', links.folder(path));
    else if (links.viewable(path)) {
        anchor.setAttribute('href', links.view(path));
        anchor.dataset['path'] = path;
    } else {
        anchor.setAttribute('href', links.file(path));
        anchor.target = '_blank';
        anchor.rel = 'noopener';
    }
}

/** What stands for an image of another site until it is asked for: its description and the site. */
function blockedImage(image: HTMLImageElement, src: string): HTMLElement {
    const placeholder = document.createElement('span');
    placeholder.className = 'md-blocked-image';
    placeholder.dataset['src'] = src;
    placeholder.dataset['alt'] = image.alt;
    let host = src;
    try {
        host = new URL(src, 'https://x').host;
    } catch {
        // Shown as written.
    }
    placeholder.textContent = `${image.alt ? image.alt + ' — ' : ''}image from ${host}`;
    placeholder.title = src;
    return placeholder;
}

/** Puts back the images of other sites held back in `root`. */
export function showExternalImages(root: ParentNode): void {
    for (const placeholder of root.querySelectorAll<HTMLElement>('.md-blocked-image')) {
        const image = document.createElement('img');
        image.src = placeholder.dataset['src'] ?? '';
        image.alt = placeholder.dataset['alt'] ?? '';
        image.referrerPolicy = 'no-referrer';
        image.loading = 'lazy';
        placeholder.replaceWith(image);
    }
}

/** Highlights the code blocks of `root` whose language is named (```js), each language loaded on demand. */
export async function highlightFences(root: ParentNode): Promise<void> {
    const blocks = [...root.querySelectorAll<HTMLElement>('pre > code[class*="language-"]')];
    await Promise.all(blocks.map(async code => {
        const name = /language-(\S+)/.exec(code.className)?.[1] ?? '';
        const description = languageForName(name);
        if (!description)
            return;
        let support;
        try {
            support = await description.load();
        } catch {
            return;     // plain, as it is
        }
        const text = code.textContent ?? '';
        const tree = support.language.parser.parse(text);
        const highlighted = document.createDocumentFragment();
        highlightCode(text, tree, classHighlighter, (part, classes) => {
            if (!classes) {
                highlighted.append(part);
                return;
            }
            const span = document.createElement('span');
            span.className = classes;
            span.textContent = part;
            highlighted.append(span);
        }, () => highlighted.append('\n'));
        code.replaceChildren(highlighted);
    }));
}
