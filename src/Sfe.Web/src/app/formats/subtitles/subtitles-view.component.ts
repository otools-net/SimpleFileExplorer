import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { LibraryItem, TextFile } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { ReadingPanelComponent } from '../reading/reading-panel.component';
import { ReadingService } from '../reading/reading.service';
import { TextEditor } from '../text/codemirror';
import { textDetails, textFailure } from '../text/text-document';
import { TextEditActionsComponent, TextEditBarComponent } from '../text/text-edit.component';
import { TextSession } from '../text/text-session';
import { VirtualRows } from '../virtual-rows';
import { formatSpan, formatTime, parseShift, parseSubtitles, shiftEdits, SubtitleCue, SubtitleDocument, subtitleTypeOf } from './subtitles';

export type SubtitlesMode = 'cues' | 'source' | 'split';
export const SUBTITLES_MODE_KEY = 'sfe.subtitles.mode';
/** How long typing pauses before the cues show the source again, and the filter what is typed in it. */
const PARSE_DELAY_MS = 300;
const FILTER_DELAY_MS = 200;
const MODES: readonly { id: SubtitlesMode; label: string; title: string }[] = [
    { id: 'cues', label: 'Cues', title: 'What is said, and when' },
    { id: 'source', label: 'Source', title: 'Its text, as written' },
    { id: 'split', label: 'Split', title: 'Both, side by side' },
];

/**
 * A subtitle file, SubRip or WebVTT (agents/docs/features/subtitles-viewer.md): its cues (media-captions) with their
 * times, filtered, only those shown drawn (`VirtualRows`); a cue played in its video (the one next to it, named as it
 * is); its source (CodeMirror), or both side by side. Edit changes the source, the cues following as it is typed;
 * **Shift times** moves every cue (subtitles out of sync), one undo.
 */
