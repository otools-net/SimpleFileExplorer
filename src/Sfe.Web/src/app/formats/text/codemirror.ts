import { defaultKeymap } from '@codemirror/commands';
import { bracketMatching, syntaxHighlighting } from '@codemirror/language';
import { gotoLine, highlightSelectionMatches, openSearchPanel, search, searchKeymap } from '@codemirror/search';
import { Compartment, EditorState, Extension } from '@codemirror/state';
import {
    drawSelection, EditorView, highlightActiveLine, highlightActiveLineGutter, highlightSpecialChars, keymap, lineNumbers,
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
    '.cm-content': { caretColor: 'var(--reader-text)' },
    '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--reader-text)' },
    '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': { backgroundColor: 'var(--reader-selection)' },
    '.cm-gutters': { backgroundColor: 'var(--reader-gutter)', color: 'var(--reader-muted)', borderRight: '1px solid var(--reader-border)' },
    '.cm-activeLine': { backgroundColor: 'var(--reader-active-line)' },
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
});

export interface TextEditorOptions {
    text: string;
    /** The file's name: its language (highlighting) comes from it. */
    name: string;
    wrap: boolean;
    lineNumbers: boolean;
    /** Where the cursor is, each time it moves (1-based). */
    onCursor?: (line: number, column: number) => void;
}

/**
 * CodeMirror 6 for the text views of `formats/` (agents/docs/features/text-viewer.md): read only for now. Colours
 * come from the app's CSS (`styles/reader.css`, `tok-*` classes of `classHighlighter`), not from a CodeMirror theme,
 * so they follow the app's theme as everything else does.
 */
export class TextEditor {
    readonly view: EditorView;
    private readonly wrapping = new Compartment();
    private readonly numbers = new Compartment();
    private readonly language = new Compartment();
    private destroyed = false;

    constructor(parent: HTMLElement, options: TextEditorOptions) {
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
            highlightSelectionMatches(),
            search({ top: true }),
            EditorState.readOnly.of(true),
            EditorState.tabSize.of(4),
            keymap.of([...searchKeymap, ...defaultKeymap]),
            EditorView.contentAttributes.of({ 'aria-label': `Content of ${options.name}` }),
        ];
        if (onCursor)
            extensions.push(EditorView.updateListener.of(update => {
                if (update.selectionSet || update.docChanged) {
                    const head = update.state.selection.main.head;
                    const line = update.state.doc.lineAt(head);
                    onCursor(line.number, head - line.from + 1);
                }
            }));
        this.view = new EditorView({ parent, state: EditorState.create({ doc: options.text, extensions }) });
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

    setWrap(wrap: boolean): void {
        this.view.dispatch({ effects: this.wrapping.reconfigure(wrap ? EditorView.lineWrapping : []) });
    }

    setLineNumbers(show: boolean): void {
        this.view.dispatch({ effects: this.numbers.reconfigure(show ? [lineNumbers(), highlightActiveLineGutter()] : []) });
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
