import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { LibraryItem, TextFile } from '../../core/models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { ReadingPanelComponent } from '../reading/reading-panel.component';
import { ReadingService } from '../reading/reading.service';
import { TextEditor } from './codemirror';
import { textDetails, textFailure } from './text-document';
import { TextEditActionsComponent, TextEditBarComponent } from './text-edit.component';
import { TextSession } from './text-session';

/**
 * A text file in the viewer or on a page of its own (agents/docs/features/text-viewer.md): highlighting by its name,
 * find (Ctrl+F), go to line (Ctrl+Alt+G), and the reading preferences of its format's profile ("Aa": font, size with
 * Ctrl+= / Ctrl+− / Ctrl+0, line height, width, margins, wrapping, line numbers, colours). Edit (E) changes it and Ctrl+S
 * saves it, as the rights allow (agents/docs/features/text-editor.md); `edit` opens it ready to be changed. Its keys,
 * wheel and swipes are its own (`data-owns-keys`).
 */
@Component({
    selector: 'sfe-text-view',
    imports: [IconComponent, ReadingPanelComponent, TextEditActionsComponent, TextEditBarComponent],
    providers: [TextSession],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view',
        'data-owns-keys': '',
        '[class.prose]': "profile() === 'prose'",
        '[attr.data-reader-theme]': 'reading.preferences().theme',
        '[style]': 'style()',
        '(keydown)': 'onKeydown($event)',
        '(window:keydown)': 'onWindowKeydown($event)',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Text">
            @if (state() === 'ready') {
                <sfe-text-edit-actions />
            }
            <button type="button" class="icon-button" [class.active]="own().wrap" [attr.aria-pressed]="own().wrap" title="Wrap long lines" aria-label="Wrap long lines" (click)="toggle('wrap')"><svg sfeIcon="wrap"></svg></button>
            <button type="button" class="icon-button" [class.active]="own().lineNumbers" [attr.aria-pressed]="own().lineNumbers" title="Line numbers" aria-label="Line numbers" (click)="toggle('lineNumbers')"><svg sfeIcon="line-numbers"></svg></button>
            <button type="button" class="icon-button" title="Find (Ctrl+F)" aria-label="Find" [disabled]="state() !== 'ready'" (click)="editor?.find()"><svg sfeIcon="search"></svg></button>
            <button type="button" class="icon-button" title="Go to line (Ctrl+Alt+G)" aria-label="Go to line" [disabled]="state() !== 'ready'" (click)="editor?.goToLine()"><svg sfeIcon="goto-line"></svg></button>
            <button type="button" class="icon-button reading-toggle" data-reading-toggle [class.active]="panelOpen()" [attr.aria-expanded]="panelOpen()"
                    title="Reading settings: font, size, width, colours" aria-label="Reading settings" (click)="panelOpen.set(!panelOpen())">Aa</button>
        </div>
        @if (panelOpen()) {
            <sfe-reading-panel [profile]="profile()" (closed)="panelOpen.set(false)" />
        }
        <sfe-text-edit-bar />
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
                @if (session.dirty()) {
                    <span class="text-edited" data-edited>Edited</span>
                }
                <span>Ln {{ cursor().line }}, Col {{ cursor().column }}</span>
            </footer>
        }
    `,
})
export class TextViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);
    /** Opened ready to be changed (a new file, a copy just saved). */
    readonly edit = input(false);

    private readonly api = inject(Api);
    protected readonly session = inject(TextSession);
    private readonly config = inject(ConfigService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    protected readonly reading = inject(ReadingService);
    private readonly hostElement = viewChild.required<ElementRef<HTMLElement>>('host');

    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    /** Offered when the file can't be shown as text. */
    protected readonly offerDownload = signal(false);
    protected readonly file = signal<TextFile | null>(null);
    protected readonly language = signal<string | null>(null);
    protected readonly cursor = signal({ line: 1, column: 1 });
    protected readonly panelOpen = signal(false);
    protected editor: TextEditor | null = null;

    /** The reading preferences that apply: those of its format's profile. */
    protected readonly profile = computed(() => this.format()?.profile ?? 'code');
    protected readonly own = computed(() => this.reading.preferences()[this.profile()]);
    protected readonly style = computed(() => this.reading.style(this.profile()));

    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const file = this.file();
        return file ? textDetails(file, this.language()) : '';
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
        // The preferences as they change (here, from the "Aa" panel, or in another view).
        effect(() => {
            const { wrap, lineNumbers } = this.own();
            this.reading.linkFonts(this.profile());
            const editor = this.editor;
            if (this.state() === 'ready' && editor)
                untracked(() => editor.configure({ wrap, lineNumbers }));
        });
        // A new font, size or spacing changes the height of the lines: the editor measures them again.
        effect(() => {
            this.style();
            if (this.state() === 'ready')
                untracked(() => this.editor?.remeasure());
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
        this.session.attach(item, file);
        const own = this.own();
        this.editor = new TextEditor(this.hostElement().nativeElement, {
            text: file.text,
            name: item.name,
            wrap: own.wrap,
            lineNumbers: own.lineNumbers,
            onCursor: (line, column) => this.cursor.set({ line, column }),
            ...this.session.editorOptions(),
        });
        this.session.setEditor(this.editor);
        this.state.set('ready');
        if (untracked(this.edit))
            this.session.startEdit();
        try {
            this.language.set(await this.editor.setLanguage(item.name));
        } catch {
            // Its highlighting couldn't be loaded (offline after an update): plain text.
        }
    }

    private fail(error: unknown): void {
        const failure = textFailure(error);
        this.offerDownload.set(failure.offerDownload);
        this.message.set(failure.message);
        this.state.set('failed');
    }

    protected toggle(key: 'wrap' | 'lineNumbers'): void {
        this.reading.update(this.profile(), { [key]: !this.own()[key] });
    }

    /** Ctrl+= / Ctrl+− / Ctrl+0 inside the view: the text's size, rather than the whole page's zoom. */
    protected onKeydown(event: KeyboardEvent): void {
        if (!(event.ctrlKey || event.metaKey) || event.altKey || event.defaultPrevented)
            return;
        const step = event.key === '=' || event.key === '+' ? 1 : event.key === '-' || event.key === '_' ? -1 : event.key === '0' ? 0 : null;
        if (step === null)
            return;
        event.preventDefault();
        this.reading.changeSize(this.profile(), step);
    }

    /**
     * Ctrl+F and Ctrl+Alt+G anywhere in the viewer find in the text, rather than in the page behind it; Ctrl+S saves,
     * E edits.
     */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (this.session.handleKey(event))
            return;
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
