import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { copyText } from '../../core/copy-path.service';
import { LibraryItem, TextFile } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { extensionOf, FILE_VIEW_HOST, FileFormat } from '../formats';
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
    columnsOf, CsvDelimiter, CsvTable, delimiterFor, delimiterName, DELIMITERS, filterRecords, parseCsv, shownCell, SortDirection, sortRecords,
} from './csv-table';

export type CsvMode = 'table' | 'source' | 'split';
export const CSV_MODE_KEY = 'sfe.csv.mode';
export const CSV_HEADER_KEY = 'sfe.csv.header';
/** How long typing pauses before the table shows the source again, and the filter what is typed in it. */
const PARSE_DELAY_MS = 300;
const FILTER_DELAY_MS = 200;
const MODES: readonly { id: CsvMode; label: string; title: string }[] = [
    { id: 'table', label: 'Table', title: 'Its rows and columns' },
    { id: 'source', label: 'Source', title: 'Its text, as written' },
    { id: 'split', label: 'Split', title: 'Both, side by side' },
];

/** The cell selected: a record (its position in the file's rows) and a column. */
interface CellPosition {
    readonly record: number;
    readonly column: number;
}

/**
 * A CSV or TSV file (agents/docs/features/csv-viewer.md): a table (papaparse; the first row as header or not, the
 * separator guessed or chosen), sorted by a column, filtered, only the rows shown drawn (`VirtualRows`); its source
 * (CodeMirror), or both side by side; the code reading preferences. Edit changes the source, the table following as
 * it is typed (agents/docs/features/text-editor.md).
 */
