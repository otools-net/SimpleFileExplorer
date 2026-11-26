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
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { LibraryItem } from '../../core/models';
import { TextFile } from '../format.models';
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
import { MenuEntry } from '../../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../../menus/overflow-row';
import { findEntry, lineNumbersEntry, modesEntry, readingEntry } from '../toolbar-entries';
import { TextSession } from '../text/text-session';
import { VirtualRows } from '../virtual-rows';
import {
    formatSpan,
    formatTime,
    parseShift,
    parseSubtitles,
    shiftEdits,
    SubtitleCue,
    SubtitleDocument,
    subtitleTypeOf,
} from './subtitles';
import { FILE_SOURCE } from '../file-source';
import { cueCounts, shifted, shiftRefused, subtitlesModes, SubtitlesMode, subtitlesTexts } from './subtitles-texts';

export const SUBTITLES_MODE_KEY = 'sfe.subtitles.mode';
/** How long typing pauses before the cues show the source again, and the filter what is typed in it. */
const PARSE_DELAY_MS = 300;
const FILTER_DELAY_MS = 200;

/**
 * A subtitle file, SubRip or WebVTT (agents/docs/features/subtitles-viewer.md): its cues (media-captions) with their
 * times, filtered, only those shown drawn (`VirtualRows`); a cue played in its video (the one next to it, named as it
 * is); its source (CodeMirror), or both side by side. Edit changes the source, the cues following as it is typed;
 * **Shift times** moves every cue (subtitles out of sync), one undo.
 */
