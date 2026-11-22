import {
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    effect,
    ElementRef,
    inject,
    input,
    signal,
    untracked,
    viewChild,
} from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { formatSize, joinParts } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { MenuEntry } from '../../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../../menus/overflow-row';
import { IconComponent } from '../../shared/icon.component';
import { FILE_SOURCE } from '../file-source';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { readPosition, writePosition } from '../text/positions';
import { NotPdfError, PDF_LOADER, PdfHandle, PdfLockedError, PdfMatches, PdfOutlineItem } from './pdf-document';
import { pdfTexts, SCALE_VALUES } from './pdf-texts';

/** Where each PDF was last read, in this browser: `{ path: page }` (as the text files' lines). */
export const PDF_PAGES_KEY = 'sfe.pdf.pages';
export const PDF_DARK_KEY = 'sfe.pdf.dark';
export const PDF_OUTLINE_KEY = 'sfe.pdf.outline';
const SAVE_PAGE_MS = 500;
const SCALES: readonly { value: string; label: string }[] = SCALE_VALUES.map((value) => ({
    value,
    label: pdfTexts.scale(value),
}));
const STEPS = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];

/**
 * pdf.js' own CSS (`pdf-viewer.css`, a style bundle not in the page: 168 kB), linked the first time a PDF is shown;
 * the version keeps an old copy from being used after an update (the bundle's name isn't hashed).
 */
function linkViewerStyles(version: string): void {
    if (document.head.querySelector('link[data-pdf-viewer]')) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = `pdf-viewer.css?v=${encodeURIComponent(version)}`;
    link.dataset['pdfViewer'] = '';
    document.head.appendChild(link);
}

/**
 * A PDF (agents/docs/features/pdf-viewer.md): its pages drawn as they are scrolled to (pdf.js, bundled), text to
 * select and find, links, the outline; zoom, the page read last time, pages darkened for reading at night. Read only.
 */