@Component({
    selector: 'sfe-subtitles-view',
    imports: [IconComponent, ReadingPanelComponent, TextEditActionsComponent, TextEditBarComponent],
    providers: [TextSession],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view subtitles-view',
        'data-owns-keys': '',
        '[attr.data-reader-theme]': 'reading.preferences().theme',
        '[attr.data-mode]': 'mode()',
        '[style]': 'style()',
        '(keydown)': 'onKeydown($event)',
        '(window:keydown)': 'onWindowKeydown($event)',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Subtitles">
            @if (state() === 'ready') {
                <sfe-text-edit-actions />
                @if (session.editing()) {
                    <span class="subtitle-shift">
                        <input #shiftBox type="text" inputmode="decimal" class="json-filter" data-shift-box placeholder="± seconds" aria-label="Seconds to move every cue by"
                               title="Seconds to move every cue by: 2.5 later, -2.5 earlier" (keydown.enter)="shift()" />
                        <button type="button" class="markdown-mode csv-header-toggle" data-shift title="Move every cue by these seconds (Ctrl+Z undoes it)" (click)="shift()">Shift times</button>
                    </span>
                }
            }
            <div class="markdown-modes" role="radiogroup" aria-label="Show">
                @for (choice of modes; track choice.id) {
                    <button type="button" role="radio" class="markdown-mode" [class.active]="mode() === choice.id" [attr.aria-checked]="mode() === choice.id"
                            [attr.data-mode]="choice.id" [title]="choice.title" (click)="setMode(choice.id)">{{ choice.label }}</button>
                }
            </div>
            @if (mode() !== 'source') {
                <input #filterBox type="search" class="json-filter" placeholder="Filter cues" aria-label="Filter cues" title="Shows the cues holding this (Ctrl+F)"
                       [value]="query()" (input)="setQuery($any($event.target).value)" (keydown)="onFilterKeydown($event)" />
            }
            @if (mode() !== 'cues') {
                <button type="button" class="icon-button" [class.active]="own().lineNumbers" [attr.aria-pressed]="own().lineNumbers" title="Line numbers" aria-label="Line numbers" (click)="toggleLineNumbers()"><svg sfeIcon="line-numbers"></svg></button>
                <button type="button" class="icon-button" title="Find in the source (Ctrl+F)" aria-label="Find" [disabled]="state() !== 'ready'" (click)="sourceEditor()?.find()"><svg sfeIcon="search"></svg></button>
            }
            <button type="button" class="icon-button reading-toggle" data-reading-toggle [class.active]="panelOpen()" [attr.aria-expanded]="panelOpen()"
                    title="Reading settings: font, size, colours" aria-label="Reading settings" (click)="panelOpen.set(!panelOpen())">Aa</button>
        </div>
        @if (panelOpen()) {
            <sfe-reading-panel profile="code" (closed)="panelOpen.set(false)" />
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
        <div class="json-panes subtitles-panes" [hidden]="state() !== 'ready'">
            <div class="json-pane" [hidden]="mode() === 'source'">
                @if (problems()[0]; as problem) {
                    <p class="json-problem" role="status" data-problem>
                        Line {{ problem.line }}: {{ problem.message }}{{ problems().length > 1 ? ' (and ' + (problems().length - 1) + ' more)' : '' }}.
                        <button type="button" class="link-button" (click)="showLine(problem.line)">Show in the source</button>
                    </p>
                }
                <div #scroller class="csv-scroller subtitle-list" role="listbox" tabindex="0" [attr.aria-label]="'Cues of ' + item().name"
                     [attr.aria-activedescendant]="activeId()" (keydown)="onListKeydown($event)">
                    <div class="csv-body" [style.height.px]="virtual.total()">
                        @for (shown of virtual.items(); track shown.key) {
                            @let cue = order()[shown.index];
                            @if (cue) {
                                <div class="subtitle-cue" role="option" [id]="optionId(cue)" [attr.data-cue]="cue.index" [class.selected]="selected() === cue.index"
                                     [class.overlaps]="cue.overlaps" [attr.aria-selected]="selected() === cue.index"
                                     [style.transform]="'translateY(' + shown.start + 'px)'" [style.height.px]="virtual.height"
                                     [title]="cue.overlaps ? 'Starts before the cue before it has ended' : ''" (click)="select(cue)" (dblclick)="showCue(cue)">
                                    <span class="subtitle-number">{{ cue.id || cue.index + 1 }}</span>
                                    <span class="subtitle-time subtitle-start">{{ time(cue.start) }}</span>
                                    <span class="subtitle-time subtitle-end">{{ time(cue.end) }}</span>
                                    <span class="subtitle-text">@for (run of cue.runs; track $index) {<span [class.subtitle-voice]="run.voice" [class.italic]="run.italic" [class.bold]="run.bold" [class.underline]="run.underline">{{ run.text }}</span>}</span>
                                </div>
                            }
                        }
                    </div>
                    @if (!order().length) {
                        <p class="csv-empty">{{ query().trim() ? 'No cue holds “' + query().trim() + '”.' : 'No cues.' }}</p>
                    }
                </div>
            </div>
            <div #source class="text-editor json-source" [hidden]="mode() === 'cues'"></div>
        </div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                @if (session.dirty()) {
                    <span class="text-edited" data-edited>Edited</span>
                }
                <span class="json-selection">
                    <span data-counts>{{ counts() }}</span>
                    @if (selectedCue(); as cue) {
                        <span class="json-path" data-selected-cue>Cue {{ cue.id || cue.index + 1 }} · {{ time(cue.start) }} · {{ span(cue.end - cue.start) }}</span>
                        @if (playUrl(cue); as url) {
                            <a class="link-button" data-play [href]="url" target="_blank" rel="noopener" [title]="'Play ' + videoName() + ' from this cue, in a new tab'">Play</a>
                        }
                    }
                </span>
            </footer>
        }
    `,
})
export class SubtitlesViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);
    /** Opened ready to be changed (a new file, a copy just saved). */
    readonly edit = input(false);

    private readonly api = inject(Api);
    protected readonly session = inject(TextSession);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    protected readonly reading = inject(ReadingService);
    private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
    private readonly filterBox = viewChild<ElementRef<HTMLInputElement>>('filterBox');
    private readonly shiftBox = viewChild<ElementRef<HTMLInputElement>>('shiftBox');
    private readonly source = viewChild.required<ElementRef<HTMLElement>>('source');

    protected readonly modes = MODES;
    protected readonly virtual = new VirtualRows(() => this.scroller()?.nativeElement ?? null, 32);
    protected readonly mode = signal<SubtitlesMode>(readMode());
    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly offerDownload = signal(false);
    protected readonly panelOpen = signal(false);
    protected readonly document = signal<SubtitleDocument | null>(null);
    protected readonly query = signal('');
    private readonly applied = signal('');
    /** The cue selected (its index among all cues). */
    protected readonly selected = signal<number | null>(null);
    /** The video the subtitles belong to, when there is one the session may play. */
    protected readonly video = signal<string | null>(null);
    /** The source's editor, made the first time the source is shown. */
    protected readonly sourceEditor = signal<TextEditor | null>(null);
    private parseLater: ReturnType<typeof setTimeout> | undefined;
    private filterLater: ReturnType<typeof setTimeout> | undefined;
    /** The latest parse asked for: an earlier one finishing later is dropped. */
    private parses = 0;
    private readonly prefix = `subtitle-${Math.random().toString(36).slice(2, 8)}`;

    protected readonly own = computed(() => this.reading.preferences().code);
    protected readonly style = computed(() => this.reading.style('code'));
    protected readonly problems = computed(() => this.document()?.problems ?? []);
    /** Hours shown on every time when some cue has them, so that the times line up. */
    private readonly hours = computed(() => (this.document()?.cues.at(-1)?.end ?? 0) >= 3600);
    protected readonly order = computed(() => {
        const cues = this.document()?.cues ?? [];
        const needle = this.applied().trim().toLowerCase();
        return needle ? cues.filter(cue => cue.plain.toLowerCase().includes(needle) || cue.id.toLowerCase().includes(needle)) : cues;
    });
    protected readonly selectedCue = computed(() => {
        const index = this.selected();
        return index === null ? null : this.document()?.cues[index] ?? null;
    });
    protected readonly activeId = computed(() => {
        const cue = this.selectedCue();
        return cue && this.order().includes(cue) ? this.optionId(cue) : null;
    });
    protected readonly counts = computed(() => {
        const cues = this.document()?.cues ?? [];
        const shown = this.order().length;
        const count = this.applied().trim() ? `${shown.toLocaleString()} of ${cues.length.toLocaleString()} cues` : `${cues.length.toLocaleString()} ${cues.length === 1 ? 'cue' : 'cues'}`;
        const overlaps = cues.filter(cue => cue.overlaps).length;
        const length = cues.length ? ` · ${formatSpan(cues.at(-1)!.end)}` : '';
        return `${count}${length}${overlaps ? ` · ${overlaps} overlapping` : ''}`;
    });
    protected readonly videoName = computed(() => this.video()?.split('/').at(-1) ?? '');
    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const file = this.session.file();
        return file ? textDetails(file, subtitleTypeOf(this.item().name) === 'srt' ? 'SubRip' : 'WebVTT') : '';
    });

    constructor() {
        const abort = new AbortController();
        inject(DestroyRef).onDestroy(() => {
            abort.abort();
            clearTimeout(this.parseLater);
            clearTimeout(this.filterLater);
            this.virtual.destroy();
            this.sourceEditor()?.destroy();
        });
        effect(() => {
            const item = this.item();
            untracked(() => void this.load(item, abort.signal));
        });
        effect(() => {
            const count = this.order().length;
            const ready = this.state() === 'ready';
            untracked(() => {
                this.virtual.setCount(count);
                if (ready)
                    this.virtual.mount();
            });
        });
        effect(() => {
            const ready = this.state() === 'ready';
            const mode = this.mode();
            const { wrap, lineNumbers } = this.own();
            untracked(() => {
                if (ready && mode !== 'cues')
                    this.editor()?.configure({ wrap, lineNumbers });
            });
        });
        // Editing needs the source: beside the cues, when only the cues were shown.
        effect(() => {
            if (this.session.editing() && untracked(this.mode) === 'cues')
                untracked(() => this.mode.set('split'));
        });
        // The cues follow the source as it is typed, once typing pauses.
        effect(() => {
            if (!this.session.revision())
                return;
            clearTimeout(this.parseLater);
            this.parseLater = setTimeout(() => {
                const text = this.sourceEditor()?.text() ?? this.session.file()?.text;
                if (text !== undefined)
                    void this.parse(text);
            }, PARSE_DELAY_MS);
        });
        effect(() => {
            this.style();
            this.reading.linkFonts('code');
            untracked(() => {
                this.sourceEditor()?.remeasure();
                setTimeout(() => this.virtual.setRowHeight(this.scroller()?.nativeElement.querySelector<HTMLElement>('.subtitle-cue')?.offsetHeight ?? 0));
            });
        });
    }

    private async load(item: LibraryItem, cancel: AbortSignal): Promise<void> {
        if (this.state() !== 'loading' || this.session.file())
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
        this.session.attach(item, file);
        await this.parse(file.text);
        this.state.set('ready');
        if (this.mode() !== 'cues')
            this.editor();
        if (untracked(this.edit))
            this.session.startEdit();
        // The video to play the cues in: only for a session that may watch videos.
        if (this.fileHost.can(Rights.viewFiles))
            this.api.subtitleVideo(item.path, cancel).then(found => this.video.set(found.path), () => undefined);
    }

    private async parse(text: string): Promise<void> {
        const parse = ++this.parses;
        let document: SubtitleDocument;
        try {
            document = await parseSubtitles(text, subtitleTypeOf(this.item().name));
        } catch (error) {
            document = { cues: [], problems: [{ message: (error as Error).message, line: 1 }] };
        }
        if (parse === this.parses)
            this.document.set(document);
    }

    /** The source's editor, made now when it isn't yet. */
    private editor(): TextEditor | null {
        const existing = this.sourceEditor();
        const file = this.session.file();
        if (existing || !file)
            return existing;
        const { wrap, lineNumbers } = this.own();
        const created = new TextEditor(this.source().nativeElement, { text: file.text, name: this.item().name, wrap, lineNumbers, ...this.session.editorOptions() });
        this.session.setEditor(created);
        this.sourceEditor.set(created);
        void created.setLanguage(this.item().name).catch(() => undefined);
        return created;
    }

    protected optionId(cue: SubtitleCue): string {
        return `${this.prefix}-${cue.index}`;
    }

    protected time(seconds: number): string {
        return formatTime(seconds, this.hours());
    }

    protected span(seconds: number): string {
        return formatSpan(seconds);
    }

    protected playUrl(cue: SubtitleCue): string | null {
        const video = this.video();
        return video ? this.config.urls()?.player(video, Math.floor(cue.start)) ?? null : null;
    }

    protected setMode(mode: SubtitlesMode): void {
        this.mode.set(mode);
        writePreference(SUBTITLES_MODE_KEY, mode);
        if (mode !== 'cues' && this.state() === 'ready')
            this.editor();
    }

    protected toggleLineNumbers(): void {
        this.reading.update('code', { lineNumbers: !this.own().lineNumbers });
    }

    protected select(cue: SubtitleCue): void {
        this.selected.set(cue.index);
        this.scroller()?.nativeElement.focus({ preventScroll: true });
    }

    /** The cue's lines in the source, selected (beside the cues when only they are shown). */
    protected showCue(cue: SubtitleCue): void {
        if (cue.line !== null)
            this.showLine(cue.line);
    }

    protected showLine(line: number): void {
        if (this.mode() === 'cues')
            this.setMode('split');
        const editor = this.editor();
        setTimeout(() => {
            if (!editor)
                return;
            const doc = editor.view.state.doc;
            const at = doc.line(Math.min(Math.max(1, line), doc.lines));
            editor.select(at.from, at.to);
            editor.focus();
        });
    }

    /** Moves every cue by the seconds typed (an edit of the source: undone with Ctrl+Z). */
    protected shift(): void {
        const editor = this.sourceEditor();
        const box = this.shiftBox()?.nativeElement;
        const milliseconds = parseShift(box?.value ?? '');
        if (!editor || !this.session.editing())
            return;
        if (milliseconds === null || milliseconds === 0) {
            this.toasts.show('Type the seconds to move the cues by: 2.5 for later, -2.5 for earlier.', { error: true });
            box?.focus();
            return;
        }
        editor.edit(shiftEdits(editor.text(), milliseconds));
        this.toasts.show(`Every cue moved ${Math.abs(milliseconds / 1000)} s ${milliseconds > 0 ? 'later' : 'earlier'}`);
    }

    protected setQuery(query: string): void {
        this.query.set(query);
        clearTimeout(this.filterLater);
        this.filterLater = setTimeout(() => {
            this.applied.set(query);
            this.virtual.scrollTo(0, 'start');
        }, FILTER_DELAY_MS);
    }

    protected onFilterKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape' && this.query()) {
            event.preventDefault();
            event.stopPropagation();
            this.setQuery('');
        } else if (event.key === 'Enter' || event.key === 'ArrowDown') {
            event.preventDefault();
            this.scroller()?.nativeElement.focus();
            const first = this.order()[0];
            if (this.selected() === null && first)
                this.selected.set(first.index);
        }
    }

    /** ↑ ↓, Page Up / Down, Home / End move between the cues; Enter shows the one selected in the source. */
    protected onListKeydown(event: KeyboardEvent): void {
        const order = this.order();
        if (!order.length || event.altKey || event.ctrlKey || event.metaKey)
            return;
        const current = order.findIndex(cue => cue.index === this.selected());
        const page = Math.max(1, Math.floor((this.scroller()?.nativeElement.clientHeight ?? 0) / this.virtual.height) - 1);
        let position = current;
        switch (event.key) {
            case 'ArrowDown': position = current < 0 ? 0 : current + 1; break;
            case 'ArrowUp': position = current < 0 ? 0 : current - 1; break;
            case 'PageDown': position = Math.max(0, current) + page; break;
            case 'PageUp': position = Math.max(0, current) - page; break;
            case 'Home': position = 0; break;
            case 'End': position = order.length - 1; break;
            case 'Enter':
                if (current >= 0) {
                    event.preventDefault();
                    this.showCue(order[current]);
                }
                return;
            default: return;
        }
        event.preventDefault();
        position = Math.max(0, Math.min(order.length - 1, position));
        this.selected.set(order[position].index);
        this.virtual.scrollTo(position);
    }

    /** Ctrl+= / Ctrl+− / Ctrl+0 inside the view: the text's size, rather than the whole page's zoom. */
    protected onKeydown(event: KeyboardEvent): void {
        if (!(event.ctrlKey || event.metaKey) || event.altKey || event.defaultPrevented)
            return;
        const step = event.key === '=' || event.key === '+' ? 1 : event.key === '-' || event.key === '_' ? -1 : event.key === '0' ? 0 : null;
        if (step === null)
            return;
        event.preventDefault();
        this.reading.changeSize('code', step);
    }

    /** Ctrl+F filters the cues when only they are shown, finds in the source otherwise; Ctrl+Alt+G goes to a line. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (this.session.handleKey(event))
            return;
        if (event.defaultPrevented || this.state() !== 'ready' || !(event.ctrlKey || event.metaKey) || event.shiftKey)
            return;
        const key = event.key.toLowerCase();
        const editor = this.sourceEditor();
        if (key === 'f' && !event.altKey) {
            event.preventDefault();
            if (this.mode() === 'cues' || !editor) {
                this.filterBox()?.nativeElement.focus();
                this.filterBox()?.nativeElement.select();
            } else
                editor.find();
        } else if (key === 'g' && event.altKey && editor && this.mode() !== 'cues') {
            event.preventDefault();
            editor.goToLine();
        }
    }
}

function readMode(): SubtitlesMode {
    const stored = readPreference(SUBTITLES_MODE_KEY);
    return stored === 'source' || stored === 'split' ? stored : 'cues';
}
