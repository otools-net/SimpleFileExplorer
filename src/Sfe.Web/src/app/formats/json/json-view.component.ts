import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { copyText } from '../../core/copy-path.service';
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
import { MenuEntry } from '../../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../../menus/overflow-row';
import { findEntry, lineNumbersEntry, modesEntry, readingEntry } from '../toolbar-entries';
import { TextSession } from '../text/text-session';
import {
    CHUNK, expandAll, filterTree, formatEdits, JsonDocument, JsonRow, parseJson, pathLabel, previewOf, rowsOf, sizeOf, sourceOf, valueClass,
} from './json-tree';
import { FILE_SOURCE } from '../file-source';

export type JsonMode = 'tree' | 'source' | 'split';
export const JSON_MODE_KEY = 'sfe.json.mode';
/** What opens at once: a small file whole, a large one as far as this many rows. */
const OPEN_ROWS = 500;
/** How long typing pauses before the tree shows the source again, and the filter what is typed in it. */
const PARSE_DELAY_MS = 300;
const FILTER_DELAY_MS = 200;
const MODES: readonly { id: JsonMode; label: string; title: string }[] = [
    { id: 'tree', label: 'Tree', title: 'Its members, to open and close' },
    { id: 'source', label: 'Source', title: 'Its text, as written' },
    { id: 'split', label: 'Split', title: 'Both, side by side' },
];

let views = 0;

/**
 * A JSON file (agents/docs/features/json-viewer.md): a tree of its members (open and close, filter, the path and value
 * of the one selected), its source (CodeMirror, as the text view), or both side by side; the code reading preferences.
 * Parsed by `jsonc-parser`: in the order written, numbers as written, comments and trailing commas allowed. Edit
 * changes the source, the tree following as it is typed (agents/docs/features/text-editor.md).
 */
