import { InjectionToken } from '@angular/core';
import { pathToken } from '../../core/paths';

/** How a book is laid out: one page at a time, two side by side (when the window is wide enough), or one long scroll. */
export type EpubMode = 'pages' | 'spread' | 'scroll';

/** A heading of the book's table of contents, with its depth (those under it follow it). */
export interface EpubTocItem {
    readonly label: string;
    readonly href: string;
    readonly depth: number;
}

/** Where the reader is: a CFI (the book's own address of a place), the chapter, how far (0–1, once counted). */
export interface EpubPlace {
    readonly cfi: string;
    readonly href: string;
    readonly percentage: number | null;
    readonly atStart: boolean;
    readonly atEnd: boolean;
}

/** The reading preferences as the book's pages take them (values, not the app's CSS variables: pages are frames). */
export interface EpubStyle {
    readonly fontFamily: string;
    /** CSS pixels. */
    readonly fontSize: number;
    readonly lineHeight: number;
    readonly color: string;
    readonly background: string;
    readonly link: string;
    /** The book's own colours give way to these (dark and sepia themes). */
    readonly forceColors: boolean;
    /** `reader-fonts.css` (absolute), when the font is one of the bundled ones. */
    readonly fontsUrl: string | null;
}

export interface EpubEvents {
    relocated(place: EpubPlace): void;
    /** A key pressed inside the book's pages (frames: the view doesn't hear them otherwise). */
    key(event: KeyboardEvent): void;
    /** A horizontal swipe on the pages: 1 forward, -1 back. */
    swipe(direction: 1 | -1): void;
}

export interface EpubHandle {
    readonly title: string | null;
    readonly author: string | null;
    readonly toc: readonly EpubTocItem[];
    next(): void;
    prev(): void;
    display(target: string): void;
    /** Lays the book out again in `mode`, at the same place. */
    setMode(mode: EpubMode): Promise<void>;
    setStyle(style: EpubStyle): void;
    /** Scrolls (scroll mode) by `pixels`, or a screen when `pages` (1 or -1). */
    scrollBy(pixels: number, pages?: boolean): void;
    resize(): void;
    destroy(): void;
}

export interface EpubOptions {
    readonly mode: EpubMode;
    /** The CFI to open at (the place read last), null for the start. */
    readonly start: string | null;
    readonly style: EpubStyle;
}

export type EpubLoader = (stage: HTMLElement, url: string, options: EpubOptions, events: EpubEvents) => Promise<EpubHandle>;

/** How the view opens a book: epub.js (`openEpub`), a fake in the specs (jsdom lays nothing out). */
export const EPUB_LOADER = new InjectionToken<EpubLoader>('EPUB_LOADER', { providedIn: 'root', factory: () => openEpub });

/** The book's path as one URL segment, as the server reads it (`EpubEntries.Token`). */
export const bookToken = pathToken;

/** The address of the book's files, served one by one by the app (`api/files/epub/{token}/`). */
export function bookUrl(path: string): string {
    return new URL(`api/files/epub/${bookToken(path)}/`, document.baseURI).href;
}

const SWIPE_PIXELS = 50;

/** The stylesheet rules the reading preferences give the book's pages. */
function rulesOf(style: EpubStyle): Record<string, Record<string, string>> {
    const rules: Record<string, Record<string, string>> = {
        'html, body': { background: `${style.background} !important` },
        body: {
            color: `${style.color} !important`,
            'font-family': `${style.fontFamily} !important`,
            'font-size': `${style.fontSize}px !important`,
            'line-height': `${style.lineHeight} !important`,
        },
        // The reader's font and spacing in the text, as e-readers do; headings keep their size.
        'p, li, dd, dt, blockquote, td, th, span, div': { 'font-family': 'inherit !important', 'line-height': 'inherit !important' },
        img: { 'max-width': '100% !important', height: 'auto !important' },
    };
    if (style.forceColors) {
        rules['body *'] = { color: 'inherit !important', 'background-color': 'transparent !important', 'border-color': 'currentColor !important' };
        rules['a, a *'] = { color: `${style.link} !important` };
    }
    return rules;
}

/**
 * Opens the book at `url` (a folder: its files are served by the app) with epub.js (`@likecoin/epub-ts`,
 * agents/docs/features/epub-reader.md): chapters in frames sandboxed without scripts, under the app's own policy.
 */
