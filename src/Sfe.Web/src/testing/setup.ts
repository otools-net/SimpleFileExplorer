/*
 * What the unit tests' DOM (jsdom) lacks and the app uses: modal dialogs, media queries, scrolling into view.
 * Loaded before every spec (angular.json, test > setupFiles).
 */

if (!HTMLDialogElement.prototype.showModal) {
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
        this.open = true;
    };
    HTMLDialogElement.prototype.show = HTMLDialogElement.prototype.showModal;
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement, returnValue?: string) {
        if (!this.open)
            return;
        this.open = false;
        if (returnValue !== undefined)
            this.returnValue = returnValue;
        this.dispatchEvent(new Event('close'));
    };
}

/** Matches of the media queries, set by tests: `setMediaQuery('(prefers-color-scheme: light)', true)`. */
const mediaQueries = new Map<string, { matches: boolean; listeners: Set<(event: MediaQueryListEvent) => void> }>();

function mediaQuery(query: string) {
    let entry = mediaQueries.get(query);
    if (!entry)
        mediaQueries.set(query, entry = { matches: false, listeners: new Set() });
    return entry;
}

window.matchMedia = (query: string): MediaQueryList => {
    const entry = mediaQuery(query);
    return {
        media: query,
        get matches() { return entry.matches; },
        onchange: null,
        addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => entry.listeners.add(listener),
        removeEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => entry.listeners.delete(listener),
        addListener: (listener: (event: MediaQueryListEvent) => void) => entry.listeners.add(listener),
        removeListener: (listener: (event: MediaQueryListEvent) => void) => entry.listeners.delete(listener),
        dispatchEvent: () => true,
    } as unknown as MediaQueryList;
};

(globalThis as { setMediaQuery?: (query: string, matches: boolean) => void }).setMediaQuery = (query, matches) => {
    const entry = mediaQuery(query);
    entry.matches = matches;
    for (const listener of entry.listeners)
        listener({ matches, media: query } as MediaQueryListEvent);
};

Element.prototype.scrollIntoView ??= function () { /* nothing to scroll in jsdom */ };
window.scrollTo = () => { /* nothing to scroll in jsdom */ };

/** IntersectionObserver that tests drive: `intersect(element)` reports it in view to the observers watching it. */
class TestIntersectionObserver {
    static readonly instances = new Set<TestIntersectionObserver>();
    readonly observed = new Set<Element>();

    constructor(private readonly callback: IntersectionObserverCallback) {
        TestIntersectionObserver.instances.add(this);
    }

    observe(element: Element): void { this.observed.add(element); }
    unobserve(element: Element): void { this.observed.delete(element); }
    disconnect(): void { this.observed.clear(); }
    takeRecords(): IntersectionObserverEntry[] { return []; }

    report(elements: Element[]): void {
        const watched = elements.filter(element => this.observed.has(element));
        if (watched.length)
            this.callback(watched.map(target => ({ target, isIntersecting: true }) as IntersectionObserverEntry), this as unknown as IntersectionObserver);
    }
}

window.IntersectionObserver ??= TestIntersectionObserver as unknown as typeof IntersectionObserver;
(globalThis as { intersect?: (...elements: Element[]) => void }).intersect = (...elements) => {
    for (const observer of TestIntersectionObserver.instances)
        observer.report(elements);
};
