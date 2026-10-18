import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { LibraryItem, TextFile } from '../../core/models';
import { baseName } from '../../core/paths';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat, formatOf } from '../formats';
import { ReadingPanelComponent } from '../reading/reading-panel.component';
import { ReadingService } from '../reading/reading.service';
import { TextEditor } from '../text/codemirror';
import { textDetails, textFailure } from '../text/text-document';
import { highlightFences, MarkdownHeading, MarkdownLinks, renderMarkdown, showExternalImages } from './markdown';

export type MarkdownMode = 'rendered' | 'source' | 'split';
export const MARKDOWN_MODE_KEY = 'sfe.markdown.mode';
export const MARKDOWN_OUTLINE_KEY = 'sfe.markdown.outline';
const MODES: readonly { id: MarkdownMode; label: string; title: string }[] = [
    { id: 'rendered', label: 'Read', title: 'The document as it is meant to be read' },
    { id: 'source', label: 'Source', title: 'Its text, as written' },
    { id: 'split', label: 'Split', title: 'Both, side by side' },
];

/** A file of the library as listings know it, from its path alone (for the links between documents). */
function itemAt(path: string): LibraryItem {
    const name = baseName(path);
    const dot = name.lastIndexOf('.');
    return { name, path, kind: 'file', extension: dot > 0 ? name.slice(dot + 1).toLowerCase() : null, size: null, modified: null };
}

/**
 * A Markdown file (agents/docs/features/markdown-viewer.md): read (rendered safely, with an outline of its headings),
 * its source (CodeMirror, as the text view), or both side by side; the prose reading preferences. Links to other
 * documents open them in the viewer; images of other sites wait until asked for.
 */