export async function openEpub(stage: HTMLElement, url: string, options: EpubOptions, events: EpubEvents): Promise<EpubHandle> {
    const { default: ePub } = await import('@likecoin/epub-ts');
    const book = ePub(url);
    await book.opened;
    const [metadata, navigation] = await Promise.all([book.loaded.metadata.catch(() => null), book.loaded.navigation.catch(() => null)]);

    let style = options.style;
    let mode = options.mode;
    let located = false;
    type Rendition = ReturnType<typeof book.renderTo>;
    type Location = NonNullable<ReturnType<Rendition['currentLocation']>>;

    const placeOf = (location: Location): EpubPlace => ({
        cfi: location.start.cfi,
        href: location.start.href,
        percentage: located ? book.locations.percentageFromCfi(location.start.cfi) : null,
        atStart: !!location.atStart,
        atEnd: !!location.atEnd,
    });

    const linkFonts = (contents: { document: Document; addStylesheet(src: string, key?: string): Promise<boolean> }) => {
        if (style.fontsUrl && !contents.document.querySelector('link[data-reader-fonts]'))
            void contents.addStylesheet(style.fontsUrl, 'reader-fonts').then(() =>
                contents.document.querySelector(`link[href="${CSS.escape(style.fontsUrl!)}"]`)?.setAttribute('data-reader-fonts', ''));
    };

    const make = (layout: EpubMode): Rendition => {
        const rendition = book.renderTo(stage, {
            width: '100%',
            height: '100%',
            allowScriptedContent: false,
            ...(layout === 'scroll' ? { flow: 'scrolled', manager: 'continuous' }
                : layout === 'spread' ? { flow: 'paginated', spread: 'auto', minSpreadWidth: 800 }
                    : { flow: 'paginated', spread: 'none' }),
        });
        rendition.hooks.content.register(linkFonts);
        rendition.themes.register({ sfe: rulesOf(style) });
        rendition.themes.select('sfe');
        rendition.on('relocated', (location: Location) => events.relocated(placeOf(location)));
        rendition.on('keydown', (event: KeyboardEvent) => events.key(event));
        let touch: { x: number; y: number } | null = null;
        rendition.on('touchstart', (event: TouchEvent) => {
            touch = event.touches.length === 1 ? { x: event.touches[0].screenX, y: event.touches[0].screenY } : null;
        });
        rendition.on('touchend', (event: TouchEvent) => {
            const end = event.changedTouches[0];
            if (touch && end && Math.abs(end.screenX - touch.x) > SWIPE_PIXELS && Math.abs(end.screenX - touch.x) > Math.abs(end.screenY - touch.y))
                events.swipe(end.screenX < touch.x ? 1 : -1);
            touch = null;
        });
        return rendition;
    };

    let rendition = make(mode);
    await rendition.display(options.start ?? undefined).catch(() => rendition.display());
    // How far through the book: counted once every chapter has been read (in the background).
    void book.locations.generate(1600).then(() => {
        located = true;
        const location = rendition.currentLocation();
        if (location?.start)
            events.relocated(placeOf(location));
    }, () => undefined);

    const flatten = (items: readonly { label: string; href: string; subitems?: unknown[] }[], depth: number, into: EpubTocItem[]) => {
        for (const item of items) {
            into.push({ label: item.label.trim(), href: item.href, depth });
            flatten((item.subitems ?? []) as typeof items, depth + 1, into);
        }
        return into;
    };

    return {
        title: metadata?.title?.trim() || null,
        author: metadata?.creator?.trim() || null,
        toc: flatten(navigation?.toc ?? [], 0, []),
        next: () => void rendition.next(),
        prev: () => void rendition.prev(),
        display: target => void rendition.display(target),
        setMode: async layout => {
            if (layout === mode)
                return;
            const place = rendition.currentLocation()?.start?.cfi;
            rendition.destroy();
            mode = layout;
            rendition = make(layout);
            await rendition.display(place);
        },
        setStyle: next => {
            style = next;
            rendition.themes.register({ sfe: rulesOf(style) });
            rendition.themes.select('sfe');
            for (const contents of rendition.getContents())
                linkFonts(contents);
        },
        scrollBy: (pixels, pages = false) => {
            const container = rendition.manager?.container;
            container?.scrollBy({ top: pages ? pixels * container.clientHeight * 0.9 : pixels });
        },
        resize: () => {
            if (stage.clientWidth && stage.clientHeight)
                rendition.resize();
        },
        destroy: () => {
            rendition.destroy();
            book.destroy();
        },
    };
}
