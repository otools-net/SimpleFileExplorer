import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { formatSize, joinParts } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { fontOf } from '../reading/reading';
import { ReadingPanelComponent } from '../reading/reading-panel.component';
import { ReadingService } from '../reading/reading.service';
import { bookUrl, EPUB_LOADER, EpubHandle, EpubMode, EpubPlace, EpubStyle, EpubTocItem } from './epub-document';

export const EPUB_MODE_KEY = 'sfe.epub.mode';
export const EPUB_TOC_KEY = 'sfe.epub.toc';
/** Where each book was last read, in this browser: `{ path: CFI }`, the most recent last. */
export const EPUB_PLACES_KEY = 'sfe.epub.places';
export const MAX_PLACES = 100;
const SAVE_PLACE_MS = 500;
const LINE_PIXELS = 48;
const MODES: readonly { id: EpubMode; label: string; title: string }[] = [
    { id: 'pages', label: 'Page', title: 'One page at a time, turned with ← →' },
    { id: 'spread', label: 'Two pages', title: 'Two pages side by side, as an open book (one when the window is narrow)' },
    { id: 'scroll', label: 'Scroll', title: 'The whole book as one long page, scrolled' },
];

function readPlaces(): Record<string, string> {
    try {
        const value = JSON.parse(readPreference(EPUB_PLACES_KEY) ?? '{}') as unknown;
        return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, string> : {};
    } catch {
        return {};
    }
}

/** The place `path` was last read at here (null: its start). */
export function readPlace(path: string): string | null {
    const place = readPlaces()[path];
    return typeof place === 'string' && place.startsWith('epubcfi(') ? place : null;
}

export function writePlace(path: string, cfi: string): void {
    const places = readPlaces();
    delete places[path];
    places[path] = cfi;
    const paths = Object.keys(places);
    for (const old of paths.slice(0, Math.max(0, paths.length - MAX_PLACES)))
        delete places[old];
    writePreference(EPUB_PLACES_KEY, JSON.stringify(places));
}

/**
 * An EPUB book (agents/docs/features/epub-reader.md): read page by page, two pages side by side, or scrolled as one
 * long page (epub.js); its table of contents, how far through it, the place read last; the prose reading
 * preferences (font, size, spacing, colours) on its pages. Its files come from the app (`api/files/epub`); nothing of
 * the book runs.
 */
