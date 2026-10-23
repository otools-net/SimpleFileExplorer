import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { settle } from '../../testing/fakes';
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
        </div>
        <span id="empty" tabindex="0" [sfeContextMenu]="empty">Nothing</span>
        <span id="plain" tabindex="0">Plain</span>
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
});
