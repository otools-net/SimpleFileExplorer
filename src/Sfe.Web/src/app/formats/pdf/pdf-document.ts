import { InjectionToken } from '@angular/core';

/** A heading of the document's outline (bookmarks), with those under it. */
export interface PdfOutlineItem {
    readonly title: string;
    readonly items: readonly PdfOutlineItem[];
    /** Shows it. */
    go(): void;
}

/** Find's answer: the match shown (1-based, 0 for none) of how many. */
export interface PdfMatches {
    readonly current: number;
    readonly total: number;
}

export interface PdfEvents {
    /** The page shown changed (1-based). */
    page(page: number): void;
    /** The zoom changed: `scale` (1 = 100 %). */
    scale(scale: number): void;
    matches(matches: PdfMatches): void;
    /** The document is locked: resolves the password typed, or null to give up. `wrong`: the one tried wasn't it. */
    password(wrong: boolean): Promise<string | null>;
}

/** An open document, drawn in its container as the person scrolls (pdf.js' viewer: text to select, links). */
export interface PdfHandle {
    readonly pages: number;
    readonly title: string | null;
    readonly outline: readonly PdfOutlineItem[];
    goTo(page: number): void;
    /** `page-width`, `page-fit`, `auto`, or a number (1 = 100 %). */
    setScale(scale: string | number): void;
    find(query: string, options: { previous?: boolean; again?: boolean }): void;
    /** The text shown again at its size (the container's size changed). */
    refresh(): void;
    destroy(): void;
}

export type PdfLoader = (container: HTMLDivElement, url: string, events: PdfEvents, start: number | null) => Promise<PdfHandle>;

/** How the view opens a document: pdf.js (`openPdf`), a fake in the specs (jsdom has no canvas). */
export const PDF_LOADER = new InjectionToken<PdfLoader>('PDF_LOADER', { providedIn: 'root', factory: () => openPdf });

/** Thrown when the file isn't a PDF pdf.js can read. */
export class NotPdfError extends Error { }

/** The password was asked for and not given. */
export class PdfLockedError extends Error { }

/**
 * Opens `url` with pdf.js (agents/docs/features/pdf-viewer.md): its worker, character maps and standard fonts served
 * by the app (`pdfjs/`, angular.json assets), no WebAssembly (the app's CSP doesn't allow it; pdf.js 6 has no eval),
 * no scripts of the document, links of other sites in a new tab without referrer.
 */
export async function openPdf(container: HTMLDivElement, url: string, events: PdfEvents, start: number | null): Promise<PdfHandle> {
    const pdfjs = await import('pdfjs-dist');
    // The viewer's components read the library from the global (pdfjs-dist/web/pdf_viewer.mjs).
    (globalThis as unknown as { pdfjsLib: unknown }).pdfjsLib = pdfjs;
    pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs/pdf.worker.min.mjs', document.baseURI).href;
    const viewer = await import('pdfjs-dist/web/pdf_viewer.mjs');

    const eventBus = new viewer.EventBus();
    const linkService = new viewer.PDFLinkService({ eventBus, externalLinkTarget: viewer.LinkTarget.BLANK, externalLinkRel: 'noopener noreferrer nofollow' });
    const findController = new viewer.PDFFindController({ eventBus, linkService });
    const pages = new viewer.PDFViewer({
        container,
        eventBus,
        linkService,
        findController,
        annotationMode: pdfjs.AnnotationMode.ENABLE,
        removePageBorders: false,
    });
    linkService.setViewer(pages);

    const task = pdfjs.getDocument({
        url,
        cMapUrl: new URL('pdfjs/cmaps/', document.baseURI).href,
        cMapPacked: true,
        standardFontDataUrl: new URL('pdfjs/standard_fonts/', document.baseURI).href,
        useWasm: false,
        enableXfa: false,
        withCredentials: true,
    });
    let wrong = false;
    task.onPassword = (update: (password: string) => void, reason: number) => {
        wrong = reason === pdfjs.PasswordResponses.INCORRECT_PASSWORD;
        void events.password(wrong).then(password => {
            if (password === null)
                void task.destroy();
            else
                update(password);
        });
    };
    let document_;
    try {
        document_ = await task.promise;
    } catch (error) {
        const name = (error as Error)?.name;
        if (name === 'InvalidPDFException')
            throw new NotPdfError((error as Error).message);
        if (name === 'PasswordException' || task.destroyed)
            throw new PdfLockedError('locked');
        throw error;
    }

    eventBus.on('pagesinit', () => {
        pages.currentScaleValue = 'auto';
        if (start && start > 1 && start <= document_.numPages)
            pages.currentPageNumber = start;
    });
    eventBus.on('pagechanging', (event: { pageNumber: number }) => events.page(event.pageNumber));
    eventBus.on('scalechanging', (event: { scale: number }) => events.scale(event.scale));
    eventBus.on('updatefindmatchescount', (event: { matchesCount: PdfMatches }) => events.matches(event.matchesCount));
    eventBus.on('updatefindcontrolstate', (event: { matchesCount: PdfMatches }) => events.matches(event.matchesCount));
    pages.setDocument(document_);
    linkService.setDocument(document_, null);

    const [metadata, outline] = await Promise.all([
        document_.getMetadata().catch(() => null),
        document_.getOutline().catch(() => null),
    ]);
    const title = (metadata?.info as { Title?: unknown } | undefined)?.Title;
    type RawOutline = { title: string; dest: unknown; items: RawOutline[] };
    const outlineOf = (items: RawOutline[] | null): PdfOutlineItem[] => (items ?? []).map(item => ({
        title: item.title,
        items: outlineOf(item.items),
        go: () => {
            if (item.dest)
                void linkService.goToDestination(item.dest as string);
        },
    }));

    return {
        pages: document_.numPages,
        title: typeof title === 'string' && title.trim() ? title.trim() : null,
        outline: outlineOf(outline as RawOutline[] | null),
        goTo: page => {
            pages.currentPageNumber = Math.min(Math.max(1, Math.round(page)), document_.numPages);
        },
        setScale: scale => {
            pages.currentScaleValue = String(scale);
        },
        find: (query, { previous = false, again = false }) => {
            eventBus.dispatch('find', {
                source: null, type: again ? 'again' : '', query, caseSensitive: false, entireWord: false, highlightAll: true,
                findPrevious: previous, matchDiacritics: false,
            });
        },
        refresh: () => pages.update(),
        destroy: () => {
            pages.setDocument(null as never);
            linkService.setDocument(null, null);
            void task.destroy();
        },
    };
}