@Component({
    selector: 'sfe-epub-view',
    imports: [IconComponent, ReadingPanelComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view epub-view prose',
        'data-owns-keys': '',
        '[attr.data-reader-theme]': 'reading.preferences().theme',
        '[attr.data-mode]': 'mode()',
        '[style]': 'style()',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Book">
            <button type="button" class="icon-button" data-toc [class.active]="showToc()" [attr.aria-pressed]="showToc()" title="Contents" aria-label="Contents"
                    [disabled]="!toc().length" (click)="toggleToc()"><svg sfeIcon="list"></svg></button>
            <button type="button" class="icon-button" data-previous title="Previous page (←)" aria-label="Previous page" [disabled]="state() !== 'ready' || place()?.atStart"
                    (click)="previous()"><svg sfeIcon="left"></svg></button>
            <button type="button" class="icon-button" data-next title="Next page (→)" aria-label="Next page" [disabled]="state() !== 'ready' || place()?.atEnd"
                    (click)="next()"><svg sfeIcon="right"></svg></button>
            <div class="markdown-modes" role="radiogroup" aria-label="Layout">
                @for (choice of modes; track choice.id) {
                    <button type="button" role="radio" class="markdown-mode" [class.active]="mode() === choice.id" [attr.aria-checked]="mode() === choice.id"
                            [attr.data-mode]="choice.id" [title]="choice.title" (click)="setMode(choice.id)">{{ choice.label }}</button>
                }
            </div>
            <button type="button" class="icon-button reading-toggle" data-reading-toggle [class.active]="panelOpen()" [attr.aria-expanded]="panelOpen()"
                    title="Reading settings: font, size, spacing, colours" aria-label="Reading settings" (click)="panelOpen.set(!panelOpen())">Aa</button>
        </div>
        @if (panelOpen()) {
            <sfe-reading-panel profile="prose" (closed)="panelOpen.set(false)" />
        }
        <!-- Always there: the grid's second row (the text views' editing bar), so that the pages take the third. -->
        <div class="pdf-messages">
            @switch (state()) {
                @case ('loading') {
                    <p class="text-message">Loading…</p>
                }
                @case ('failed') {
                    <div class="text-message">
                        <p>{{ message() }}</p>
                        @if (downloadUrl(); as url) {
                            <p><a class="button" [href]="url"><svg sfeIcon="download"></svg> Download</a></p>
                        }
                    </div>
                }
            }
        </div>
        <div class="pdf-panes epub-panes" [class.with-outline]="showToc() && toc().length" [hidden]="state() === 'failed'">
            @if (showToc() && toc().length) {
                <nav class="markdown-outline epub-toc" aria-label="Contents">
                    @for (entry of toc(); track $index) {
                        <a href="" [class.current]="entry.href.split('#')[0] === currentHref()" [style.padding-inline-start.px]="8 + entry.depth * 14"
                           (click)="goTo($event, entry)">{{ entry.label }}</a>
                    }
                </nav>
            }
            <div #frame class="pdf-frame epub-frame">
                <div #stage class="epub-stage" [attr.aria-label]="item().name"></div>
            </div>
        </div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                <span class="json-selection">
                    @if (chapter(); as label) {
                        <span class="json-path" data-chapter>{{ label }}</span>
                    }
                    <span data-progress [title]="place()?.percentage === null ? 'Counting the pages of the book…' : 'How far through the book'">{{ progress() }}</span>
                </span>
            </footer>
        }
    `,
})
export class EpubViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);
    /** Opened ready to be changed: books are only read. */
    readonly edit = input(false);

    private readonly config = inject(ConfigService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    protected readonly reading = inject(ReadingService);
    private readonly load = inject(EPUB_LOADER);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly stage = viewChild.required<ElementRef<HTMLDivElement>>('stage');
    private readonly frame = viewChild.required<ElementRef<HTMLDivElement>>('frame');

    protected readonly modes = MODES;
    protected readonly mode = signal<EpubMode>(readMode());
    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly panelOpen = signal(false);
    protected readonly showToc = signal(readPreference(EPUB_TOC_KEY) === '1');
    protected readonly title = signal<string | null>(null);
    protected readonly author = signal<string | null>(null);
    protected readonly toc = signal<readonly EpubTocItem[]>([]);
    protected readonly place = signal<EpubPlace | null>(null);
    private handle: EpubHandle | null = null;
    private savePlace: ReturnType<typeof setTimeout> | undefined;

    protected readonly style = computed(() => this.reading.style('prose'));
    protected readonly currentHref = computed(() => this.place()?.href.split('#')[0] ?? null);
    /** The chapter read: the last heading of the contents in the file shown. */
    protected readonly chapter = computed(() => {
        const href = this.currentHref();
        return href ? this.toc().filter(entry => entry.href.split('#')[0] === href)[0]?.label ?? null : null;
    });
    protected readonly progress = computed(() => {
        const percentage = this.place()?.percentage;
        return percentage === null || percentage === undefined ? '…' : `${Math.round(percentage * 100)} %`;
    });
    protected readonly downloadUrl = computed(() => this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const size = this.item().size;
        return joinParts(['EPUB', this.title(), this.author(), size ? formatSize(size) : null], '  ·  ');
    });

    constructor() {
        let destroyed = false;
        let resize: ResizeObserver | null = null;
        inject(DestroyRef).onDestroy(() => {
            destroyed = true;
            clearTimeout(this.savePlace);
            this.handle?.destroy();
            resize?.disconnect();
        });
        effect(() => {
            const item = this.item();
            untracked(() => void this.open(item, () => destroyed));
        });
        // The reading preferences on the book's pages (they are frames: values, not the app's variables).
        effect(() => {
            this.style();
            this.reading.preferences();
            this.reading.linkFonts('prose');
            untracked(() => this.handle?.setStyle(this.bookStyle()));
        });
        effect(() => {
            const frame = this.frame().nativeElement;
            untracked(() => {
                if (typeof ResizeObserver === 'undefined' || resize)
                    return;
                resize = new ResizeObserver(() => this.handle?.resize());
                resize.observe(frame);
            });
        });
    }

    private async open(item: LibraryItem, destroyed: () => boolean): Promise<void> {
        if (this.handle)
            return;
        try {
            const handle = await this.load(this.stage().nativeElement, bookUrl(item.path), { mode: this.mode(), start: readPlace(item.path), style: this.bookStyle() }, {
                relocated: place => {
                    this.place.set(place);
                    clearTimeout(this.savePlace);
                    this.savePlace = setTimeout(() => writePlace(item.path, place.cfi), SAVE_PLACE_MS);
                },
                key: event => this.onKeydown(event),
                swipe: direction => direction > 0 ? this.next() : this.previous(),
            });
            if (destroyed()) {
                handle.destroy();
                return;
            }
            this.handle = handle;
            // The colours are the host's computed ones: read them again now that it is laid out.
            handle.setStyle(this.bookStyle());
            this.title.set(handle.title);
            this.author.set(handle.author);
            this.toc.set(handle.toc);
            this.state.set('ready');
        } catch (error) {
            if (destroyed())
                return;
            this.message.set(`This book could not be opened: ${(error as Error)?.message ?? error}. It can only be downloaded.`);
            this.state.set('failed');
        }
    }

    /** The preferences as values: the host's variables as the browser resolves them. */
    private bookStyle(): EpubStyle {
        const preferences = this.reading.preferences();
        const computed = getComputedStyle(this.host.nativeElement);
        const variable = (name: string, fallback: string) => computed.getPropertyValue(name).trim() || fallback;
        // The view's own colours are the reader theme's (`.text-view`), as rgb().
        const background = computed.backgroundColor || 'rgb(255, 255, 255)';
        const font = fontOf(preferences.prose.font);
        // From the service, not the host: the host's binding may not be applied yet when the book opens.
        const family = (this.style()['--reader-font'] ?? font.family)
            .split(/var\((--[\w-]+)\)/).map((part, index) => index % 2 ? variable(part, 'serif') : part).join('');
        return {
            fontFamily: family,
            fontSize: preferences.prose.size,
            lineHeight: preferences.prose.lineHeight,
            color: computed.color || 'rgb(31, 35, 40)',
            background,
            link: variable('--tok-link', '#0969da'),
            forceColors: preferences.theme === 'sepia' || isDark(background),
            fontsUrl: font.bundled ? new URL(`reader-fonts.css?v=${encodeURIComponent(this.config.config()?.version ?? '')}`, document.baseURI).href : null,
        };
    }

    protected next(): void {
        this.handle?.next();
    }

    protected previous(): void {
        this.handle?.prev();
    }

    protected async setMode(mode: EpubMode): Promise<void> {
        this.mode.set(mode);
        writePreference(EPUB_MODE_KEY, mode);
        await this.handle?.setMode(mode);
    }

    protected toggleToc(): void {
        this.showToc.update(shown => !shown);
        writePreference(EPUB_TOC_KEY, this.showToc() ? '1' : '0');
    }

    protected goTo(event: Event, entry: EpubTocItem): void {
        event.preventDefault();
        this.handle?.display(entry.href);
    }

    /**
     * Turning the pages: ← → Page Up / Down, Space (Shift+Space back); in the scroll layout, ↑ ↓ and the pages scroll.
     * Also the keys pressed inside the book's pages. Ctrl+= / Ctrl+− / Ctrl+0: the text's size.
     */
    protected onKeydown(event: KeyboardEvent): void {
        if (event.defaultPrevented || this.state() !== 'ready' || event.altKey)
            return;
        if (event.ctrlKey || event.metaKey) {
            const step = event.key === '=' || event.key === '+' ? 1 : event.key === '-' || event.key === '_' ? -1 : event.key === '0' ? 0 : null;
            if (step !== null) {
                event.preventDefault();
                this.reading.changeSize('prose', step);
            }
            return;
        }
        if ((event.target as Element | null)?.closest?.('input, select, textarea, .reading-panel'))
            return;
        const scroll = this.mode() === 'scroll';
        const forward = event.key === 'ArrowRight' || event.key === 'PageDown' || (event.key === ' ' && !event.shiftKey);
        const back = event.key === 'ArrowLeft' || event.key === 'PageUp' || (event.key === ' ' && event.shiftKey);
        if (scroll && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
            event.preventDefault();
            this.handle?.scrollBy(event.key === 'ArrowDown' ? LINE_PIXELS : -LINE_PIXELS);
        } else if (scroll && (event.key === 'PageDown' || event.key === 'PageUp' || event.key === ' ')) {
            event.preventDefault();
            this.handle?.scrollBy(forward ? 1 : -1, true);
        } else if (forward || back) {
            event.preventDefault();
            if (forward)
                this.next();
            else
                this.previous();
        }
    }
}

/** A background dark enough for light text (its colour as the browser gives it: `rgb(r, g, b)`). */
function isDark(color: string): boolean {
    const match = /rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)/.exec(color);
    if (!match)
        return false;
    const [r, g, b] = match.slice(1, 4).map(Number);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;
}

function readMode(): EpubMode {
    const stored = readPreference(EPUB_MODE_KEY);
    return stored === 'spread' || stored === 'scroll' ? stored : 'pages';
}
