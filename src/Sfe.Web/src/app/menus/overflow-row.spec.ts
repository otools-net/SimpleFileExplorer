import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ContextMenuComponent, MenuEntry } from './context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from './overflow-row';

const entry = (id: string, more: Partial<MenuEntry> = {}) => (): MenuEntry => ({ id, label: id.toUpperCase(), icon: 'info', title: id, run: () => ran.push(id), ...more });
let ran: string[] = [];

@Component({
    imports: [OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent, ContextMenuComponent],
    template: `
        <div id="row" sfeOverflowRow="Test row" data-width="400">
            <button id="a" type="button" data-width="50" [sfeOverflowItem]="a" [overflowStay]="stayA">A</button>
            <button id="b" type="button" data-width="50" [sfeOverflowItem]="b" [overflowPriority]="priorityB">B</button>
            <button id="c" type="button" data-width="50" [sfeOverflowItem]="c">C</button>
            @if (showD()) {
                <button id="d" type="button" data-width="50" [sfeOverflowItem]="d">D</button>
            }
            <button sfeOverflowMore data-width="30"></button>
        </div>
        <sfe-context-menu />
    `,
})
class Host {
    stayA = false;
    priorityB = 0;
    readonly showD = signal(true);
    a = entry('a');
    b = entry('b', { checked: true });
    c = entry('c', { disabled: true });
    d = entry('d');
}

/** The width of the element (its `data-width`): jsdom has no layout. */
function width(this: HTMLElement): number {
    return Number(this.getAttribute('data-width') ?? 0);
}

describe('OverflowRow', () => {
    let fixture: ComponentFixture<Host>;
    let element: HTMLElement;
    let row: OverflowRowDirective;
    const offsetWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth')!;
    const clientWidth = Object.getOwnPropertyDescriptor(Element.prototype, 'clientWidth')!;
    const at = (selector: string) => element.querySelector<HTMLElement>(selector)!;
    const shown = () => [...element.querySelectorAll('#row > button[id]')].filter(button => !button.hasAttribute('data-overflowed')).map(button => button.id);
    const more = () => at('.overflow-more');

    async function setUp(change: (host: Host) => void = () => {}): Promise<void> {
        fixture = TestBed.createComponent(Host);
        change(fixture.componentInstance);
        element = fixture.nativeElement;
        await settle();
        row = fixture.debugElement.query(By.directive(OverflowRowDirective)).injector.get(OverflowRowDirective);
    }

    /** The row `px` wide, measured again (the debounce and the gaps' second pass are microtasks). */
    async function resize(px: number): Promise<void> {
        at('#row').setAttribute('data-width', String(px));
        row.update();
        await settle();
    }

    async function settle(): Promise<void> {
        await new Promise(resolve => setTimeout(resolve));      // the measuring's microtasks
        await fixture.whenStable();
    }

    beforeEach(() => {
        ran = [];
        Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: width });
        Object.defineProperty(Element.prototype, 'clientWidth', { configurable: true, get: width });
        (window as { ResizeObserver?: unknown }).ResizeObserver = class {
            observe(): void { /* measured by update() in these tests */ }
            unobserve(): void { /* nothing observed */ }
            disconnect(): void { /* nothing observed */ }
        };
    });

    afterEach(() => {
        Object.defineProperty(HTMLElement.prototype, 'offsetWidth', offsetWidth);
        Object.defineProperty(Element.prototype, 'clientWidth', clientWidth);
        delete (window as { ResizeObserver?: unknown }).ResizeObserver;
    });

    it('shows every button and no "⋯" when they all fit', async () => {
        await setUp();
        await resize(200);
        expect(shown()).toEqual(['a', 'b', 'c', 'd']);
        expect(more().classList).not.toContain('shown');
        expect(more().getAttribute('aria-hidden')).toBe('true');
    });

    it('takes the rightmost buttons out first, with room for the "⋯"', async () => {
        await setUp();
        await resize(150);
        // 4 × 50 doesn't fit; 3 × 50 + 30 neither; 2 × 50 + 30 does.
        expect(shown()).toEqual(['a', 'b']);
        expect(more().classList).toContain('shown');
        expect(more().getAttribute('aria-hidden')).toBeNull();
        expect(row.hidden().map(item => item.element.id)).toEqual(['c', 'd']);
    });

    it('keeps the buttons that stay, however narrow', async () => {
        await setUp(host => host.stayA = true);
        await resize(20);
        expect(shown()).toEqual(['a']);
    });

    it('takes a lower priority out before the ones on its right', async () => {
        await setUp(host => host.priorityB = -1);
        await resize(150);
        expect(shown()).toEqual(['a', 'c']);
        expect(row.hidden().map(item => item.element.id)).toEqual(['b', 'd']);
    });

    it('shows everything again once wide enough', async () => {
        await setUp();
        await resize(120);
        await resize(400);
        expect(shown()).toEqual(['a', 'b', 'c', 'd']);
        expect(more().classList).not.toContain('shown');
    });

    it('follows buttons that come and go', async () => {
        await setUp();
        await resize(150);
        fixture.componentInstance.showD.set(false);
        await settle();
        await resize(150);
        expect(shown()).toEqual(['a', 'b', 'c']);
    });

    it('keeps room for the gaps of the items shown only, and settles at a width where one fits without its gap', async () => {
        await setUp();
        at('#row').style.columnGap = '10px';
        expect(getComputedStyle(at('#row')).columnGap).toBe('10px');
        // a, b and the "⋯": 50 + 50 + 30 and two gaps = 150 (a gap for each of the 4 items would leave only a).
        await resize(155);
        expect(shown()).toEqual(['a', 'b']);
        // a, b, c and the "⋯" need 210: c fits without its gap, not with it; it leaves and stays out.
        await resize(205);
        expect(shown()).toEqual(['a', 'b']);
        await resize(210);
        expect(shown()).toEqual(['a', 'b', 'c']);
    });

    it('opens the buttons that left as a menu, in the row\'s order, which runs them', async () => {
        await setUp();
        await resize(100);
        more().click();
        await settle();
        const entries = [...document.querySelectorAll<HTMLElement>('.item-menu-entry')];
        expect(entries.map(entry => entry.dataset['entry'])).toEqual(['b', 'c', 'd']);
        expect(document.querySelector('.item-menu-title')?.textContent).toBe('Test row');
        entries[2].click();
        expect(ran).toEqual(['d']);
    });

    it('shows on/off entries with a check mark, and disabled ones greyed and not run', async () => {
        await setUp();
        await resize(100);
        more().click();
        await settle();
        const b = document.querySelector<HTMLElement>('[data-entry="b"]')!;
        const c = document.querySelector<HTMLElement>('[data-entry="c"]')!;
        expect(b.getAttribute('role')).toBe('menuitemcheckbox');
        expect(b.getAttribute('aria-checked')).toBe('true');
        expect(b.querySelector('.item-menu-check')).not.toBeNull();
        expect(c.getAttribute('aria-disabled')).toBe('true');
        c.click();
        expect(ran).toEqual([]);
        expect(document.querySelector('.item-menu')).not.toBeNull();
    });
});
