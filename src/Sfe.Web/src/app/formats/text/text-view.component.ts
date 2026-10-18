import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api, ApiError } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { formatSize } from '../../core/format';
import { LibraryItem, TextFile } from '../../core/models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { extensionOf, FILE_VIEW_HOST, FileFormat } from '../formats';
import { ReadingPanelComponent } from '../reading/reading-panel.component';
import { ReadingService } from '../reading/reading.service';
import { TextEditor } from './codemirror';
import { readPosition, writePosition } from './positions';
import { ENCODINGS, LINE_ENDINGS, textFailure } from './text-document';
import { TextEditActionsComponent, TextEditBarComponent } from './text-edit.component';
import { TextSession } from './text-session';

/** The encodings a file can be read with (Reopen with encoding), and those it can be saved in. */
const READ_ENCODINGS = ['utf-8', 'windows-1252', 'utf-16le', 'utf-16be'];
const SAVE_ENCODINGS = ['utf-8', 'utf-8-bom', 'windows-1252', 'utf-16le', 'utf-16be'];
const SAVE_LINE_ENDINGS = ['lf', 'crlf', 'cr'];

/** A part of a file too large to be read whole, as the server cut it (bytes). */
export const PART_BYTES = 256 * 1024;
/** How often a followed file is read for what was added. */
export const FOLLOW_MS = 2000;
/** The most characters a file read in parts keeps while followed (the oldest lines go). */
const FOLLOW_LIMIT = 4_000_000;

interface Part {
    offset: number;
    end: number;
    size: number;
    encoding: string;
}

