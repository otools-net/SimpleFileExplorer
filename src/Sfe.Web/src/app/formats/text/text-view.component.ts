import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api, ApiError } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { formatSize, joinParts } from '../../core/format';
import { LibraryItem, TextFile } from '../../core/models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { TextEditor } from './codemirror';

const ENCODINGS: Readonly<Record<string, string>> = {
    'utf-8': 'UTF-8', 'utf-8-bom': 'UTF-8 with BOM', 'utf-16le': 'UTF-16 LE', 'utf-16be': 'UTF-16 BE', 'windows-1252': 'Windows-1252',
};
const LINE_ENDINGS: Readonly<Record<string, string>> = { lf: 'LF', crlf: 'CRLF', cr: 'CR', mixed: 'Mixed line endings' };

/** The view's toggles, kept while the viewer steps from file to file (the reading preferences come in a later phase). */
const wrapLines = signal(false);
const showLineNumbers = signal(true);

/**
 * A text file in the viewer (agents/docs/features/text-viewer.md), read only: highlighting by its name, line numbers,
 * wrapping, find (Ctrl+F) and go to line (Ctrl+Alt+G). Its keys, wheel and swipes are its own (`data-owns-keys`).
 */
@Component({
    selector: 'sfe-text-view',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'text-view', 'data-owns-keys': '', '(window:keydown)': 'onWindowKeydown($event)' },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Text">
            <button type="button" class="icon-button" [class.active]="wrap()" [attr.aria-pressed]="wrap()" title="Wrap long lines" aria-label="Wrap long lines" (click)="toggleWrap()"><svg sfeIcon="wrap"></svg></button>
            <button type="button" class="icon-button" [class.active]="lineNumbers()" [attr.aria-pressed]="lineNumbers()" title="Line numbers" aria-label="Line numbers" (click)="toggleLineNumbers()"><svg sfeIcon="line-numbers"></svg></button>
            <button type="button" class="icon-button" title="Find (Ctrl+F)" aria-label="Find" [disabled]="state() !== 'ready'" (click)="editor?.find()"><svg sfeIcon="search"></svg></button>
            <button type="button" class="icon-button" title="Go to line (Ctrl+Alt+G)" aria-label="Go to line" [disabled]="state() !== 'ready'" (click)="editor?.goToLine()"><svg sfeIcon="goto-line"></svg></button>
        </div>
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
        <div #host class="text-editor" [hidden]="state() !== 'ready'"></div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                <span>Ln {{ cursor().line }}, Col {{ cursor().column }}</span>
            </footer>
        }
    `,
})
export class TextViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);

    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    private readonly hostElement = viewChild.required<ElementRef<HTMLElement>>('host');

    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    /** Offered when the file can't be shown as text. */
    protected readonly offerDownload = signal(false);
    protected readonly file = signal<TextFile | null>(null);
    protected readonly language = signal<string | null>(null);
    protected readonly cursor = signal({ line: 1, column: 1 });
    protected readonly wrap = wrapLines.asReadonly();
    protected readonly lineNumbers = showLineNumbers.asReadonly();
    protected editor: TextEditor | null = null;

    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const file = this.file();
        return file ? joinParts([this.language() ?? 'Plain text', ENCODINGS[file.encoding] ?? file.encoding, LINE_ENDINGS[file.lineEnding] ?? file.lineEnding, formatSize(file.size)], '  ·  ') : '';
    });

    constructor() {
        const abort = new AbortController();
        inject(DestroyRef).onDestroy(() => {
            abort.abort();
            this.editor?.destroy();
            this.editor = null;
        });
        // One view per file (the viewer gives each file elements of its own): loaded once.
        effect(() => {
            const item = this.item();
            untracked(() => void this.load(item, abort.signal));
        });
    }

    private async load(item: LibraryItem, cancel: AbortSignal): Promise<void> {
        if (this.editor || this.state() !== 'loading')
            return;
        let file: TextFile;
        try {
            file = await this.api.textFile(item.path, cancel);
        } catch (error) {
            if (cancel.aborted)
                return;
            this.fail(error);
            return;
        }
        if (cancel.aborted)
            return;
        this.file.set(file);
        this.editor = new TextEditor(this.hostElement().nativeElement, {
            text: file.text,
            name: item.name,
            wrap: this.wrap(),
            lineNumbers: this.lineNumbers(),
            onCursor: (line, column) => this.cursor.set({ line, column }),
        });
        this.state.set('ready');
        try {
            this.language.set(await this.editor.setLanguage(item.name));
        } catch {
            // Its highlighting couldn't be loaded (offline after an update): plain text.
        }
    }

    private fail(error: unknown): void {
        const status = error instanceof ApiError ? error.status : 0;
        this.offerDownload.set(status === 413 || status === 415 || status === 500);
        this.message.set(
            status === 413 ? 'This file is too large to be shown as text (more than 10 MB).'
                : status === 415 ? 'This file is not text: it can only be downloaded.'
                    : status === 404 ? 'This file is not there any more.'
                        : status === 403 ? 'You may not open this file.'
                            : `It could not be read: ${error instanceof Error ? error.message : String(error)}`);
        this.state.set('failed');
    }

    protected toggleWrap(): void {
        wrapLines.update(wrap => !wrap);
        this.editor?.setWrap(wrapLines());
    }

    protected toggleLineNumbers(): void {
        showLineNumbers.update(show => !show);
        this.editor?.setLineNumbers(showLineNumbers());
    }

    /** Ctrl+F and Ctrl+Alt+G anywhere in the viewer find in the text, rather than in the page behind it. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (event.defaultPrevented || !this.editor || !(event.ctrlKey || event.metaKey) || event.shiftKey)
            return;
        const key = event.key.toLowerCase();
        if (key === 'f' && !event.altKey) {
            event.preventDefault();
            this.editor.find();
        } else if (key === 'g' && event.altKey) {
            event.preventDefault();
            this.editor.goToLine();
        }
    }
}