@Component({
    selector: 'sfe-markdown-view',
    imports: [IconComponent, ReadingPanelComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view markdown-view prose',
        'data-owns-keys': '',
        '[attr.data-reader-theme]': 'reading.preferences().theme',
        '[attr.data-mode]': 'mode()',
        '[style]': 'style()',
        '(keydown)': 'onKeydown($event)',
        '(window:keydown)': 'onWindowKeydown($event)',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Document">
            <div class="markdown-modes" role="radiogroup" aria-label="Show">
                @for (choice of modes; track choice.id) {
                    <button type="button" role="radio" class="markdown-mode" [class.active]="mode() === choice.id" [attr.aria-checked]="mode() === choice.id"
                            [attr.data-mode]="choice.id" [title]="choice.title" (click)="setMode(choice.id)">{{ choice.label }}</button>
                }
            </div>
            @if (mode() !== 'source') {
                <button type="button" class="icon-button" [class.active]="outline()" [attr.aria-pressed]="outline()" title="Outline: the headings" aria-label="Outline"
                        [disabled]="!headings().length" (click)="toggleOutline()"><svg sfeIcon="list"></svg></button>
            }
            @if (mode() !== 'rendered') {
                <button type="button" class="icon-button" [class.active]="own().lineNumbers" [attr.aria-pressed]="own().lineNumbers" title="Line numbers" aria-label="Line numbers" (click)="toggleLineNumbers()"><svg sfeIcon="line-numbers"></svg></button>
                <button type="button" class="icon-button" title="Find in the source (Ctrl+F)" aria-label="Find" [disabled]="state() !== 'ready'" (click)="sourceEditor()?.find()"><svg sfeIcon="search"></svg></button>
            }
            <button type="button" class="icon-button reading-toggle" data-reading-toggle [class.active]="panelOpen()" [attr.aria-expanded]="panelOpen()"
                    title="Reading settings: font, size, width, colours" aria-label="Reading settings" (click)="panelOpen.set(!panelOpen())">Aa</button>
        </div>
        @if (panelOpen()) {
            <sfe-reading-panel profile="prose" (closed)="panelOpen.set(false)" />
        }
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
        <div class="markdown-panes" [hidden]="state() !== 'ready'" [class.with-outline]="outline() && mode() !== 'source' && headings().length">
            @if (outline() && mode() !== 'source' && headings().length) {
                <nav class="markdown-outline" aria-label="Outline">
                    @for (heading of headings(); track heading.id) {
                        @if (heading.level <= 3) {
                            <a [href]="'#' + heading.id" [class]="'level-' + heading.level" (click)="goTo($event, heading.id)">{{ heading.text }}</a>
                        }
                    }
                </nav>
            }
            <article #article class="markdown-body" tabindex="0" [attr.aria-label]="item().name" [hidden]="mode() === 'source'" (click)="onArticleClick($event)"><div #content class="markdown-content"></div></article>
            <div #source class="text-editor markdown-source" [hidden]="mode() === 'rendered'"></div>
        </div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                @if (externalImages()) {
                    <span class="markdown-external">{{ externalImages() }} {{ externalImages() === 1 ? 'image' : 'images' }} of other sites not shown
                        <button type="button" class="link-button" title="Load them from the sites they are on (those sites see that this document is read)" (click)="showImages()">Show</button></span>
                }
            </footer>
        }
    `,
})
export class MarkdownViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);

    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    protected readonly reading = inject(ReadingService);
    private readonly article = viewChild.required<ElementRef<HTMLElement>>('article');
    /** Inside the article: the document, at most the reading width (in characters of its body text). */
    private readonly content = viewChild.required<ElementRef<HTMLElement>>('content');
    private readonly source = viewChild.required<ElementRef<HTMLElement>>('source');

    protected readonly modes = MODES;
    protected readonly mode = signal<MarkdownMode>(readMode());
    protected readonly outline = signal(readPreference(MARKDOWN_OUTLINE_KEY) !== '0');
    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly offerDownload = signal(false);
    protected readonly file = signal<TextFile | null>(null);
    protected readonly headings = signal<MarkdownHeading[]>([]);
    protected readonly externalImages = signal(0);
    protected readonly panelOpen = signal(false);
    /** The source's editor, made the first time the source is shown. */
    protected readonly sourceEditor = signal<TextEditor | null>(null);

    protected readonly own = computed(() => this.reading.preferences().prose);
    protected readonly style = computed(() => this.reading.style('prose'));
    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const file = this.file();
        return file ? textDetails(file, 'Markdown') : '';
    });

    private readonly links: MarkdownLinks = {
        file: path => this.config.urls()!.file(path),
        view: path => this.config.urls()!.view(path),
        folder: path => `./?${new URLSearchParams({ path })}`,
        viewable: path => formatOf(itemAt(path)) !== null,
    };

    constructor() {
        const abort = new AbortController();
        inject(DestroyRef).onDestroy(() => {
            abort.abort();
            this.sourceEditor()?.destroy();
        });
        effect(() => {
            const item = this.item();
            untracked(() => void this.load(item, abort.signal));
        });
        // The source's editor, once it is shown, and the prose preferences on it.
        effect(() => {
            const file = this.file();
            const mode = this.mode();
            const { wrap, lineNumbers } = this.own();
            untracked(() => {
                if (!file || mode === 'rendered')
                    return;
                const editor = this.sourceEditor();
                if (editor)
                    editor.configure({ wrap, lineNumbers });
                else {
                    const created = new TextEditor(this.source().nativeElement, { text: file.text, name: 'source.md', wrap, lineNumbers });
                    this.sourceEditor.set(created);
                    void created.setLanguage('source.md').catch(() => undefined);
                }
            });
        });
        effect(() => {
            this.style();
            this.reading.linkFonts('prose');
            untracked(() => this.sourceEditor()?.remeasure());
        });
    }

    private async load(item: LibraryItem, cancel: AbortSignal): Promise<void> {
        if (this.state() !== 'loading' || this.file())
            return;
        let file: TextFile;
        try {
            file = await this.api.textFile(item.path, cancel);
        } catch (error) {
            if (cancel.aborted)
                return;
            const failure = textFailure(error);
            this.offerDownload.set(failure.offerDownload);
            this.message.set(failure.message);
            this.state.set('failed');
            return;
        }
        if (cancel.aborted)
            return;
        const rendered = renderMarkdown(file.text, item.path, this.links);
        const article = this.content().nativeElement;
        article.replaceChildren(rendered.fragment);
        this.headings.set(rendered.headings);
        this.externalImages.set(rendered.externalImages);
        this.file.set(file);
        this.state.set('ready');
        await highlightFences(article);
    }

    protected setMode(mode: MarkdownMode): void {
        this.mode.set(mode);
        writePreference(MARKDOWN_MODE_KEY, mode);
    }

    protected toggleOutline(): void {
        this.outline.update(shown => !shown);
        writePreference(MARKDOWN_OUTLINE_KEY, this.outline() ? '1' : '0');
    }

    protected toggleLineNumbers(): void {
        this.reading.update('prose', { lineNumbers: !this.own().lineNumbers });
    }

    protected showImages(): void {
        showExternalImages(this.article().nativeElement);
        this.externalImages.set(0);
    }

    /** Scrolls the document to a heading (outline, `#section` links), inside the view rather than the page. */
    protected goTo(event: Event | null, id: string): void {
        event?.preventDefault();
        const target = this.article().nativeElement.querySelector(`#${CSS.escape(id)}`);
        target?.scrollIntoView?.({ block: 'start' });
    }

    /** Links of the document: its own headings, and other documents shown here (a modified click opens a new tab). */
    protected onArticleClick(event: MouseEvent): void {
        const anchor = (event.target as Element).closest?.('a');
        if (!anchor)
            return;
        if (anchor.dataset['anchor']) {
            this.goTo(event, anchor.dataset['anchor']);
            return;
        }
        const path = anchor.dataset['path'];
        if (path && this.fileHost.open && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
            event.preventDefault();
            this.fileHost.open(itemAt(path));
        }
    }

    /** Ctrl+= / Ctrl+− / Ctrl+0 inside the view: the text's size, rather than the whole page's zoom. */
    protected onKeydown(event: KeyboardEvent): void {
        if (!(event.ctrlKey || event.metaKey) || event.altKey || event.defaultPrevented)
            return;
        const step = event.key === '=' || event.key === '+' ? 1 : event.key === '-' || event.key === '_' ? -1 : event.key === '0' ? 0 : null;
        if (step === null)
            return;
        event.preventDefault();
        this.reading.changeSize('prose', step);
    }

    /** Ctrl+F finds in the source when it is shown; in the document alone, the browser's own find does. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        const editor = this.sourceEditor();
        if (event.defaultPrevented || !editor || this.mode() === 'rendered' || !(event.ctrlKey || event.metaKey) || event.shiftKey)
            return;
        const key = event.key.toLowerCase();
        if (key === 'f' && !event.altKey) {
            event.preventDefault();
            editor.find();
        } else if (key === 'g' && event.altKey) {
            event.preventDefault();
            editor.goToLine();
        }
    }
}

function readMode(): MarkdownMode {
    const stored = readPreference(MARKDOWN_MODE_KEY);
    return stored === 'source' || stored === 'split' ? stored : 'rendered';
}
