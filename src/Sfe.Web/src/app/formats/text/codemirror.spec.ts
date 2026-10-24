import { TextEditor } from './codemirror';

describe('TextEditor: a line going on past the right edge', () => {
    let parent: HTMLElement;
    let editor: TextEditor;

    beforeEach(() => {
        parent = document.createElement('div');
        document.body.append(parent);
        editor = new TextEditor(parent, { text: 'a long line\n', name: 'notes.log', wrap: false, lineNumbers: false });
    });

    afterEach(() => {
        editor.destroy();
        parent.remove();
    });

    /** jsdom has no layout: the scroller's sizes as a browser would give them (a 10 px scroll bar at the bottom). */
    function layout(scroller: HTMLElement, sizes: { scrollLeft: number; clientWidth: number; scrollWidth: number }): void {
        const values = { ...sizes, offsetWidth: sizes.clientWidth, offsetHeight: 410, clientHeight: 400, offsetTop: 36 };
        for (const [name, value] of Object.entries(values))
            Object.defineProperty(scroller, name, { configurable: true, value });
    }

    function scroll(sizes: { scrollLeft: number; clientWidth: number; scrollWidth: number }): DOMTokenList {
        const scroller = editor.view.scrollDOM;
        layout(scroller, sizes);
        scroller.dispatchEvent(new Event('scroll'));
        return editor.view.dom.classList;
    }

    it('fades the right edge while the line goes on, not once scrolled to its end', () => {
        expect(scroll({ scrollLeft: 0, clientWidth: 360, scrollWidth: 760 }).contains('cm-line-continues')).toBe(true);
        expect(scroll({ scrollLeft: 200, clientWidth: 360, scrollWidth: 760 }).contains('cm-line-continues')).toBe(true);
        expect(scroll({ scrollLeft: 400, clientWidth: 360, scrollWidth: 760 }).contains('cm-line-continues')).toBe(false);
    });

    it('does not fade lines that fit', () => {
        expect(scroll({ scrollLeft: 0, clientWidth: 360, scrollWidth: 360 }).contains('cm-line-continues')).toBe(false);
    });

    it('keeps the fade off the scroll bar and the panels above the text', () => {
        scroll({ scrollLeft: 0, clientWidth: 360, scrollWidth: 760 });
        const style = editor.view.dom.style;
        expect(style.getPropertyValue('--cm-fade-top')).toBe('36px');
        expect(style.getPropertyValue('--cm-fade-bottom')).toBe('10px');
        expect(style.getPropertyValue('--cm-fade-right')).toBe('0px');
    });

    it('forgets the fade when the editor goes', () => {
        scroll({ scrollLeft: 0, clientWidth: 360, scrollWidth: 760 });
        const dom = editor.view.dom;
        editor.destroy();
        expect(dom.classList.contains('cm-line-continues')).toBe(false);
    });
});
