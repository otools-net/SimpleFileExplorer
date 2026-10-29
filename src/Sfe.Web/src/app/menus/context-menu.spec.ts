import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { setMediaQuery, settle } from '../../testing/fakes';
import { ContextMenu, ContextMenuComponent, ContextMenuDirective, keepsBrowserMenu, MenuEntry, MenuTarget, usePageMenu } from './context-menu';

function target(key: string, entries: MenuEntry[] = [{ id: 'act', label: 'Act', icon: 'info', title: 'Act', run: vi.fn() }], touch?: () => void): MenuTarget {
    return { kind: 'folderLink', key, name: key, entries: () => entries, touch };
}

@Component({
    imports: [ContextMenuDirective, ContextMenuComponent],
    template: `
        <div id="outer" [sfeContextMenu]="outer">
            <button id="inner" type="button" [sfeContextMenu]="inner">Inner</button>
            <input id="text">
            <input id="box" type="checkbox">
            <div id="editable" contenteditable="true">Notes</div>
            <p id="words">Some words</p>
            <a id="itemLink" href="files/a.txt">An item's own link</a>
        </div>
        <a id="anchor" href="#part-2">Part 2</a>
        <a id="appLink" href="admin?section=logs">Logs</a>
        <span id="empty" tabindex="0" [sfeContextMenu]="empty">Nothing</span>
        <span id="plain" tabindex="0">Plain</span>
        <dialog id="dialog" open>
            <p id="inDialog">Words</p>
            <input id="dialogText">
            <a id="dialogLink" href="?path=Photos">Photos</a>
            <div data-context-menu="own"><p id="reader">A book</p><a id="readerLink" href="?path=Books">Books</a></div>
        </dialog>
        <div data-context-menu="none"><button id="picker" type="button">Folder</button></div>
        <sfe-context-menu />
    `,
})
class Host {
    outer = target('outer');
    inner = target('inner');
    empty = target('empty', []);
}

