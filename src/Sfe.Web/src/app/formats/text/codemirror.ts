import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { bracketMatching, syntaxHighlighting } from '@codemirror/language';
import { gotoLine, highlightSelectionMatches, openSearchPanel, search, searchKeymap } from '@codemirror/search';
import { Compartment, EditorState, Extension, Text } from '@codemirror/state';
import {
    drawSelection, EditorView, highlightActiveLine, highlightActiveLineGutter, highlightSpecialChars, keymap, lineNumbers,
    ViewPlugin, ViewUpdate,
} from '@codemirror/view';
import { classHighlighter } from '@lezer/highlight';
import { languageFor } from './languages';

/**
 * CodeMirror's parts in the reader's colours: variables of `styles/reader.css` (a theme of CodeMirror's own outranks
 * its base styles, which global CSS would have to out-specify).
 */
const readerTheme = EditorView.theme({
    '&': { height: '100%', color: 'var(--reader-text)', backgroundColor: 'var(--reader-bg)' },
    '.cm-scroller': { fontFamily: 'var(--reader-font)', fontSize: 'var(--reader-size)', lineHeight: 'var(--reader-line-height)' },
    // Margins all around; a longest line (when lines wrap), centred. With no width the calc() is invalid: no maximum.
    '.cm-content': {
        caretColor: 'var(--reader-text)',
        padding: 'var(--reader-padding)',
        maxWidth: 'calc(var(--reader-width) + 2 * var(--reader-padding))',
        marginInline: 'auto',
    },
    '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--reader-text)' },
    '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': { backgroundColor: 'var(--reader-selection)' },
    '.cm-gutters': { backgroundColor: 'var(--reader-gutter)', color: 'var(--reader-muted)', borderRight: '1px solid var(--reader-border)' },
    '.cm-activeLine': { backgroundColor: 'var(--reader-current-line, var(--reader-active-line))' },
    '.cm-activeLineGutter': { backgroundColor: 'var(--reader-active-line)', color: 'var(--reader-text)' },
    '.cm-selectionMatch': { backgroundColor: 'var(--reader-selection-match)' },
    '.cm-searchMatch': { backgroundColor: 'var(--mark-bg)', outline: '1px solid var(--accent)' },
    '.cm-searchMatch.cm-searchMatch-selected': { backgroundColor: 'var(--accent)', color: 'var(--accent-contrast)' },
    '&.cm-focused .cm-matchingBracket': { backgroundColor: 'var(--reader-selection-match)', outline: '1px solid var(--reader-muted)' },
    '.cm-panels': { backgroundColor: 'var(--reader-gutter)', color: 'var(--reader-text)' },
    '.cm-panels.cm-panels-top': { borderBottom: '1px solid var(--reader-border)' },
    '.cm-panel': { fontFamily: 'var(--font)' },
    '.cm-textfield': { backgroundColor: 'var(--reader-bg)', color: 'var(--reader-text)', border: '1px solid var(--reader-border)', borderRadius: '6px', padding: '3px 6px' },
    '.cm-button': { backgroundImage: 'none', backgroundColor: 'var(--reader-active-line)', color: 'var(--reader-text)', border: '1px solid var(--reader-border)', borderRadius: '6px' },
    '.cm-panel.cm-search [name=close]': { color: 'var(--reader-muted)' },
    // A line going on past the right edge (see lineContinues): a fade over the text, not over the scroll bars or panels.
    '&.cm-line-continues::after': {
        content: '""',
        position: 'absolute',
        zIndex: '2',
        top: 'var(--cm-fade-top, 0)',
        right: 'var(--cm-fade-right, 0)',
        bottom: 'var(--cm-fade-bottom, 0)',
        width: '32px',
        background: 'linear-gradient(to right, transparent, var(--reader-bg))',
        pointerEvents: 'none',
    },
});

/**
 * Shows that a line goes on past the right edge (wrapping off, not scrolled to its end): `cm-line-continues` on the
 * editor, a fade (readerTheme) placed by `--cm-fade-*` inside the scroller's scroll bars and under a search panel.
 */
export const lineContinues = ViewPlugin.fromClass(class {
    constructor(private readonly view: EditorView) {
        view.scrollDOM.addEventListener('scroll', this.onScroll, { passive: true });
        this.schedule();
    }

    update(update: ViewUpdate): void {
        if (update.geometryChanged || update.docChanged || update.viewportChanged)
            this.schedule();
    }

    destroy(): void {
        this.view.scrollDOM.removeEventListener('scroll', this.onScroll);
        this.view.dom.classList.remove('cm-line-continues');
    }

    /** Scrolling reads the layout at once (nothing is being updated then). */
    private readonly onScroll = () => showLineContinues(this.view, readLineContinues(this.view));

    /** After an update, when CodeMirror reads the layout. */
    private schedule(): void {
        this.view.requestMeasure({ key: this, read: readLineContinues, write: (fade, view) => showLineContinues(view, fade) });
    }
});