/**
 * A text file in the viewer or on a page of its own (agents/docs/features/text-viewer.md): highlighting by its name,
 * find (Ctrl+F), go to line (Ctrl+Alt+G), and the reading preferences of its format's profile ("Aa": font, size with
 * Ctrl+= / Ctrl+− / Ctrl+0, line height, width, margins, wrapping, line numbers, colours). Edit (E) changes it and Ctrl+S
 * saves it, as the rights allow (agents/docs/features/text-editor.md); `edit` opens it ready to be changed. A file too
 * large to be read whole is read in parts; **Follow** shows what is added to its end (logs). It opens where it was last
 * read in this browser. Its keys, wheel and swipes are its own (`data-owns-keys`).
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
            @if (state() === 'ready' && !part()) {
                <sfe-text-edit-actions />
            }
            @if (part(); as shown) {
                <div class="text-parts" role="group" aria-label="Parts of the file">
                    <button type="button" class="text-action" data-part="start" title="The start of the file" [disabled]="shown.offset === 0 || loadingPart()" (click)="openPart(0)">Start</button>
                    <button type="button" class="text-action" data-part="previous" title="The part before" [disabled]="shown.offset === 0 || loadingPart()" (click)="previousPart()">‹ Earlier</button>
                    <button type="button" class="text-action" data-part="next" title="The part after" [disabled]="shown.end >= shown.size || loadingPart()" (click)="openPart(shown.end)">Later ›</button>
                    <button type="button" class="text-action" data-part="end" title="The end of the file" [disabled]="shown.end >= shown.size || loadingPart()" (click)="openPart(-1)">End</button>
                </div>
            }
            @if (state() === 'ready' && !session.editing()) {
                <button type="button" class="text-action" data-follow [class.active]="following()" [attr.aria-pressed]="following()"
                        title="Show what is added to the end of the file as it grows (a log being written)" (click)="toggleFollow()"><svg sfeIcon="down"></svg> Follow</button>
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
                <span class="text-status-details">
                    <span>{{ language() ?? 'Plain text' }}</span>
                    @if (part(); as shown) {
                        <span>{{ encodingName(shown.encoding) }}</span>
                        <span data-part-range>{{ partRange() }}</span>
                    } @else if (session.file(); as file) {
                        @if (session.editing()) {
                            <select class="text-status-select" data-save-encoding title="The encoding the file is saved in" aria-label="Save in encoding"
                                    (change)="session.encoding.set(valueOf($event))">
                                @for (encoding of saveEncodings; track encoding) {
                                    <option [value]="encoding" [selected]="encoding === session.encoding()">{{ encodingName(encoding) }}</option>
                                }
                            </select>
                            <select class="text-status-select" data-save-line-ending title="The line endings the file is saved with" aria-label="Save with line endings"
                                    (change)="session.lineEnding.set(valueOf($event))">
                                @for (ending of saveLineEndings; track ending) {
                                    <option [value]="ending" [selected]="ending === session.lineEnding()">{{ lineEndingName(ending) }}</option>
                                }
                            </select>
                        } @else {
                            <select class="text-status-select" data-read-encoding title="Read the file again in another encoding (letters shown wrong)" aria-label="Reopen with encoding"
                                    [disabled]="following()" (change)="session.reopen(valueOf($event))">
                                @for (encoding of readEncodings; track encoding) {
                                    <option [value]="encoding" [selected]="encoding === readValue(file.encoding)">{{ encoding === 'utf-8' && file.encoding === 'utf-8-bom' ? encodingName('utf-8-bom') : encodingName(encoding) }}</option>
                                }
                            </select>
                            <span>{{ lineEndingName(file.lineEnding) }}</span>
                        }
                        <span>{{ size(file.size) }}</span>
                    }
                </span>
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
    protected readonly language = signal<string | null>(null);
    protected readonly cursor = signal({ line: 1, column: 1 });
    protected readonly panelOpen = signal(false);
    /** The part shown of a file too large to be read whole; null when it is read whole. */
    protected readonly part = signal<Part | null>(null);
    protected readonly loadingPart = signal(false);
    protected readonly following = signal(false);
    protected editor: TextEditor | null = null;
    private cancel: AbortSignal | null = null;
    private followTimer: ReturnType<typeof setTimeout> | undefined;
    /** Where the next part of a followed file starts (bytes). */
    private followFrom = 0;
    private positionTimer: ReturnType<typeof setTimeout> | undefined;

    protected readonly readEncodings = READ_ENCODINGS;
    protected readonly saveEncodings = SAVE_ENCODINGS;
    protected readonly saveLineEndings = SAVE_LINE_ENDINGS;

    /** The reading preferences that apply: those of its format's profile. */
    protected readonly profile = computed(() => this.format()?.profile ?? 'code');
    protected readonly own = computed(() => this.reading.preferences()[this.profile()]);
    protected readonly style = computed(() => this.reading.style(this.profile()));

    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly partRange = computed(() => {
        const part = this.part();
        return part ? `${formatSize(part.offset)} – ${formatSize(part.end)} of ${formatSize(part.size)}` : '';
    });

    constructor() {
        const abort = new AbortController();
        this.cancel = abort.signal;
        inject(DestroyRef).onDestroy(() => {
            abort.abort();
            clearTimeout(this.followTimer);
            this.savePosition();
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
            if (error instanceof ApiError && error.status === 413) {
                // Too large to be read whole: in parts, a log from its end.
                await this.openPart(extensionOf(item) === 'log' ? -1 : 0);
                return;
            }
            this.fail(error);
            return;
        }
        if (cancel.aborted)
            return;
        this.session.attach(item, file);
        this.editor = this.createEditor(file.text, true);
        this.session.setEditor(this.editor);
        this.state.set('ready');
        if (untracked(this.edit))
            this.session.startEdit();
        // Where it was last read in this browser (not a new file, opened to be written).
        const line = untracked(this.edit) ? null : readPosition(item.path);
        if (line)
            this.editor.scrollToLine(line);
        await this.highlight(item);
    }

    private createEditor(text: string, editable: boolean): TextEditor {
        const own = this.own();
        const editor = new TextEditor(this.hostElement().nativeElement, {
            text,
            name: this.item().name,
            wrap: own.wrap,
            lineNumbers: own.lineNumbers,
            onCursor: (line, column) => this.cursor.set({ line, column }),
            ...(editable ? this.session.editorOptions() : {}),
        });
        editor.view.scrollDOM.addEventListener('scroll', () => {
            clearTimeout(this.positionTimer);
            this.positionTimer = setTimeout(() => this.savePosition(), 500);
        });
        return editor;
    }

    private async highlight(item: LibraryItem): Promise<void> {
        try {
            this.language.set(await this.editor!.setLanguage(item.name));
        } catch {
            // Its highlighting couldn't be loaded (offline after an update): plain text.
        }
    }

    /** Remembers the first line shown (a file read whole, not followed), to open it there next time. */
    private savePosition(): void {
        clearTimeout(this.positionTimer);
        if (this.editor && this.state() === 'ready' && !this.part() && !this.following())
            writePosition(this.item().path, this.editor.topLine());
    }

    /** A part of a file too large to be read whole: from `offset` (bytes), or its end (-1). */
    async openPart(offset: number, length = PART_BYTES): Promise<void> {
        const item = this.item();
        this.loadingPart.set(true);
        try {
            const part = await this.api.textPart(item.path, offset, length, this.part()?.encoding, this.cancel ?? undefined);
            this.part.set({ offset: part.offset, end: part.end, size: part.size, encoding: part.encoding });
            if (this.editor)
                this.editor.replace(part.text);
            else {
                this.editor = this.createEditor(part.text, false);
                this.state.set('ready');
                void this.highlight(item);
            }
            if (offset < 0)
                this.editor.scrollToEnd();
            else
                this.editor.scrollToLine(1);
        } catch (error) {
            if (this.cancel?.aborted)
                return;
            if (!this.editor)
                this.fail(error);
            else
                this.message.set((error as Error).message);
        } finally {
            this.loadingPart.set(false);
        }
    }

    /** The part ending where the one shown starts. */
    protected previousPart(): void {
        const part = this.part();
        if (!part)
            return;
        const from = Math.max(0, part.offset - PART_BYTES);
        void this.openPart(from, part.offset - from);
    }

    protected toggleFollow(): void {
        if (this.following())
            this.stopFollowing();
        else
            void this.startFollowing();
    }

    /** Shows what is added to the end of the file every few seconds (tail -f); read only meanwhile. */
    private async startFollowing(): Promise<void> {
        if (this.session.dirty() || this.session.editing())
            return;
        this.following.set(true);
        this.session.following.set(true);
        const part = this.part();
        if (part && part.end < part.size)
            await this.openPart(-1);
        this.followFrom = this.part()?.end ?? this.session.file()?.size ?? 0;
        this.editor?.scrollToEnd();
        this.followTimer = setTimeout(() => void this.follow(), FOLLOW_MS);
    }

    private stopFollowing(): void {
        clearTimeout(this.followTimer);
        this.following.set(false);
        this.session.following.set(false);
        // Read whole: its text and version as they are now, for a save.
        if (!this.part())
            void this.session.reload().then(() => this.editor?.scrollToEnd());
    }

    private async follow(): Promise<void> {
        if (!this.following())
            return;
        let next = FOLLOW_MS;
        try {
            const encoding = this.part()?.encoding ?? readEncoding(this.session.file()?.encoding);
            const added = await this.api.textPart(this.item().path, this.followFrom, PART_BYTES, encoding, this.cancel ?? undefined);
            if (!this.following())
                return;
            if (added.size < this.followFrom) {
                // Emptied or started anew (a log rotated): from its new end, or read whole again.
                if (this.part())
                    await this.openPart(-1);
                else
                    await this.session.reload();
                this.followFrom = this.part()?.end ?? this.session.file()?.size ?? 0;
                this.editor?.scrollToEnd();
            } else if (added.end > this.followFrom) {
                this.editor?.append(added.text, this.part() ? FOLLOW_LIMIT : Infinity);
                this.followFrom = added.end;
                this.part.update(part => part && { ...part, end: added.end, size: added.size });
                // More waiting than a part holds: the next one at once.
                if (added.end < added.size)
                    next = 0;
            }
        } catch {
            // Not reachable for a moment, or gone: tried again (opening it again tells what happened).
        }
        if (this.following())
            this.followTimer = setTimeout(() => void this.follow(), next);
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

    protected encodingName(encoding: string): string {
        return ENCODINGS[encoding] ?? encoding;
    }

    protected lineEndingName(ending: string): string {
        return LINE_ENDINGS[ending] ?? ending;
    }

    protected readValue(encoding: string): string {
        return readEncoding(encoding);
    }

    protected size(bytes: number): string {
        return formatSize(bytes);
    }

    protected valueOf(event: Event): string {
        return (event.target as HTMLSelectElement).value;
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

/** The encoding a file is read in again (a UTF-8 file with its mark reads as UTF-8). */
function readEncoding(encoding: string | undefined): string {
    return encoding === 'utf-8-bom' || !encoding ? 'utf-8' : encoding;
}