@Component({
    selector: 'sfe-json-view',
    imports: [IconComponent, ReadingPanelComponent, TextEditActionsComponent, TextEditBarComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    providers: [TextSession],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view json-view',
        'data-owns-keys': '',
        '[attr.data-reader-theme]': 'reading.preferences().theme',
        '[attr.data-mode]': 'mode()',
        '[style]': 'style()',
        '(keydown)': 'onKeydown($event)',
        '(window:keydown)': 'onWindowKeydown($event)',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Document" sfeOverflowRow="Document">
            @if (state() === 'ready') {
                <sfe-text-edit-actions [sfeOverflowItem]="null" overflowStay />
                @if (session.editing()) {
                    <button type="button" class="icon-button" data-format [sfeOverflowItem]="more.format" title="Format: lay the source out again, indented (comments kept)" aria-label="Format"
                            [disabled]="!document()?.root || !!problems().length" (click)="formatSource()"><svg sfeIcon="format"></svg></button>
                }
            }
            <div class="markdown-modes" role="radiogroup" aria-label="Show" [sfeOverflowItem]="more.mode">
                @for (choice of modes; track choice.id) {
                    <button type="button" role="radio" class="markdown-mode" [class.active]="mode() === choice.id" [attr.aria-checked]="mode() === choice.id"
                            [attr.data-mode]="choice.id" [title]="choice.title" (click)="setMode(choice.id)">{{ choice.label }}</button>
                }
            </div>
            @if (mode() !== 'source') {
                <input #filterBox type="search" class="json-filter" [sfeOverflowItem]="null" overflowStay placeholder="Filter keys and values" aria-label="Filter keys and values" title="Shows the members whose key or value holds this (Ctrl+F)"
                       [value]="query()" (input)="setQuery($any($event.target).value)" (keydown)="onFilterKeydown($event)" />
                <button type="button" class="icon-button" [sfeOverflowItem]="more.openAll" title="Open all" aria-label="Open all" data-expand-all [disabled]="!document()?.root" (click)="openAll()"><svg sfeIcon="unfold"></svg></button>
                <button type="button" class="icon-button" [sfeOverflowItem]="more.closeAll" title="Close all" aria-label="Close all" data-collapse-all [disabled]="!document()?.root" (click)="closeAll()"><svg sfeIcon="fold"></svg></button>
            }
            @if (mode() !== 'tree') {
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
        <div class="json-panes" [hidden]="state() !== 'ready'">
            <div class="json-pane" [hidden]="mode() === 'source'">
                @if (problems()[0]; as problem) {
                    <p class="json-problem" role="status" data-problem>
                        Not JSON at line {{ problem.line }}, column {{ problem.column }}: {{ problem.message }}{{ problems().length > 1 ? ' (and ' + (problems().length - 1) + ' more)' : '' }}.
                        <button type="button" class="link-button" (click)="showOffset(problem.offset, problem.offset + problem.length)">Show in the source</button>
                    </p>
                }
                @if (filtered(); as filter) {
                    <p class="json-filter-count" role="status">{{ filter.count ? (filter.limited ? 'The first ' + filter.count.toLocaleString() + ' matches' : filter.count.toLocaleString() + (filter.count === 1 ? ' match' : ' matches')) : 'Nothing matches' }}</p>
                }
                <div #tree class="json-tree" role="tree" tabindex="0" [attr.aria-label]="'Members of ' + item().name" [attr.aria-activedescendant]="activeId()"
                     (keydown)="onTreeKeydown($event)">
                    @for (row of rows(); track row.id; let index = $index) {
                        @if (row.more; as more) {
                            <div class="json-row json-more" [style.--depth]="row.depth">
                                <span class="json-toggle"></span>
                                <button type="button" class="link-button" (click)="showMore(row)">Show {{ nextChunk(more).toLocaleString() }} more</button>
                                <span class="json-size">{{ more.shown.toLocaleString() }} of {{ more.total.toLocaleString() }} shown</span>
                            </div>
                        } @else {
                            <div class="json-row" role="treeitem" [id]="domId(index)" [style.--depth]="row.depth" [class.selected]="row.id === selected()"
                                 [attr.aria-level]="row.depth + 1" [attr.aria-expanded]="row.container ? row.expanded : null" [attr.aria-selected]="row.id === selected()"
                                 [attr.data-path]="row.id" (click)="select(row)" (dblclick)="showInSource(row)">
                                @if (row.container) {
                                    <button type="button" class="json-toggle" tabindex="-1" [class.open]="row.expanded" [attr.aria-label]="row.expanded ? 'Close' : 'Open'"
                                            (click)="toggle(row, $event)"><svg sfeIcon="right"></svg></button>
                                } @else {
                                    <span class="json-toggle"></span>
                                }
                                @if (row.key !== null) {
                                    <span class="json-key" [class.tok-propertyName]="!row.index" [class.json-index]="row.index">{{ row.key }}</span><span class="json-colon">:</span>
                                }
                                @if (row.container) {
                                    @if (row.expanded) {
                                        <span class="json-size">{{ size(row) }}</span>
                                    } @else {
                                        <span class="json-preview">{{ preview(row) }}</span><span class="json-size">{{ size(row) }}</span>
                                    }
                                } @else {
                                    <span class="json-value" [class]="valueClass(row)">{{ value(row) }}</span>
                                }
                            </div>
                        }
                    }
                </div>
            </div>
            <div #source class="text-editor json-source" [hidden]="mode() === 'tree'"></div>
        </div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                @if (session.dirty()) {
                    <span class="text-edited" data-edited>Edited</span>
                }
                @if (mode() !== 'source' && selectedRow(); as row) {
                    <span class="json-selection">
                        <span class="json-path" data-selected-path [title]="path(row)">{{ path(row) }}</span>
                        <button type="button" class="link-button" data-copy-path title="Copy the path of the member selected" (click)="copyPath(row)">Copy path</button>
                        <button type="button" class="link-button" data-copy-value title="Copy its value, as written in the file" (click)="copyValue(row)">Copy value</button>
                    </span>
                }
            </footer>
        }
    `,
})
export class JsonViewComponent {
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
    private readonly tree = viewChild<ElementRef<HTMLElement>>('tree');
    private readonly filterBox = viewChild<ElementRef<HTMLInputElement>>('filterBox');
    private readonly source = viewChild.required<ElementRef<HTMLElement>>('source');
    private readonly prefix = `json-${++views}`;

    protected readonly modes = MODES;
    protected readonly mode = signal<JsonMode>(readMode());
    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly offerDownload = signal(false);
    protected readonly panelOpen = signal(false);
    /** The document as last parsed (as loaded, or as typed). */
    protected readonly document = signal<JsonDocument | null>(null);
    protected readonly expanded = signal<ReadonlySet<string>>(new Set());
    private readonly shown = signal<ReadonlyMap<string, number>>(new Map());
    protected readonly query = signal('');
    protected readonly filtered = signal<ReturnType<typeof filterTree> | null>(null);
    protected readonly selected = signal<string | null>(null);
    /** The source's editor, made the first time the source is shown. */
    protected readonly sourceEditor = signal<TextEditor | null>(null);
    private parseLater: ReturnType<typeof setTimeout> | undefined;
    private filterLater: ReturnType<typeof setTimeout> | undefined;

    protected readonly own = computed(() => this.reading.preferences().code);
    /** The toolbar's buttons in its "⋯" menu, when they don't fit (the editing actions and the fields stay). */
    protected readonly more = {
        format: (): MenuEntry => ({ id: 'format', label: 'Format', icon: 'format', title: 'Format: lay the source out again, indented (comments kept)', disabled: !this.document()?.root || !!this.problems().length, run: () => this.formatSource() }),
        mode: (): MenuEntry => modesEntry(this.modes, this.mode(), mode => this.setMode(mode)),
        openAll: (): MenuEntry => ({ id: 'openAll', label: 'Open all', icon: 'unfold', title: 'Open all', disabled: !this.document()?.root, run: () => this.openAll() }),
        closeAll: (): MenuEntry => ({ id: 'closeAll', label: 'Close all', icon: 'fold', title: 'Close all', disabled: !this.document()?.root, run: () => this.closeAll() }),
        lineNumbers: (): MenuEntry => lineNumbersEntry(this.own().lineNumbers, () => this.toggleLineNumbers()),
        find: (): MenuEntry => findEntry(this.state() !== 'ready', () => this.sourceEditor()?.find()),
        reading: (): MenuEntry => readingEntry(this.panelOpen(), () => this.panelOpen.set(!this.panelOpen()), "Reading settings: font, size, colours"),
    };
    protected readonly style = computed(() => this.reading.style('code'));
    protected readonly problems = computed(() => this.document()?.problems ?? []);
    protected readonly rows = computed(() => {
        const document = this.document();
        return document ? this.rowsOf(document) : [];
    });
    protected readonly selectedRow = computed(() => {
        const id = this.selected();
        return id === null ? null : this.rows().find(row => row.id === id && !row.more) ?? null;
    });
    protected readonly activeId = computed(() => {
        const id = this.selected();
        const index = id === null ? -1 : this.rows().findIndex(row => row.id === id);
        return index >= 0 ? this.domId(index) : null;
    });
    protected readonly downloadUrl = computed(() =>
        this.offerDownload() && this.fileHost.can(Rights.download) ? this.fileSource.download(this.item()) : null);
    protected readonly details = computed(() => {
        const file = this.session.file();
        const document = this.document();
        return file ? textDetails(file, document && !document.strict && !document.problems.length ? 'JSON with comments' : 'JSON') : '';
    });

    constructor() {
        const abort = new AbortController();
        inject(DestroyRef).onDestroy(() => {
            abort.abort();
            clearTimeout(this.parseLater);
            clearTimeout(this.filterLater);
            this.sourceEditor()?.destroy();
        });
        effect(() => {
            const item = this.item();
            untracked(() => void this.load(item, abort.signal));
        });
        // The source's editor, once it is shown, and the code preferences on it.
        effect(() => {
            const ready = this.state() === 'ready';
            const mode = this.mode();
            const { wrap, lineNumbers } = this.own();
            untracked(() => {
                if (ready && mode !== 'tree')
                    this.editor()?.configure({ wrap, lineNumbers });
            });
        });
        // Editing needs the source: beside the tree, when only the tree was shown.
        effect(() => {
            if (this.session.editing() && untracked(this.mode) === 'tree')
                untracked(() => this.mode.set('split'));
        });
        // The tree follows the source as it is typed, once typing pauses.
        effect(() => {
            if (!this.session.revision())
                return;
            clearTimeout(this.parseLater);
            this.parseLater = setTimeout(() => {
                // The source as typed; read again (changed elsewhere) without the source shown: the file's.
                const text = this.sourceEditor()?.text() ?? this.session.file()?.text;
                if (text !== undefined)
                    this.parse(text, false);
            }, PARSE_DELAY_MS);
        });
        effect(() => {
            this.style();
            this.reading.linkFonts('code');
            untracked(() => this.sourceEditor()?.remeasure());
        });
    }

    private async load(item: LibraryItem, cancel: AbortSignal): Promise<void> {
        if (this.state() !== 'loading' || this.session.file())
            return;
        let file: TextFile;
        try {
            file = await this.fileSource.textFile(item, cancel);
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
        this.parse(file.text, true);
        this.state.set('ready');
        if (this.mode() !== 'tree')
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
        const created = new TextEditor(this.source().nativeElement, { text: file.text, name: 'source.json', wrap, lineNumbers, ...this.session.editorOptions() });
        this.session.setEditor(created);
        this.sourceEditor.set(created);
        void created.setLanguage('source.json').catch(() => undefined);
        return created;
    }

    /** Parses `text`; the first time, opens as much as shows at once. The filter, when there is one, runs again. */
    private parse(text: string, first: boolean): void {
        const document = parseJson(text);
        this.document.set(document);
        if (first) {
            const open = expandAll(document.root, OPEN_ROWS);
            if (document.root)
                open.add('');
            this.expanded.set(open);
            this.selected.set(document.root ? '' : null);
        }
        if (this.query().trim())
            this.filtered.set(filterTree(document.root, this.query()));
    }

    private rowsOf(document: JsonDocument): JsonRow[] {
        return rowsOf(document.root, { expanded: this.expanded(), shown: this.shown(), keep: this.filtered()?.keep ?? null });
    }

    protected domId(index: number): string {
        return `${this.prefix}-${index}`;
    }

    protected size(row: JsonRow): string {
        return sizeOf(row.node);
    }

    protected preview(row: JsonRow): string {
        return previewOf(row.node, this.document()!.text);
    }

    protected value(row: JsonRow): string {
        return sourceOf(row.node, this.document()!.text);
    }

    protected valueClass(row: JsonRow): string {
        return `json-value ${valueClass(row.node)}`;
    }

    protected path(row: JsonRow): string {
        return pathLabel(row.id);
    }

    protected nextChunk(more: { shown: number; total: number }): number {
        return Math.min(CHUNK, more.total - more.shown);
    }

    protected setMode(mode: JsonMode): void {
        this.mode.set(mode);
        writePreference(JSON_MODE_KEY, mode);
        if (mode !== 'tree' && this.state() === 'ready')
            this.editor();
    }

    protected toggleLineNumbers(): void {
        this.reading.update('code', { lineNumbers: !this.own().lineNumbers });
    }

    protected select(row: JsonRow): void {
        this.selected.set(row.id);
        this.tree()?.nativeElement.focus({ preventScroll: true });
    }

    protected toggle(row: JsonRow, event?: Event): void {
        event?.stopPropagation();
        this.selected.set(row.id);
        this.setOpen(row, !row.expanded);
    }

    private setOpen(row: JsonRow, open: boolean): void {
        this.expanded.update(expanded => {
            const next = new Set(expanded);
            if (open)
                next.add(row.id);
            else
                next.delete(row.id);
            return next;
        });
    }

    protected showMore(row: JsonRow): void {
        const id = row.id.slice(0, -'#more'.length);
        this.shown.update(shown => new Map(shown).set(id, (row.more?.shown ?? CHUNK) + CHUNK));
    }

    /** Opens everything under the member selected (the whole tree from the root), as far as a few thousand rows. */
    protected openAll(): void {
        const document = this.document();
        if (!document)
            return;
        const from = this.selectedRow()?.container ? this.selected()! : '';
        const open = expandAll(document.root, undefined, from);
        this.expanded.update(expanded => new Set([...expanded, ...open]));
    }

    protected closeAll(): void {
        this.expanded.set(new Set(['']));
        this.shown.set(new Map());
        this.selected.set(this.document()?.root ? '' : null);
    }

    protected setQuery(query: string): void {
        this.query.set(query);
        clearTimeout(this.filterLater);
        this.filterLater = setTimeout(() => {
            const document = this.document();
            this.filtered.set(query.trim() && document ? filterTree(document.root, query) : null);
        }, FILTER_DELAY_MS);
    }

    protected onFilterKeydown(event: KeyboardEvent): void {
        if (event.key === 'Escape' && this.query()) {
            event.preventDefault();
            event.stopPropagation();
            this.setQuery('');
        } else if (event.key === 'Enter' || event.key === 'ArrowDown') {
            event.preventDefault();
            this.tree()?.nativeElement.focus();
            if (this.selected() === null && this.rows().length)
                this.selected.set(this.rows()[0].id);
        }
    }

    /** The member in the source, selected (a property with its key), beside the tree when only the tree is shown. */
    protected showInSource(row: JsonRow): void {
        const node = row.node.parent?.type === 'property' ? row.node.parent : row.node;
        this.showOffset(node.offset, node.offset + node.length);
    }

    protected showOffset(from: number, to: number): void {
        if (this.mode() === 'tree')
            this.setMode('split');
        const editor = this.editor();
        // Once shown: a hidden editor has nothing to scroll.
        setTimeout(() => {
            editor?.select(from, to);
            editor?.focus();
        });
    }

    protected async copyPath(row: JsonRow): Promise<void> {
        await copyText(pathLabel(row.id));
        this.toasts.show('Path copied');
    }

    protected async copyValue(row: JsonRow): Promise<void> {
        const text = this.document()!.text;
        await copyText(text.slice(row.node.offset, row.node.offset + row.node.length));
        this.toasts.show('Value copied');
    }

    /** Lays the source out again (an edit: undone with Ctrl+Z). */
    protected formatSource(): void {
        const editor = this.sourceEditor();
        if (!editor || !this.session.editing())
            return;
        editor.edit(formatEdits(editor.text()));
    }

    /** Arrows move and open as in a tree of files; Enter shows the member in the source, `*` opens all under it. */
    protected onTreeKeydown(event: KeyboardEvent): void {
        if (event.ctrlKey || event.metaKey || event.altKey)
            return;
        const rows = this.rows().filter(row => !row.more);
        if (!rows.length)
            return;
        let index = rows.findIndex(row => row.id === this.selected());
        const row = index >= 0 ? rows[index] : null;
        switch (event.key) {
            case 'ArrowDown': index = Math.min(rows.length - 1, index + 1); break;
            case 'ArrowUp': index = Math.max(0, index - 1); break;
            case 'Home': index = 0; break;
            case 'End': index = rows.length - 1; break;
            case 'PageDown': index = Math.min(rows.length - 1, index + 20); break;
            case 'PageUp': index = Math.max(0, index - 20); break;
            case 'ArrowRight':
                if (row?.container && !row.expanded)
                    this.setOpen(row, true);
                else if (row?.container)
                    index++;
                break;
            case 'ArrowLeft':
                if (row?.container && row.expanded)
                    this.setOpen(row, false);
                else if (row)
                    index = rows.findIndex(other => other.id === row.id.slice(0, row.id.lastIndexOf('/')));
                break;
            case ' ':
                if (row?.container)
                    this.setOpen(row, !row.expanded);
                break;
            case 'Enter':
                if (row)
                    this.showInSource(row);
                break;
            case '*':
                this.openAll();
                break;
            default:
                return;
        }
        event.preventDefault();
        const next = this.rows().filter(shown => !shown.more)[Math.max(0, Math.min(index, rows.length - 1))] ?? rows[0];
        this.selected.set(next.id);
        setTimeout(() => {
            const active = this.activeId();
            const element = active ? this.tree()?.nativeElement.querySelector(`#${CSS.escape(active)}`) : null;
            element?.scrollIntoView?.({ block: 'nearest' });
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

    /** Ctrl+F filters the tree when only it is shown, finds in the source otherwise; Ctrl+Alt+G goes to a line. */
    protected onWindowKeydown(event: KeyboardEvent): void {
        if (this.session.handleKey(event))
            return;
        if (event.defaultPrevented || this.state() !== 'ready' || !(event.ctrlKey || event.metaKey) || event.shiftKey)
            return;
        const key = event.key.toLowerCase();
        const editor = this.sourceEditor();
        if (key === 'f' && !event.altKey) {
            event.preventDefault();
            if (this.mode() === 'tree' || !editor) {
                this.filterBox()?.nativeElement.focus();
                this.filterBox()?.nativeElement.select();
            } else
                editor.find();
        } else if (key === 'g' && event.altKey && editor && this.mode() !== 'tree') {
            event.preventDefault();
            editor.goToLine();
        }
    }
}

function readMode(): JsonMode {
    const stored = readPreference(JSON_MODE_KEY);
    return stored === 'source' || stored === 'split' ? stored : 'tree';
}