interface LineFade { continues: boolean; top: number; right: number; bottom: number }

function readLineContinues(view: EditorView): LineFade {
    const scroller = view.scrollDOM;
    return {
        continues: scroller.scrollLeft + scroller.clientWidth < scroller.scrollWidth - 1,
        top: scroller.offsetTop,
        right: scroller.offsetWidth - scroller.clientWidth,
        bottom: scroller.offsetHeight - scroller.clientHeight,
    };
}

function showLineContinues(view: EditorView, fade: LineFade): void {
    view.dom.classList.toggle('cm-line-continues', fade.continues);
    view.dom.style.setProperty('--cm-fade-top', `${fade.top}px`);
    view.dom.style.setProperty('--cm-fade-right', `${Math.max(0, fade.right)}px`);
    view.dom.style.setProperty('--cm-fade-bottom', `${Math.max(0, fade.bottom)}px`);
}

export interface TextEditorOptions {
    text: string;
    /** The file's name: its language (highlighting) comes from it. */
    name: string;
    wrap: boolean;
    lineNumbers: boolean;
    /** Where the cursor is, each time it moves (1-based). */
    onCursor?: (line: number, column: number) => void;
    /** The text was changed (typing, undo...): `dirty` says whether it differs from what was last saved. */
    onChange?: (dirty: boolean) => void;
    /** Editable from the start (a new file). */
    editable?: boolean;
}

/**
 * CodeMirror 6 for the text views of `formats/` (agents/docs/features/text-viewer.md, text-editor.md): read only
 * until `setEditable` (then with undo, and Tab indenting), knowing whether its text differs from the last saved. Colours
 * come from the app's CSS (`styles/reader.css`, `tok-*` classes of `classHighlighter`), not from a CodeMirror theme,
 * so they follow the app's theme as everything else does.
 */
export class TextEditor {
    readonly view: EditorView;
    private readonly wrapping = new Compartment();
    private readonly numbers = new Compartment();
    private readonly language = new Compartment();
    private readonly editing = new Compartment();
    private destroyed = false;
    /** The document as last loaded or saved: `dirty` compares with it (undoing back to it is clean again). */
    private saved: Text;
    private wrap: boolean;
    private lineNumbers: boolean;

    constructor(parent: HTMLElement, options: TextEditorOptions) {
        this.wrap = options.wrap;
        this.lineNumbers = options.lineNumbers;
        const onCursor = options.onCursor;
        const extensions: Extension[] = [
            this.numbers.of(options.lineNumbers ? [lineNumbers(), highlightActiveLineGutter()] : []),
            this.wrapping.of(options.wrap ? EditorView.lineWrapping : []),
            this.language.of([]),
            highlightSpecialChars(),
            drawSelection(),
            highlightActiveLine(),
            bracketMatching(),
            syntaxHighlighting(classHighlighter),
            readerTheme,
            lineContinues,
            highlightSelectionMatches(),
            search({ top: true }),
            this.editing.of(TextEditor.editingExtensions(options.editable ?? false)),
            history(),
            EditorState.tabSize.of(4),
            keymap.of([...searchKeymap, ...historyKeymap, ...defaultKeymap]),
            EditorView.contentAttributes.of({ 'aria-label': `Content of ${options.name}` }),
        ];
        const onChange = options.onChange;
        extensions.push(EditorView.updateListener.of(update => {
            if (onCursor && (update.selectionSet || update.docChanged)) {
                const head = update.state.selection.main.head;
                const line = update.state.doc.lineAt(head);
                onCursor(line.number, head - line.from + 1);
            }
            if (onChange && update.docChanged)
                onChange(!update.state.doc.eq(this.saved));
        }));
        const state = EditorState.create({ doc: options.text, extensions });
        this.saved = state.doc;
        this.view = new EditorView({ parent, state });
    }

    private static editingExtensions(editable: boolean): Extension {
        return editable ? [EditorState.readOnly.of(false), keymap.of([indentWithTab])] : EditorState.readOnly.of(true);
    }

    /** Lets the text be changed, or not. */
    setEditable(editable: boolean): void {
        this.view.dispatch({ effects: this.editing.reconfigure(TextEditor.editingExtensions(editable)) });
    }