describe('ContextMenu', () => {
    let menu: ContextMenu;
    let element: HTMLElement;
    const at = (selector: string) => element.querySelector<HTMLElement>(selector)!;
    const rightClick = (on: Element, init: MouseEventInit = {}) => {
        const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 10, clientY: 20, ...init });
        on.dispatchEvent(event);
        return event;
    };

    beforeEach(async () => {
        const fixture = TestBed.createComponent(Host);
        await fixture.whenStable();
        element = fixture.nativeElement;
        menu = TestBed.inject(ContextMenu);
    });

    afterEach(() => getSelection()?.removeAllRanges());

    it('opens the menu of the nearest element that has one, at the pointer', async () => {
        const event = rightClick(at('#inner'));

        expect(event.defaultPrevented).toBe(true);
        expect(menu.open()).toMatchObject({ target: { key: 'inner' }, x: 10, y: 20, trigger: at('#inner') });
        rightClick(at('#words'));
        expect(menu.open()?.target.key).toBe('outer');
        await settle();
        expect(document.querySelector('.item-menu')?.getAttribute('data-menu')).toBe('folderLink');
    });

    it('keeps the browser\'s menu with Shift, in text fields and on selected text', () => {
        expect(rightClick(at('#inner'), { shiftKey: true }).defaultPrevented).toBe(false);
        expect(rightClick(at('#text')).defaultPrevented).toBe(false);
        expect(rightClick(at('#editable')).defaultPrevented).toBe(false);
        // A check box isn't a text field: the menu of what holds it.
        expect(rightClick(at('#box')).defaultPrevented).toBe(true);

        const range = document.createRange();
        range.selectNodeContents(at('#words'));
        getSelection()!.addRange(range);
        expect(rightClick(at('#words')).defaultPrevented).toBe(false);
        // Elsewhere than the selection: the app's menu.
        expect(rightClick(at('#inner')).defaultPrevented).toBe(true);
        expect(keepsBrowserMenu(new MouseEvent('contextmenu'))).toBe(false);
    });

    it('leaves the browser\'s menu where there is nothing to offer, and no page menu', () => {
        expect(rightClick(at('#empty')).defaultPrevented).toBe(false);
        expect(rightClick(at('#plain')).defaultPrevented).toBe(false);
        expect(menu.open()).toBeNull();
    });

    it('shows the page\'s menu for a right click on nothing with a menu of its own', () => {
        menu.page.set(target('page'));

        expect(rightClick(at('#plain')).defaultPrevented).toBe(true);
        expect(menu.open()?.target.key).toBe('page');
        expect(menu.open()?.trigger).toBeNull();
        expect(rightClick(at('#plain'), { shiftKey: true }).defaultPrevented).toBe(false);
    });

    it('shows no menu in dialogs and the folder picker, and leaves readers marked as having their own alone', () => {
        menu.page.set(target('page'));

        // A dialog, the folder picker: neither the page's menu nor the browser's.
        expect(rightClick(at('#inDialog')).defaultPrevented).toBe(true);
        expect(rightClick(at('#picker')).defaultPrevented).toBe(true);
        expect(menu.open()).toBeNull();
        // Still the browser's with Shift and in text fields; a link still has its menu.
        expect(rightClick(at('#inDialog'), { shiftKey: true }).defaultPrevented).toBe(false);
        expect(rightClick(at('#dialogText')).defaultPrevented).toBe(false);
        rightClick(at('#dialogLink'));
        expect(menu.open()?.target).toMatchObject({ kind: 'appLink', name: 'Photos' });
        menu.close();

        // A reader or editor (the viewer's documents): its own right click, links included.
        expect(rightClick(at('#reader')).defaultPrevented).toBe(false);
        expect(rightClick(at('#readerLink')).defaultPrevented).toBe(false);
        expect(menu.open()).toBeNull();
    });

    it('has a page\'s menu while the page is shown (usePageMenu)', () => {
        @Component({ template: '' })
        class Page {
            readonly target = target('page');
            constructor() {
                usePageMenu(this.target);
            }
        }
        const page = TestBed.createComponent(Page);
        expect(menu.page()).toBe(page.componentInstance.target);

        page.destroy();
        expect(menu.page()).toBeNull();
    });

    it('gives a link with no menu of its own a link menu: open, in a new tab, copy its address', async () => {
        // A link that is part of an element with a menu (an item's name): the element's.
        rightClick(at('#itemLink'));
        expect(menu.open()?.target.key).toBe('outer');
        // A link to a place in the same page: no link menu (nor a page menu here: the browser's).
        expect(rightClick(at('#anchor')).defaultPrevented).toBe(false);

        // Anywhere else (an app link), even with no page menu.
        const event = rightClick(at('#appLink'));
        expect(event.defaultPrevented).toBe(true);
        const link = menu.open()!.target;
        expect(link).toMatchObject({ kind: 'appLink', name: 'Logs' });
        expect(link.entries().map(e => e.id)).toEqual(['open', 'newTab', 'copyLink']);
        expect(link.entries()[1]).toMatchObject({ newTab: true, href: expect.stringMatching(/\/admin\?section=logs$/) });
        const click = vi.fn((e: Event) => e.preventDefault());
        at('#appLink').addEventListener('click', click);
        link.entries()[0].run!();
        expect(click).toHaveBeenCalled();

        // The keyboard on a focused link: the same.
        menu.close();
        at('#appLink').focus();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true }));
        expect(menu.open()).toMatchObject({ target: { kind: 'appLink', name: 'Logs' }, trigger: at('#appLink') });
    });

    it('does the target\'s touch action on a long press instead of opening the menu', () => {
        const touch = vi.fn();
        const event = Object.assign(new MouseEvent('contextmenu', { cancelable: true }), { pointerType: 'touch' });

        menu.fromEvent(event, target('item', undefined, touch));

        expect(event.defaultPrevented).toBe(true);
        expect(touch).toHaveBeenCalled();
        expect(menu.open()).toBeNull();
        // Without one, the menu opens (a sheet on phones).
        menu.fromEvent(Object.assign(new MouseEvent('contextmenu', { cancelable: true }), { pointerType: 'touch' }), target('link'));
        expect(menu.open()?.target.key).toBe('link');
    });

    it('opens the focused element\'s menu with the ContextMenu key or Shift+F10, and swallows the browser\'s echo', () => {
        at('#inner').focus();
        const key = new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true });
        document.dispatchEvent(key);

        expect(key.defaultPrevented).toBe(true);
        expect(menu.open()).toMatchObject({ target: { key: 'inner' }, trigger: at('#inner') });
        // The browser's own contextmenu event of that key: neither its menu nor a second one.
        menu.close();
        expect(rightClick(at('#inner'), { shiftKey: true }).defaultPrevented).toBe(true);
        expect(menu.open()).toBeNull();

        at('#words').setAttribute('tabindex', '0');
        at('#words').focus();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F10', shiftKey: true, bubbles: true, cancelable: true }));
        expect(menu.open()?.target.key).toBe('outer');
        menu.close();

        at('#text').focus();
        const inField = new KeyboardEvent('keydown', { key: 'ContextMenu', bubbles: true, cancelable: true });
        document.dispatchEvent(inField);
        expect(inField.defaultPrevented).toBe(false);
        expect(menu.open()).toBeNull();
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F10', bubbles: true }));
        expect(menu.open()).toBeNull();
    });

    it('shows links as links (a new tab), runs actions and opens submenus in its place', async () => {
        const speed = vi.fn();
        menu.showAt(target('links', [
            { id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'In a tab of its own', href: 'view?path=a.txt', newTab: true },
            { id: 'speed', label: 'Speed', icon: 'fast', title: 'Playing speed', submenu: () => [{ id: 'fast', label: '2×', icon: 'fast', title: 'Twice as fast', run: speed }] },
        ]), at('#inner'));
        await settle();

        const link = document.querySelector<HTMLAnchorElement>('[data-entry="newTab"]')!;
        expect(link.tagName).toBe('A');
        expect(link.getAttribute('href')).toBe('view?path=a.txt');
        expect(link.target).toBe('_blank');
        expect(link.rel).toBe('noopener');
        expect(document.activeElement).toBe(link);

        document.querySelector<HTMLButtonElement>('[data-entry="speed"]')!.click();
        await settle();
        expect([...document.querySelectorAll('[data-entry]')].map(e => e.getAttribute('data-entry'))).toEqual(['back', 'fast']);
        document.querySelector('.item-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
        await settle();
        expect([...document.querySelectorAll('[data-entry]')].map(e => e.getAttribute('data-entry'))).toEqual(['newTab', 'speed']);

        document.querySelector<HTMLButtonElement>('[data-entry="speed"]')!.click();
        await settle();
        document.querySelector<HTMLButtonElement>('[data-entry="fast"]')!.click();
        expect(speed).toHaveBeenCalled();
        expect(menu.open()).toBeNull();
    });

    it('closes on Esc, giving the focus back to what opened it, and its ⋮ button toggles it', async () => {
        const trigger = at('#inner');
        menu.toggle(target('inner'), trigger);
        await settle();
        document.querySelector('.item-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(menu.open()).toBeNull();
        expect(document.activeElement).toBe(trigger);

        menu.toggle(target('inner'), trigger);
        expect(menu.open()).not.toBeNull();
        menu.toggle(target('inner'), trigger);
        expect(menu.open()).toBeNull();
    });

    describe('scrolling and resizing', () => {
        const width = window.innerWidth;
        const resize = (to: number) => {
            Object.defineProperty(window, 'innerWidth', { configurable: true, value: to });
            window.dispatchEvent(new Event('resize'));
        };

        afterEach(() => {
            setMediaQuery('(max-width: 640px)', false);
            Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
        });

        it('closes a menu at the pointer when the page scrolls or the window resizes', async () => {
            menu.showAt(target('inner'), at('#inner'));
            await settle();
            window.dispatchEvent(new Event('scroll'));
            expect(menu.open()).toBeNull();

            menu.showAt(target('inner'), at('#inner'));
            await settle();
            window.dispatchEvent(new Event('resize'));
            expect(menu.open()).toBeNull();
        });

        it('keeps a phone sheet open through a scroll, the address bar showing again and a turn', async () => {
            setMediaQuery('(max-width: 640px)', true);
            menu.showAt(target('inner'), at('#inner'));
            await settle();

            // The end of a fling, then the address bar coming back as the sheet freezes the page: only the height changes.
            window.dispatchEvent(new Event('scroll'));
            window.dispatchEvent(new Event('resize'));
            expect(menu.open()?.target.key).toBe('inner');

            resize(width + 200);
            expect(menu.open()?.target.key).toBe('inner');
        });

        it("places a finger's menu again under its button when a turn makes the sheet a popup", async () => {
            setMediaQuery('(max-width: 640px)', true);
            document.dispatchEvent(Object.assign(new Event('pointerdown', { bubbles: true }), { pointerType: 'touch' }));
            const trigger = at('#inner');
            menu.showAt(target('inner'), trigger);
            await settle();
            vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue(new DOMRect(30, 40, 20, 10));

            setMediaQuery('(max-width: 640px)', false);
            resize(width + 200);
            expect(menu.open()?.target.key).toBe('inner');
            await settle();
            expect(document.querySelector<HTMLElement>('.item-menu')!.style.top).toBe('54px');
            // The page scrolling as it is laid out again for the new size: part of the turn, the menu stays.
            window.dispatchEvent(new Event('scroll'));
            expect(menu.open()?.target.key).toBe('inner');
        });
    });

    describe("close watchers (Android's Back)", () => {
        const made: { onclose: (() => void) | null; destroyed: boolean; destroy(): void }[] = [];
        class FakeWatcher {
            onclose: (() => void) | null = null;
            destroyed = false;
            constructor() { made.push(this); }
            destroy(): void { this.destroyed = true; }
        }

        beforeEach(() => {
            made.length = 0;
            (globalThis as { CloseWatcher?: unknown }).CloseWatcher = FakeWatcher;
        });
        afterEach(() => delete (globalThis as { CloseWatcher?: unknown }).CloseWatcher);

        it('Back closes the open menu, and only it', async () => {
            menu.showAt(target('inner'), at('#inner'));
            await settle();
            expect(made).toHaveLength(1);
            made[0].onclose!();
            await settle();
            expect(menu.open()).toBeNull();
        });

        it("a menu closed otherwise lets go of its watcher (the next Back is the page's again)", async () => {
            menu.showAt(target('inner'), at('#inner'));
            await settle();
            menu.close();
            await settle();
            expect(made[0].destroyed).toBe(true);

            menu.showAt(target('outer'), at('#outer'));
            await settle();
            // The old watcher, if it ever fired, would not close the new menu.
            made[0].onclose?.();
            expect(menu.open()?.target.key).toBe('outer');
        });
    });
});