@Component({
    selector: 'sfe-csv-view',
    imports: [IconComponent, ReadingPanelComponent, TextEditActionsComponent, TextEditBarComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    providers: [TextSession],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view csv-view',
        'data-owns-keys': '',
        '[attr.data-reader-theme]': 'reading.preferences().theme',
        '[attr.data-mode]': 'mode()',
        '[style]': 'style()',
        '(keydown)': 'onKeydown($event)',
        '(window:keydown)': 'onWindowKeydown($event)',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Table" sfeOverflowRow="Table">
            @if (state() === 'ready') {
                <sfe-text-edit-actions [sfeOverflowItem]="null" overflowStay />
            }
            <div class="markdown-modes" role="radiogroup" aria-label="Show" [sfeOverflowItem]="more.mode">
                @for (choice of modes; track choice.id) {
                    <button type="button" role="radio" class="markdown-mode" [class.active]="mode() === choice.id" [attr.aria-checked]="mode() === choice.id"
                            [attr.data-mode]="choice.id" [title]="choice.title" (click)="setMode(choice.id)">{{ choice.label }}</button>
                }
            </div>
            @if (mode() !== 'source') {
                <input #filterBox type="search" class="json-filter" [sfeOverflowItem]="null" overflowStay placeholder="Filter rows" aria-label="Filter rows" title="Shows the rows with a cell holding this (Ctrl+F)"
                       [value]="query()" (input)="setQuery($any($event.target).value)" (keydown)="onFilterKeydown($event)" />
                <button type="button" class="markdown-mode csv-header-toggle" data-header [sfeOverflowItem]="more.header" [class.active]="header()" [attr.aria-pressed]="header()"
                        title="The first row names the columns" (click)="toggleHeader()">Header</button>
                <select class="csv-delimiter" data-delimiter [sfeOverflowItem]="more.separator" aria-label="Separator" title="What separates the cells" [value]="delimiter()" (change)="setDelimiter($any($event.target).value)">
                    @for (choice of delimiters; track choice.id) {
                        <option [value]="choice.id">{{ choice.id === 'auto' ? 'Separator: guess' : choice.label }}</option>
                    }
                </select>
            }
            @if (mode() !== 'table') {
                <button type="button" class="icon-button" [sfeOverflowItem]="more.lineNumbers" [class.active]="own().lineNumbers" [attr.aria-pressed]="own().lineNumbers" title="Line numbers" aria-label="Line numbers" (click)="toggleLineNumbers()"><svg sfeIcon="line-numbers"></svg></button>
                <button type="button" class="icon-button" [sfeOverflowItem]="more.find" title="Find in the source (Ctrl+F)" aria-label="Find" [disabled]="state() !== 'ready'" (click)="sourceEditor()?.find()"><svg sfeIcon="search"></svg></button>
            }
            <button type="button" class="icon-button reading-toggle" data-reading-toggle [sfeOverflowItem]="more.reading" [class.active]="panelOpen()" [attr.aria-expanded]="panelOpen()"
                    title="Reading settings: font, size, colours" aria-label="Reading settings" (click)="panelOpen.set(!panelOpen())">Aa</button>
            <button class="icon-button" sfeOverflowMore></button>
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
        <div class="json-panes csv-panes" [hidden]="state() !== 'ready'">
            <div class="json-pane" [hidden]="mode() === 'source'">
                @if (problems()[0]; as problem) {
                    <p class="json-problem" role="status" data-problem>
                        Record {{ problem.record.toLocaleString() }}: {{ problem.message }}{{ problems().length > 1 ? ' (and ' + (problems().length - 1) + ' more)' : '' }}.
                    </p>
                }
                <div #scroller class="csv-scroller" role="table" tabindex="0" [attr.aria-label]="item().name" [attr.aria-rowcount]="order().length + 1"
                     [attr.aria-colcount]="columns().length" (keydown)="onTableKeydown($event)">
                    <div #head class="csv-row csv-head" role="row" aria-rowindex="1" [style.grid-template-columns]="template()">
                        <span class="csv-number" role="columnheader">#</span>
                        @for (column of columns(); track $index) {
                            <span role="columnheader" class="csv-cell" [class.numeric]="column.numeric" [attr.aria-sort]="sortOf($index) ?? 'none'">
                                <button type="button" class="csv-sort" [attr.data-column]="$index" [title]="'Sort by ' + column.name" (click)="sortBy($index)">
                                    <span class="csv-name">{{ column.name }}</span>
                                    @if (sortOf($index); as direction) {
                                        <svg class="csv-sort-mark" [class.descending]="direction === 'descending'" sfeIcon="up"></svg>
                                    }
                                </button>
                            </span>
                        }
                    </div>
                    <div class="csv-body" [style.height.px]="virtual.total()">
                        @for (shown of virtual.items(); track shown.key) {
                            @let record = order()[shown.index];
                            @if (record !== undefined) {
                                <div class="csv-row" role="row" [attr.aria-rowindex]="shown.index + 2" [attr.data-record]="record" [style.grid-template-columns]="template()"
                                     [style.transform]="'translateY(' + shown.start + 'px)'" [style.height.px]="virtual.height">
                                    <span class="csv-number" role="rowheader">{{ record - first() + 1 }}</span>
                                    @for (column of columns(); track $index) {
                                        <span role="cell" class="csv-cell" [attr.data-column]="$index" [class.numeric]="column.numeric" [class.selected]="selected()?.record === record && selected()?.column === $index"
                                              (click)="select(record, $index)">{{ cell(record, $index) }}</span>
                                    }
                                </div>
                            }
                        }
                    </div>
                    @if (!order().length) {
                        <p class="csv-empty">{{ query().trim() ? 'No row holds “' + query().trim() + '”.' : 'No rows.' }}</p>
                    }
                </div>
            </div>
            <div #source class="text-editor json-source" [hidden]="mode() === 'table'"></div>
        </div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                @if (session.dirty()) {
                    <span class="text-edited" data-edited>Edited</span>
                }
                <span class="json-selection">
                    <span data-counts>{{ counts() }}</span>
                    @if (mode() !== 'source' && selectedValue(); as value) {
                        <span class="json-path" data-selected-cell [title]="value.text">{{ value.label }}</span>
                        <button type="button" class="link-button" data-copy-cell title="Copy the cell selected (Ctrl+C)" (click)="copyCell()">Copy cell</button>
                    }
                </span>
            </footer>
        }
    `,
})
export class CsvViewComponent {
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
    private readonly head = viewChild<ElementRef<HTMLElement>>('head');
    private readonly filterBox = viewChild<ElementRef<HTMLInputElement>>('filterBox');
    private readonly source = viewChild.required<ElementRef<HTMLElement>>('source');

    protected readonly modes = MODES;
    protected readonly delimiters = DELIMITERS;
    protected readonly virtual = new VirtualRows(() => this.scroller()?.nativeElement ?? null);
    protected readonly mode = signal<CsvMode>(readMode());
    protected readonly header = signal(readPreference(CSV_HEADER_KEY) !== '0');
    protected readonly delimiter = signal<CsvDelimiter>('auto');
    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly offerDownload = signal(false);
    protected readonly panelOpen = signal(false);
    /** The text as last parsed (as loaded, or as typed). */
    private readonly text = signal<string | null>(null);
    protected readonly query = signal('');
    /** The filter as applied (a moment after typing). */
    private readonly applied = signal('');
    protected readonly sort = signal<{ column: number; direction: SortDirection } | null>(null);
    protected readonly selected = signal<CellPosition | null>(null);
    /** The source's editor, made the first time the source is shown. */
    protected readonly sourceEditor = signal<TextEditor | null>(null);
    private parseLater: ReturnType<typeof setTimeout> | undefined;
    private filterLater: ReturnType<typeof setTimeout> | undefined;

    protected readonly own = computed(() => this.reading.preferences().code);
    /** The toolbar's buttons in its "⋯" menu, when they don't fit (the editing actions and the fields stay). */
    protected readonly more = {
        mode: (): MenuEntry => modesEntry(this.modes, this.mode(), mode => this.setMode(mode)),
        header: (): MenuEntry => ({ id: 'header', label: 'Header', icon: 'rows', title: 'The first row names the columns', checked: this.header(), run: () => this.toggleHeader() }),
        separator: (): MenuEntry => ({
            id: 'separator', label: 'Separator', icon: 'rows', title: 'What separates the cells',
            submenu: () => this.delimiters.map(choice => ({
                id: 'separator-' + choice.label.toLowerCase(), label: choice.id === 'auto' ? 'Guess' : choice.label, icon: 'rows', title: 'Cells separated by: ' + choice.label,
                checked: choice.id === this.delimiter(), run: () => this.setDelimiter(choice.id),
            })),
        }),
        lineNumbers: (): MenuEntry => lineNumbersEntry(this.own().lineNumbers, () => this.toggleLineNumbers()),
        find: (): MenuEntry => findEntry(this.state() !== 'ready', () => this.sourceEditor()?.find()),
        reading: (): MenuEntry => readingEntry(this.panelOpen(), () => this.panelOpen.set(!this.panelOpen()), "Reading settings: font, size, colours"),
    };
    protected readonly style = computed(() => this.reading.style('code'));
    protected readonly table = computed<CsvTable | null>(() => {
        const text = this.text();
        return text === null ? null : parseCsv(text, this.delimiter());
    });
    protected readonly problems = computed(() => this.table()?.problems ?? []);
    /** The first record that isn't the header. */
    protected readonly first = computed(() => this.header() && this.table()?.rows.length ? 1 : 0);
    protected readonly columns = computed(() => {
        const table = this.table();
        return table ? columnsOf(table.rows, this.header()) : [];
    });
    /** The records shown, in the order shown: filtered, then sorted. */
    protected readonly order = computed(() => {
        const table = this.table();
        if (!table)
            return [];
        const records = filterRecords(table.rows, this.first(), this.applied());
        const sort = this.sort();
        const column = sort ? this.columns()[sort.column] : null;
        return sort && column ? sortRecords(records, table.rows, sort.column, column.numeric, sort.direction) : records;
    });
    protected readonly template = computed(() => {
        const digits = String(this.table()?.rows.length ?? 0).length;
        // Characters, plus the cells' padding (16px) and border.
        return [`calc(${digits + 1}ch + 17px)`, ...this.columns().map(column => `calc(${column.width}ch + 17px)`)].join(' ');
    });
    protected readonly counts = computed(() => {
        const table = this.table();
        if (!table)
            return '';
        const total = table.rows.length - this.first();
        const shown = this.order().length;
        const rows = this.applied().trim() ? `${shown.toLocaleString()} of ${total.toLocaleString()} rows` : `${total.toLocaleString()} ${total === 1 ? 'row' : 'rows'}`;
        const columns = this.columns().length;
        return `${rows} × ${columns} ${columns === 1 ? 'column' : 'columns'} · ${delimiterName(table.delimiter)}`;
    });
    protected readonly selectedValue = computed(() => {
        const position = this.selected();
        const table = this.table();
        const column = position ? this.columns()[position.column] : null;
        if (!position || !table || !column || position.record >= table.rows.length)
            return null;
        const text = table.rows[position.record][position.column] ?? '';
        return { text, label: `Row ${(position.record - this.first() + 1).toLocaleString()} · ${column.name}: ${shownCell(text).slice(0, 60)}` };
    });
    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const file = this.session.file();
        return file ? textDetails(file, extensionOf(this.item()) === 'tsv' ? 'TSV' : 'CSV') : '';
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
        // As many rows as shown; the table follows its scroll once it is in the page.
        effect(() => {
            const count = this.order().length;
            const ready = this.state() === 'ready';
            untracked(() => {
                this.virtual.setCount(count);
                if (ready)
                    this.virtual.mount();
            });
        });
        // The source's editor, once it is shown, and the code preferences on it.
        effect(() => {
            const ready = this.state() === 'ready';
            const mode = this.mode();
            const { wrap, lineNumbers } = this.own();
            untracked(() => {
                if (ready && mode !== 'table')
                    this.editor()?.configure({ wrap, lineNumbers });
            });
        });
        // Editing needs the source: beside the table, when only the table was shown.
        effect(() => {
            if (this.session.editing() && untracked(this.mode) === 'table')
                untracked(() => this.mode.set('split'));
        });
        // The table follows the source as it is typed, once typing pauses.
        effect(() => {
            if (!this.session.revision())
                return;
            clearTimeout(this.parseLater);
            this.parseLater = setTimeout(() => {
                // The source as typed; read again (changed elsewhere) without the source shown: the file's.
                const text = this.sourceEditor()?.text() ?? this.session.file()?.text;
                if (text !== undefined)
                    this.text.set(text);
            }, PARSE_DELAY_MS);
        });
        // The font, size or spacing: the editor and the rows measured again.
        effect(() => {
            this.style();
            this.reading.linkFonts('code');
            untracked(() => {
                this.sourceEditor()?.remeasure();
                setTimeout(() => this.virtual.setRowHeight(this.head()?.nativeElement.offsetHeight ?? 0));
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
        this.delimiter.set(delimiterFor(extensionOf(item)));
        this.text.set(file.text);
        this.state.set('ready');
        if (this.mode() !== 'table')
            this.editor();
        if (untracked(this.edit))
            this.session.startEdit();
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
        return created;
    }

    protected cell(record: number, column: number): string {
        return shownCell(this.table()!.rows[record]?.[column]);
    }

    protected sortOf(column: number): SortDirection | null {
        const sort = this.sort();
        return sort?.column === column ? sort.direction : null;
    }

    /** Sorts by `column`: ascending, then descending, then as in the file. */
    protected sortBy(column: number): void {
        const current = this.sortOf(column);
        this.sort.set(current === null ? { column, direction: 'ascending' } : current === 'ascending' ? { column, direction: 'descending' } : null);
        this.virtual.scrollTo(0, 'start');
    }

    protected setMode(mode: CsvMode): void {
        this.mode.set(mode);
        writePreference(CSV_MODE_KEY, mode);
        if (mode !== 'table' && this.state() === 'ready')
            this.editor();
    }

    protected toggleHeader(): void {
        this.header.update(header => !header);
        writePreference(CSV_HEADER_KEY, this.header() ? '1' : '0');
        this.sort.set(null);
        this.selected.set(null);
    }

    protected setDelimiter(delimiter: CsvDelimiter): void {
        this.delimiter.set(delimiter);
        this.sort.set(null);
        this.selected.set(null);
    }

    protected toggleLineNumbers(): void {
        this.reading.update('code', { lineNumbers: !this.own().lineNumbers });
    }

    protected select(record: number, column: number): void {
        this.selected.set({ record, column });
        this.scroller()?.nativeElement.focus({ preventScroll: true });
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
            if (!this.selected() && first !== undefined)
                this.selected.set({ record: first, column: 0 });
        }
    }

    protected async copyCell(): Promise<void> {
        const value = this.selectedValue();
        if (!value)
            return;
        await copyText(value.text);
        this.toasts.show('Cell copied');
    }

    /** Arrows move between cells, Home / End to the row's ends (with Ctrl, the table's), Page Up / Down by a screen. */
    protected onTableKeydown(event: KeyboardEvent): void {
        const order = this.order();
        const columns = this.columns().length;
        if (!order.length || !columns || event.altKey)
            return;
        const key = event.key;
        if ((event.ctrlKey || event.metaKey) && key.toLowerCase() === 'c') {
            if (this.selectedValue() && !window.getSelection()?.toString()) {
                event.preventDefault();
                void this.copyCell();
            }
            return;
        }
        const current = this.selected();
        let row = current ? Math.max(0, order.indexOf(current.record)) : 0;
        let column = current?.column ?? 0;
        const page = Math.max(1, Math.floor((this.scroller()?.nativeElement.clientHeight ?? 0) / this.virtual.height) - 1);
        const ctrl = event.ctrlKey || event.metaKey;
        switch (key) {
            case 'ArrowDown': row++; break;
            case 'ArrowUp': row--; break;
            case 'ArrowRight': column++; break;
            case 'ArrowLeft': column--; break;
            case 'PageDown': row += page; break;
            case 'PageUp': row -= page; break;
            case 'Home': if (ctrl) row = 0; column = 0; break;
            case 'End': if (ctrl) row = order.length - 1; column = columns - 1; break;
            default: return;
        }
        event.preventDefault();
        // Nothing selected yet: the arrows and pages start at the first cell.
        if (!current && (key.startsWith('Arrow') || key.startsWith('Page'))) {
            row = 0;
            column = 0;
        }
        row = Math.max(0, Math.min(order.length - 1, row));
        column = Math.max(0, Math.min(columns - 1, column));
        this.selected.set({ record: order[row], column });
        this.virtual.scrollTo(row);
        setTimeout(() => {
            const cell = this.scroller()?.nativeElement.querySelector(`[data-record="${order[row]}"] [data-column="${column}"]`);
            cell?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
        });
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

    /** Ctrl+F filters the table when only it is shown, finds in the source otherwise; Ctrl+Alt+G goes to a line. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (this.session.handleKey(event))
            return;
        if (event.defaultPrevented || this.state() !== 'ready' || !(event.ctrlKey || event.metaKey) || event.shiftKey)
            return;
        const key = event.key.toLowerCase();
        const editor = this.sourceEditor();
        if (key === 'f' && !event.altKey) {
            event.preventDefault();
            if (this.mode() === 'table' || !editor) {
                this.filterBox()?.nativeElement.focus();
                this.filterBox()?.nativeElement.select();
            } else
                editor.find();
        } else if (key === 'g' && event.altKey && editor && this.mode() !== 'table') {
            event.preventDefault();
            editor.goToLine();
        }
    }
}

function readMode(): CsvMode {
    const stored = readPreference(CSV_MODE_KEY);
    return stored === 'source' || stored === 'split' ? stored : 'table';
}