@Component({
    selector: 'sfe-subtitles-view',
    imports: [
        IconComponent,
        ReadingPanelComponent,
        TextEditActionsComponent,
        TextEditBarComponent,
        OverflowRowDirective,
        OverflowItemDirective,
        OverflowMoreComponent,
    ],
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
    templateUrl: './subtitles-view.component.html',
})
export class SubtitlesViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);
    /** Opened ready to be changed (a new file, a copy just saved). */
    readonly edit = input(false);

    private readonly api = inject(Api);
    private readonly fileSource = inject(FILE_SOURCE);
    protected readonly session = inject(TextSession);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    protected readonly reading = inject(ReadingService);
    private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
    private readonly filterBox = viewChild<ElementRef<HTMLInputElement>>('filterBox');
    private readonly shiftBox = viewChild<ElementRef<HTMLInputElement>>('shiftBox');
    private readonly source = viewChild.required<ElementRef<HTMLElement>>('source');

    protected readonly modes = subtitlesModes();
    protected readonly texts = subtitlesTexts;
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
    /** The toolbar's buttons in its "⋯" menu, when they don't fit (the editing actions and the fields stay). */
    protected readonly more = {
        mode: (): MenuEntry => modesEntry(this.modes, this.mode(), (mode) => this.setMode(mode)),
        lineNumbers: (): MenuEntry => lineNumbersEntry(this.own().lineNumbers, () => this.toggleLineNumbers()),
        find: (): MenuEntry => findEntry(this.state() !== 'ready', () => this.sourceEditor()?.find()),
        reading: (): MenuEntry => readingEntry(this.panelOpen(), () => this.panelOpen.set(!this.panelOpen())),
    };
    protected readonly style = computed(() => this.reading.style('code'));
    protected readonly problems = computed(() => this.document()?.problems ?? []);
    /** Hours shown on every time when some cue has them, so that the times line up. */
    private readonly hours = computed(() => (this.document()?.cues.at(-1)?.end ?? 0) >= 3600);
    protected readonly order = computed(() => {
        const cues = this.document()?.cues ?? [];
        const needle = this.applied().trim().toLowerCase();
        return needle
            ? cues.filter((cue) => cue.plain.toLowerCase().includes(needle) || cue.id.toLowerCase().includes(needle))
            : cues;
    });
    protected readonly selectedCue = computed(() => {
        const index = this.selected();
        return index === null ? null : (this.document()?.cues[index] ?? null);
    });
    protected readonly activeId = computed(() => {
        const cue = this.selectedCue();
        return cue && this.order().includes(cue) ? this.optionId(cue) : null;
    });
    protected readonly counts = computed(() => {
        const cues = this.document()?.cues ?? [];
        const shown = this.applied().trim() ? this.order().length : null;
        const overlaps = cues.filter((cue) => cue.overlaps).length;
        return cueCounts(cues.length, shown, cues.length ? formatSpan(cues.at(-1)!.end) : null, overlaps);
    });
    protected readonly videoName = computed(() => this.video()?.split('/').at(-1) ?? '');
    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.fileSource.download(this.item()) : null,
    );
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
                if (ready) this.virtual.mount();
            });
        });
        effect(() => {
            const ready = this.state() === 'ready';
            const mode = this.mode();
            const { wrap, lineNumbers } = this.own();
            untracked(() => {
                if (ready && mode !== 'cues') this.editor()?.configure({ wrap, lineNumbers });
            });
        });
        // Editing needs the source: beside the cues, when only the cues were shown.
        effect(() => {
            if (this.session.editing() && untracked(this.mode) === 'cues') untracked(() => this.mode.set('split'));
        });
        // The cues follow the source as it is typed, once typing pauses.
        effect(() => {
            if (!this.session.revision()) return;
            clearTimeout(this.parseLater);
            this.parseLater = setTimeout(() => {
                const text = this.sourceEditor()?.text() ?? this.session.file()?.text;
                if (text !== undefined) void this.parse(text);
            }, PARSE_DELAY_MS);
        });
        effect(() => {
            this.style();
            this.reading.linkFonts('code');
            untracked(() => {
                this.sourceEditor()?.remeasure();
                setTimeout(() =>
                    this.virtual.setRowHeight(
                        this.scroller()?.nativeElement.querySelector<HTMLElement>('.subtitle-cue')?.offsetHeight ?? 0,
                    ),
                );
            });
        });
    }

    private async load(item: LibraryItem, cancel: AbortSignal): Promise<void> {
        if (this.state() !== 'loading' || this.session.file()) return;
        let file: TextFile;
        try {
            file = await this.fileSource.textFile(item, cancel);
        } catch (error) {
            if (cancel.aborted) return;
            const failure = textFailure(error);
            this.offerDownload.set(failure.offerDownload);
            this.message.set(failure.message);
            this.state.set('failed');
            return;
        }
        if (cancel.aborted) return;
        this.session.attach(item, file);
        await this.parse(file.text);
        this.state.set('ready');
        if (this.mode() !== 'cues') this.editor();
        if (untracked(this.edit)) this.session.startEdit();
        // The video to play the cues in: only for a session that may watch videos.
        if (this.fileHost.can(Rights.viewFiles) && this.fileSource.library)
            this.api.subtitleVideo(item.path, cancel).then(
                (found) => this.video.set(found.path),
                () => undefined,
            );
    }

    private async parse(text: string): Promise<void> {
        const parse = ++this.parses;
        let document: SubtitleDocument;
        try {
            document = await parseSubtitles(text, subtitleTypeOf(this.item().name));
        } catch (error) {
            document = { cues: [], problems: [{ message: (error as Error).message, line: 1 }] };
        }
        if (parse === this.parses) this.document.set(document);
    }

    /** The source's editor, made now when it isn't yet. */
    private editor(): TextEditor | null {
        const existing = this.sourceEditor();
        const file = this.session.file();
        if (existing || !file) return existing;
        const { wrap, lineNumbers } = this.own();
        const created = new TextEditor(this.source().nativeElement, {
            text: file.text,
            name: this.item().name,
            wrap,
            lineNumbers,
            ...this.session.editorOptions(),
        });
        this.session.setEditor(created);
        this.sourceEditor.set(created);
        void created.setLanguage(this.item().name).catch(() => undefined);
        return created;
    }

    protected optionId(cue: SubtitleCue): string {
        return `${this.prefix}-${cue.index}`;
    }

    /** A cue's own id, or its number. */
    protected cueName(cue: SubtitleCue): string {
        return cue.id || String(cue.index + 1);
    }

    protected time(seconds: number): string {
        return formatTime(seconds, this.hours());
    }

    protected span(seconds: number): string {
        return formatSpan(seconds);
    }

    protected playUrl(cue: SubtitleCue): string | null {
        const video = this.video();
        return video ? (this.config.urls()?.player(video, Math.floor(cue.start)) ?? null) : null;
    }

    protected setMode(mode: SubtitlesMode): void {
        this.mode.set(mode);
        writePreference(SUBTITLES_MODE_KEY, mode);
        if (mode !== 'cues' && this.state() === 'ready') this.editor();
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
        if (cue.line !== null) this.showLine(cue.line);
    }

    protected showLine(line: number): void {
        if (this.mode() === 'cues') this.setMode('split');
        const editor = this.editor();
        setTimeout(() => {
            if (!editor) return;
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
        if (!editor || !this.session.editing()) return;
        if (milliseconds === null || milliseconds === 0) {
            this.toasts.show(shiftRefused(), { level: 'error' });
            box?.focus();
            return;
        }
        editor.edit(shiftEdits(editor.text(), milliseconds));
        this.toasts.show(shifted(milliseconds));
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
            if (this.selected() === null && first) this.selected.set(first.index);
        }
    }

    /** ↑ ↓, Page Up / Down, Home / End move between the cues; Enter shows the one selected in the source. */
    protected onListKeydown(event: KeyboardEvent): void {
        const order = this.order();
        if (!order.length || event.altKey || event.ctrlKey || event.metaKey) return;
        const current = order.findIndex((cue) => cue.index === this.selected());
        const page = Math.max(
            1,
            Math.floor((this.scroller()?.nativeElement.clientHeight ?? 0) / this.virtual.height) - 1,
        );
        let position = current;
        switch (event.key) {
            case 'ArrowDown':
                position = current < 0 ? 0 : current + 1;
                break;
            case 'ArrowUp':
                position = current < 0 ? 0 : current - 1;
                break;
            case 'PageDown':
                position = Math.max(0, current) + page;
                break;
            case 'PageUp':
                position = Math.max(0, current) - page;
                break;
            case 'Home':
                position = 0;
                break;
            case 'End':
                position = order.length - 1;
                break;
            case 'Enter':
                if (current >= 0) {
                    event.preventDefault();
                    this.showCue(order[current]);
                }
                return;
            default:
                return;
        }
        event.preventDefault();
        position = Math.max(0, Math.min(order.length - 1, position));
        this.selected.set(order[position].index);
        this.virtual.scrollTo(position);
    }

    /** Ctrl+= / Ctrl+− / Ctrl+0 inside the view: the text's size, rather than the whole page's zoom. */
    protected onKeydown(event: KeyboardEvent): void {
        if (!(event.ctrlKey || event.metaKey) || event.altKey || event.defaultPrevented) return;
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
        this.reading.changeSize('code', step);
    }

    /** Ctrl+F filters the cues when only they are shown, finds in the source otherwise; Ctrl+Alt+G goes to a line. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (this.session.handleKey(event)) return;
        if (event.defaultPrevented || this.state() !== 'ready' || !(event.ctrlKey || event.metaKey) || event.shiftKey)
            return;
        const key = event.key.toLowerCase();
        const editor = this.sourceEditor();
        if (key === 'f' && !event.altKey) {
            event.preventDefault();
            if (this.mode() === 'cues' || !editor) {
                this.filterBox()?.nativeElement.focus();
                this.filterBox()?.nativeElement.select();
            } else editor.find();
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