@Component({
    selector: 'sfe-pdf-view',
    imports: [IconComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view pdf-view',
        'data-owns-keys': '',
        '[class.dark-pages]': 'dark()',
        '(keydown)': 'onKeydown($event)',
        '(window:keydown)': 'onWindowKeydown($event)',
    },
    templateUrl: './pdf-view.component.html',
})
export class PdfViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);
    /** Opened ready to be changed: PDFs are only read. */
    readonly edit = input(false);

    private readonly config = inject(ConfigService);
    private readonly fileSource = inject(FILE_SOURCE);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    private readonly load = inject(PDF_LOADER);
    private readonly container = viewChild.required<ElementRef<HTMLDivElement>>('container');
    private readonly frame = viewChild.required<ElementRef<HTMLDivElement>>('frame');
    private readonly findBox = viewChild<ElementRef<HTMLInputElement>>('findBox');
    private readonly passwordBox = viewChild<ElementRef<HTMLInputElement>>('passwordBox');

    protected readonly scales = SCALES;
    /** The toolbar's buttons in its "⋯" menu, when they don't fit (pages, zoom and find stay). */
    /** On a phone: the find field shown, under the bar. */
    protected readonly findOpen = signal(false);
    protected readonly more = {
        zoom: (): MenuEntry => {
            const current = this.scaleValue();
            const choices = [...SCALES, ...(this.customScale() ? [this.customScale()!] : [])];
            return {
                id: 'zoom',
                label: pdfTexts.zoom(choices.find((choice) => choice.value === current)?.label ?? current),
                icon: 'zoom-in',
                title: pdfTexts.zoomHint(),
                disabled: this.state() !== 'ready',
                submenu: () =>
                    choices.map((choice) => ({
                        id: 'zoom-' + choice.value,
                        label: choice.label,
                        icon: 'zoom-in',
                        title: pdfTexts.scaleHint(choice.value),
                        checked: choice.value === current,
                        run: () => this.setScale(choice.value),
                    })),
            };
        },
        outline: (): MenuEntry => ({
            id: 'outline',
            label: pdfTexts.outline(),
            icon: 'list',
            title: pdfTexts.outlineHint(),
            checked: this.showOutline(),
            run: () => this.toggleOutline(),
        }),
        dark: (): MenuEntry => ({
            id: 'dark',
            label: pdfTexts.dark(),
            icon: 'theme',
            title: pdfTexts.darkHint(),
            checked: this.dark(),
            run: () => this.toggleDark(),
        }),
    };
    protected readonly state = signal<'loading' | 'locked' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly wrongPassword = signal(false);
    protected readonly page = signal(1);
    protected readonly pages = signal(0);
    protected readonly title = signal<string | null>(null);
    protected readonly outline = signal<readonly PdfOutlineItem[]>([]);
    protected readonly showOutline = signal(readPreference(PDF_OUTLINE_KEY) !== '0');
    /** The zoom chosen (`auto`, `page-width`, `page-fit`, or a number), and the scale it gives now. */
    protected readonly scaleValue = signal('auto');
    private readonly scale = signal(1);
    protected readonly query = signal('');
    protected readonly matches = signal<PdfMatches>({ current: 0, total: 0 });
    protected readonly matchesText = computed(() => pdfTexts.matches(this.matches().current, this.matches().total));
    protected readonly dark = signal(readPreference(PDF_DARK_KEY) === '1');
    private handle: PdfHandle | null = null;
    private answer: ((password: string | null) => void) | null = null;
    private savePage: ReturnType<typeof setTimeout> | undefined;

    /** The outline as a list, each heading with its depth (those under it follow it). */
    protected readonly outlineRows = computed(() => {
        const rows: { item: PdfOutlineItem; depth: number }[] = [];
        const visit = (items: readonly PdfOutlineItem[], depth: number) => {
            for (const item of items) {
                rows.push({ item, depth });
                visit(item.items, depth + 1);
            }
        };
        visit(this.outline(), 0);
        return rows;
    });
    /** A zoom not in the list (Ctrl+= from 300 %, a number typed): shown as its percentage. */
    protected readonly customScale = computed(() => {
        const value = this.scaleValue();
        return SCALES.some((choice) => choice.value === value)
            ? null
            : { value, label: pdfTexts.scale(value) };
    });
    protected readonly downloadUrl = computed(() =>
        this.fileHost.can(Rights.download) ? this.fileSource.download(this.item()) : null,
    );
    protected readonly details = computed(() => {
        const size = this.item().size;
        return joinParts(
            [
                'PDF',
                pdfTexts.pageCount(this.pages()),
                size ? formatSize(size) : null,
                this.title(),
            ],
            '  ·  ',
        );
    });

    constructor() {
        linkViewerStyles(this.config.config()?.version ?? '');
        let destroyed = false;
        inject(DestroyRef).onDestroy(() => {
            destroyed = true;
            clearTimeout(this.savePage);
            this.answer?.(null);
            this.handle?.destroy();
            resize?.disconnect();
        });
        let resize: ResizeObserver | null = null;
        effect(() => {
            const item = this.item();
            untracked(() => void this.open(item, () => destroyed));
        });
        // The container's size changed (the outline, the window): a zoom that fits fits again.
        effect(() => {
            const frame = this.frame().nativeElement;
            untracked(() => {
                if (typeof ResizeObserver === 'undefined' || resize) return;
                resize = new ResizeObserver(() => {
                    if (this.handle && Number.isNaN(Number(this.scaleValue()))) this.handle.setScale(this.scaleValue());
                });
                resize.observe(frame);
            });
        });
    }

    private async open(item: LibraryItem, destroyed: () => boolean): Promise<void> {
        if (!this.config.urls() || this.handle) return;
        try {
            const handle = await this.load(
                this.container().nativeElement,
                this.fileSource.original(item),
                {
                    page: (page) => {
                        this.page.set(page);
                        clearTimeout(this.savePage);
                        this.savePage = setTimeout(() => writePosition(item.path, page, PDF_PAGES_KEY), SAVE_PAGE_MS);
                    },
                    scale: (scale) => this.scale.set(scale),
                    matches: (matches) => this.matches.set(matches),
                    password: (wrong) =>
                        new Promise<string | null>((resolve) => {
                            this.wrongPassword.set(wrong);
                            this.answer = resolve;
                            this.state.set('locked');
                            setTimeout(() => this.passwordBox()?.nativeElement.focus());
                        }),
                },
                readPosition(item.path, PDF_PAGES_KEY),
            );
            if (destroyed()) {
                handle.destroy();
                return;
            }
            this.handle = handle;
            this.pages.set(handle.pages);
            this.title.set(handle.title);
            this.outline.set(handle.outline);
            this.state.set('ready');
        } catch (error) {
            if (destroyed()) return;
            this.message.set(
                error instanceof NotPdfError
                    ? pdfTexts.notPdf()
                    : error instanceof PdfLockedError
                      ? pdfTexts.locked()
                      : pdfTexts.couldNotShow(String((error as Error)?.message ?? error)),
            );
            this.state.set('failed');
        }
    }

    protected unlock(event: Event): void {
        event.preventDefault();
        const answer = this.answer;
        this.answer = null;
        this.state.set('loading');
        answer?.(this.passwordBox()?.nativeElement.value ?? '');
    }

    protected goTo(page: number | string): void {
        const number = Number(page);
        if (this.handle && Number.isFinite(number)) this.handle.goTo(number);
    }

    protected setScale(value: string): void {
        this.scaleValue.set(value);
        this.handle?.setScale(value);
    }

    /** One step larger or smaller than the scale shown now (whatever chose it). */
    protected zoom(direction: 1 | -1): void {
        const current = this.scale();
        const next =
            direction > 0
                ? (STEPS.find((step) => step > current + 0.01) ?? STEPS.at(-1)!)
                : ([...STEPS].reverse().find((step) => step < current - 0.01) ?? STEPS[0]);
        this.setScale(String(next));
    }

    protected toggleOutline(): void {
        this.showOutline.update((shown) => !shown);
        writePreference(PDF_OUTLINE_KEY, this.showOutline() ? '1' : '0');
    }

    protected toggleDark(): void {
        this.dark.update((dark) => !dark);
        writePreference(PDF_DARK_KEY, this.dark() ? '1' : '0');
    }

    protected goToOutline(event: Event, entry: PdfOutlineItem): void {
        event.preventDefault();
        entry.go();
    }

    protected find(query: string): void {
        this.query.set(query);
        this.handle?.find(query, {});
    }

    protected onFindKeydown(event: KeyboardEvent): void {
        if (event.key === 'Enter') {
            event.preventDefault();
            this.handle?.find(this.query(), { again: true, previous: event.shiftKey });
        } else if (event.key === 'Escape' && this.query()) {
            event.preventDefault();
            event.stopPropagation();
            (event.target as HTMLInputElement).value = '';
            this.find('');
        } else if (event.key === 'Escape' && this.findOpen()) {
            event.preventDefault();
            event.stopPropagation();
            this.findOpen.set(false);
        }
    }

    /** The magnifier of a phone: shows the find field under the bar (focused), or hides it. */
    protected toggleFind(): void {
        this.findOpen.set(!this.findOpen());
        if (this.findOpen()) this.focusFind();
    }

    /** Once the field shows (a phone's is hidden until the magnifier opens it). */
    private focusFind(): void {
        setTimeout(() => {
            this.findBox()?.nativeElement.focus();
            this.findBox()?.nativeElement.select();
        });
    }

    /** ← / → turn the pages (not while typing); Ctrl+= / Ctrl+− / Ctrl+0 zoom, rather than the whole page's. */
    protected onKeydown(event: KeyboardEvent): void {
        if (event.defaultPrevented || this.state() !== 'ready') return;
        const typing = (event.target as Element).closest?.('input, select, textarea');
        if ((event.ctrlKey || event.metaKey) && !event.altKey) {
            const step =
                event.key === '=' || event.key === '+'
                    ? 1
                    : event.key === '-' || event.key === '_'
                      ? -1
                      : event.key === '0'
                        ? 0
                        : null;
            if (step === null) return;
            event.preventDefault();
            if (step === 0) this.setScale('auto');
            else this.zoom(step as 1 | -1);
        } else if (
            !typing &&
            !event.altKey &&
            !event.shiftKey &&
            (event.key === 'ArrowLeft' || event.key === 'ArrowRight')
        ) {
            event.preventDefault();
            this.goTo(this.page() + (event.key === 'ArrowRight' ? 1 : -1));
        }
    }

    /** Ctrl+F finds in the document, rather than the browser in the page around it. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (
            event.defaultPrevented ||
            this.state() !== 'ready' ||
            !(event.ctrlKey || event.metaKey) ||
            event.shiftKey ||
            event.altKey ||
            event.key.toLowerCase() !== 'f'
        )
            return;
        event.preventDefault();
        this.findOpen.set(true);
        this.focusFind();
    }
}