// iOS sends no `contextmenu` for a held finger: the app's own hold opens the same menus (Android's own event wins).
describe('a finger held on an element with a menu', () => {
    let menu: ContextMenu;
    let element: HTMLElement;
    const pointer = (type: string, on: Element, init: { x?: number; y?: number } = {}) => on.dispatchEvent(Object.assign(new Event(type, { bubbles: true, cancelable: true }),
        { pointerType: 'touch', pointerId: 7, isPrimary: true, clientX: init.x ?? 10, clientY: init.y ?? 20 }));

    beforeEach(async () => {
        vi.useFakeTimers();
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        element = fixture.nativeElement;
        menu = TestBed.inject(ContextMenu);
    });
    afterEach(() => {
        menu.close();
        vi.useRealTimers();
    });

    it('opens its menu after a moment, as a finger\'s, and the lift is no tap', () => {
        const inner = element.querySelector('#inner')!;
        expect(inner.hasAttribute('data-menu-target')).toBe(true);
        pointer('pointerdown', inner);
        vi.advanceTimersByTime(600);
        expect(menu.open()).toBeNull();
        vi.advanceTimersByTime(100);
        expect(menu.open()?.target.key).toBe('inner');
        expect(menu.open()?.touch).toBe(true);

        pointer('pointerup', inner);
        const click = new MouseEvent('click', { bubbles: true, cancelable: true });
        inner.dispatchEvent(click);
        expect(click.defaultPrevented).toBe(true);
        // The next tap's click goes through.
        pointer('pointerdown', inner);
        pointer('pointerup', inner);
        const next = new MouseEvent('click', { bubbles: true, cancelable: true });
        inner.dispatchEvent(next);
        expect(next.defaultPrevented).toBe(false);
    });

    it('leaves it to the browser that sends its own (Android), and drops a second one after (a Windows touch screen)', () => {
        const inner = element.querySelector('#inner')!;
        const seen = vi.fn();
        inner.addEventListener('contextmenu', seen);
        pointer('pointerdown', inner);
        vi.advanceTimersByTime(500);
        inner.dispatchEvent(Object.assign(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }), { pointerType: 'touch' }));
        vi.advanceTimersByTime(500);
        expect(seen).toHaveBeenCalledTimes(1);
        pointer('pointerup', inner);

        pointer('pointerdown', inner);
        vi.advanceTimersByTime(700);
        expect(seen).toHaveBeenCalledTimes(2);
        const late = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        inner.dispatchEvent(late);
        expect(seen).toHaveBeenCalledTimes(2);
        expect(late.defaultPrevented).toBe(true);

        // Sent after the lift (a slow phone, a Windows touch screen): still the hold's, dropped for a moment.
        pointer('pointerup', inner);
        const afterLift = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        inner.dispatchEvent(afterLift);
        expect(seen).toHaveBeenCalledTimes(2);
        expect(afterLift.defaultPrevented).toBe(true);
    });

    it('is no hold when the finger moves (scrolling) or the element has no menu', () => {
        const inner = element.querySelector('#inner')!;
        pointer('pointerdown', inner);
        pointer('pointermove', inner, { x: 10, y: 40 });
        vi.advanceTimersByTime(1000);
        expect(menu.open()).toBeNull();

        pointer('pointerdown', element.querySelector('#plain')!);
        vi.advanceTimersByTime(1000);
        expect(menu.open()).toBeNull();
    });
});
