/*
 * What the unit tests' DOM (jsdom) lacks and the app uses: modal dialogs, media queries, scrolling into view.
 * Loaded before every spec (angular.json, test > setupFiles). Angular runs Vitest with `isolate: false`: the spec
 * files of a worker share one global scope and this file runs again for each of them, so whatever it installs must
 * be replaced as a whole (never `??=` next to state of this module), and what specs change on globals is put back
 * after each test (which files share a worker changes from run to run).
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
    disconnect(): void {
        this.observed.clear();
        TestIntersectionObserver.instances.delete(this);
    }
    takeRecords(): IntersectionObserverEntry[] { return []; }

    report(elements: Element[]): void {
        const watched = elements.filter(element => this.observed.has(element));
        if (watched.length)
            this.callback(watched.map(target => ({ target, isIntersecting: true }) as IntersectionObserverEntry), this as unknown as IntersectionObserver);
    }
}

// Always replaced: with `??=`, a later spec file kept an earlier run's class while `intersect` below read this run's
// `instances`, so it reached no observer (CoversService, LocationComponent failed depending on the file order).
window.IntersectionObserver = TestIntersectionObserver as unknown as typeof IntersectionObserver;
(globalThis as { intersect?: (...elements: Element[]) => void }).intersect = (...elements) => {
    for (const observer of TestIntersectionObserver.instances)
        observer.report(elements);
};

/**
 * Globals specs replace for a test (`Object.defineProperty(window, 'isSecureContext', …)`, `document.execCommand = …`):
 * their own properties are put back as they were before the test, so a value can't leak into the next spec file.
 */
const patchedGlobals: [object, PropertyKey][] = [
    [window, 'isSecureContext'],
    [window, 'scrollY'],
    [navigator, 'clipboard'],
    [document, 'execCommand'],
];
let savedGlobals: (PropertyDescriptor | undefined)[] = [];
beforeEach(() => {
    // Storage outlives a spec file too: each test starts with none (preferences, search history, theme).
    localStorage.clear();
    sessionStorage.clear();
    savedGlobals = patchedGlobals.map(([target, key]) => Object.getOwnPropertyDescriptor(target, key));
});
afterEach(() => {
    patchedGlobals.forEach(([target, key], index) => {
        const saved = savedGlobals[index];
        if (saved)
            Object.defineProperty(target, key, saved);
        else
            delete (target as Record<PropertyKey, unknown>)[key];
    });
});