    get editable(): boolean {
        return !this.view.state.readOnly;
    }

    /** The text as it is now (lines joined with \n, whatever the file had). */
    text(): string {
        return this.view.state.doc.toString();
    }

    get dirty(): boolean {
        return !this.view.state.doc.eq(this.saved);
    }

    /** The text as it is now is what the file holds (saved). */
    markSaved(): void {
        this.saved = this.view.state.doc;
    }

    /** Puts `text` in place of everything (the file read again), as saved. */
    replace(text: string): void {
        this.view.dispatch({ changes: { from: 0, to: this.view.state.doc.length, insert: text } });
        this.markSaved();
    }

    /**
     * Adds `text` at the end (a file followed as it grows), as saved; keeps the view at the end when it was there.
     * `limit`: the most characters kept, the oldest lines dropped beyond it.
     */
    append(text: string, limit = Infinity): void {
        const atEnd = this.atEnd();
        const doc = this.view.state.doc;
        const changes: { from: number; to?: number; insert: string }[] = [{ from: doc.length, insert: text }];
        const over = doc.length + text.length - limit;
        if (over > 0)
            changes.unshift({ from: 0, to: Math.min(doc.length, doc.lineAt(Math.min(over, doc.length)).to + 1), insert: '' });
        this.view.dispatch({ changes });
        this.markSaved();
        if (atEnd)
            this.scrollToEnd();
    }

    /** The view shows the last line (within a line's height). */
    atEnd(): boolean {
        const scroller = this.view.scrollDOM;
        return scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 24;
    }

    scrollToEnd(): void {
        this.view.dispatch({ effects: EditorView.scrollIntoView(this.view.state.doc.length, { y: 'end' }) });
    }

    /** The first line shown (1-based). */
    topLine(): number {
        const block = this.view.lineBlockAtHeight(this.view.scrollDOM.scrollTop);
        return this.view.state.doc.lineAt(block.from).number;
    }

    /** Shows `line` (1-based) at the top, the cursor on it. */
    scrollToLine(line: number): void {
        const doc = this.view.state.doc;
        const at = doc.line(Math.min(Math.max(1, line), doc.lines)).from;
        this.view.dispatch({ selection: { anchor: at }, effects: EditorView.scrollIntoView(at, { y: 'start' }) });
    }

    /** Selects `from`–`to` (offsets in `text()`) and shows it in the middle. */
    select(from: number, to = from): void {
        const length = this.view.state.doc.length;
        const anchor = Math.min(Math.max(0, from), length);
        const head = Math.min(Math.max(anchor, to), length);
        this.view.dispatch({ selection: { anchor, head }, effects: EditorView.scrollIntoView(anchor, { y: 'center' }) });
    }

    /** Changes the text as typing would (undone at once, and dirty): `{ offset, length, content }` of the text now. */
    edit(changes: readonly { offset: number; length: number; content: string }[]): void {
        if (!changes.length)
            return;
        this.view.dispatch({
            changes: changes.map(change => ({ from: change.offset, to: change.offset + change.length, insert: change.content })),
            userEvent: 'input',
        });
    }

    /** Loads the language of `name` and highlights with it; resolves its name, or null for plain text. */
    async setLanguage(name: string): Promise<string | null> {
        const description = languageFor(name);
        if (!description)
            return null;
        const support = await description.load();
        if (!this.destroyed)
            this.view.dispatch({ effects: this.language.reconfigure(support) });
        return description.name;
    }

    /** Wrapping and line numbers as they are now wanted (only what changed is reconfigured). */
    configure(options: { wrap: boolean; lineNumbers: boolean }): void {
        const effects = [];
        if (options.wrap !== this.wrap)
            effects.push(this.wrapping.reconfigure(options.wrap ? EditorView.lineWrapping : []));
        if (options.lineNumbers !== this.lineNumbers)
            effects.push(this.numbers.reconfigure(options.lineNumbers ? [lineNumbers(), highlightActiveLineGutter()] : []));
        this.wrap = options.wrap;
        this.lineNumbers = options.lineNumbers;
        if (effects.length)
            this.view.dispatch({ effects });
    }

    /** The font, size or spacing changed (CSS variables): the editor measures its lines again. */
    remeasure(): void {
        this.view.requestMeasure();
    }

    find(): void {
        openSearchPanel(this.view);
    }

    goToLine(): void {
        gotoLine(this.view);
    }

    focus(): void {
        this.view.focus();
    }

    destroy(): void {
        this.destroyed = true;
        this.view.destroy();
    }
}
