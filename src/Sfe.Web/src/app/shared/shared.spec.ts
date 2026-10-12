import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { formatDuration, formatElapsed, formatSize, highlightParts, highlightPattern, joinParts, plural } from '../core/format';
import { deferred, settle } from '../../testing/fakes';
import { BusyDirective, whileBusy } from './busy.directive';
import { HighlightComponent } from './highlight.component';
import { SheetComponent } from './sheet.component';

describe('SheetComponent', () => {
    @Component({
        imports: [SheetComponent],
        template: `
            <sfe-sheet #sheet heading="Title" [subtitle]="subtitle()" (closed)="closed = closed + 1">
                <p class="content">Body</p>
                <button sheetFooter type="button" class="footer-button">OK</button>
            </sfe-sheet>
        `,
    })
    class Host {
        readonly subtitle = signal<string | null>(null);
        closed = 0;
    }

    let fixture: ComponentFixture<Host>;
    let dialog: HTMLDialogElement;
    let sheet: SheetComponent;

    beforeEach(async () => {
        fixture = TestBed.createComponent(Host);
        await fixture.whenStable();
        dialog = fixture.nativeElement.querySelector('dialog');
        sheet = fixture.debugElement.children[0].componentInstance;
    });

    it('puts the content in the body and the footer elements in the footer', () => {
        expect(dialog.querySelector('h2')!.textContent).toBe('Title');
        expect(dialog.querySelector('.sheet-body .content')).not.toBeNull();
        expect(dialog.querySelector('.sheet-foot .footer-button')).not.toBeNull();
        expect(dialog.querySelector('.sheet-subtitle')).toBeNull();
    });

    it('shows a subtitle when there is one', async () => {
        fixture.componentInstance.subtitle.set('3 files');
        await fixture.whenStable();

        expect(dialog.querySelector('.sheet-subtitle')!.textContent).toBe('3 files');
    });

    it('opens as a modal once, and says when it closed', () => {
        sheet.open();
        sheet.open();
        expect(dialog.open).toBe(true);
        expect(sheet.isOpen()).toBe(true);

        dialog.querySelector<HTMLButtonElement>('.sheet-head button')!.click();

        expect(dialog.open).toBe(false);
        expect(sheet.isOpen()).toBe(false);
        expect(fixture.componentInstance.closed).toBe(1);
    });

    it('closes on a click on the backdrop that also started there', () => {
        sheet.open();

        dialog.dispatchEvent(new Event('pointerdown'));
        dialog.dispatchEvent(new MouseEvent('click'));

        expect(dialog.open).toBe(false);
    });

    it('stays open when a press inside the card ends on the backdrop', () => {
        sheet.open();

        dialog.querySelector('.content')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
        dialog.dispatchEvent(new MouseEvent('click'));
        expect(dialog.open).toBe(true);

        // Nor does it remember that press for the next click.
        dialog.dispatchEvent(new MouseEvent('click'));
        expect(dialog.open).toBe(true);
    });

    it('lets presses inside the card do what they do (a dropdown opens on its mousedown)', () => {
        const press = new PointerEvent('pointerdown', { bubbles: true, cancelable: true });
        dialog.querySelector('.content')!.dispatchEvent(press);
        expect(press.defaultPrevented).toBe(false);

        const onBackdrop = new PointerEvent('pointerdown', { bubbles: true, cancelable: true });
        dialog.dispatchEvent(onBackdrop);
        expect(onBackdrop.defaultPrevented).toBe(false);
    });

    it('stays open on clicks inside the card', () => {
        sheet.open();

        dialog.dispatchEvent(new Event('pointerdown'));
        dialog.querySelector<HTMLElement>('.content')!.click();

        expect(dialog.open).toBe(true);
    });
});

describe('BusyDirective', () => {
    @Component({
        imports: [BusyDirective],
        template: `<button type="button" [sfeBusy]="busy()" [disabled]="busy()">Save</button>`,
    })
    class Host {
        readonly busy = signal(false);
    }

    it('shows a spinner and marks the button busy, once', async () => {
        const fixture = TestBed.createComponent(Host);
        await fixture.whenStable();
        const button: HTMLButtonElement = fixture.nativeElement.querySelector('button');

        fixture.componentInstance.busy.set(true);
        await fixture.whenStable();
        expect(button.querySelectorAll('.button-spinner')).toHaveLength(1);
        expect(button.getAttribute('aria-busy')).toBe('true');
        expect(button.classList).toContain('busy');

        fixture.componentInstance.busy.set(false);
        await fixture.whenStable();
        expect(button.querySelector('.button-spinner')).toBeNull();
        expect(button.hasAttribute('aria-busy')).toBe(false);
        expect(button.textContent).toBe('Save');
    });

    it('is busy while the task runs, and not after it failed', async () => {
        const busy = signal(false);
        const task = deferred<number>();

        const running = whileBusy(busy, () => task.promise);
        expect(busy()).toBe(true);
        task.resolve(5);
        expect(await running).toBe(5);
        expect(busy()).toBe(false);

        await expect(whileBusy(busy, () => Promise.reject(new Error('no')))).rejects.toThrow('no');
        expect(busy()).toBe(false);
    });
});

describe('HighlightComponent', () => {
    async function render(text: string, query: string): Promise<HTMLElement> {
        const fixture = TestBed.createComponent(HighlightComponent);
        fixture.componentRef.setInput('text', text);
        fixture.componentRef.setInput('pattern', highlightPattern(query));
        await settle();
        return fixture.nativeElement;
    }

    it('marks the searched words, whatever their case', async () => {
        const element = await render('Sunny Sunday', 'sun');

        expect([...element.querySelectorAll('mark')].map(m => m.textContent)).toEqual(['Sun', 'Sun']);
        expect(element.textContent).toBe('Sunny Sunday');
    });

    it('never parses the name as HTML', async () => {
        const element = await render('<img src=x onerror=alert(1)>.jpg', 'img');

        expect(element.querySelector('img')).toBeNull();
        expect(element.textContent).toBe('<img src=x onerror=alert(1)>.jpg');
    });
});

describe('format', () => {
    it('stops at terabytes', () => {
        expect(formatSize(0)).toBe('0 B');
        expect(formatSize(1023)).toBe('1023 B');
        expect(formatSize(1024)).toBe('1 KB');
        expect(formatSize(1024 ** 5)).toBe(`${(1024).toLocaleString()} TB`);
        expect(formatSize(undefined)).toBe('');
    });

    it('shows broken durations as zero', () => {
        expect(formatDuration(NaN)).toBe('0:00');
        expect(formatDuration(-5)).toBe('0:00');
        expect(formatDuration(Infinity)).toBe('0:00');
        expect(formatDuration(59.9)).toBe('0:59');
        expect(formatDuration(36000)).toBe('10:00:00');
    });

    it('rounds elapsed times, never below zero', () => {
        expect(formatElapsed(-3)).toBe('0s');
        expect(formatElapsed(44.6)).toBe('45s');
        expect(formatElapsed(7800)).toBe('2h 10m');
    });

    it('counts with the plural only when not one', () => {
        expect(plural(0, 'file')).toBe('0 files');
        expect(plural(1, 'copy', 'copies')).toBe('1 copy');
        expect(plural(1500, 'file')).toBe(`${(1500).toLocaleString()} files`);
    });

    it('joins the parts that are set', () => {
        expect(joinParts(['a', null, '', undefined, false, 'b'])).toBe('a · b');
        expect(joinParts([null])).toBe('');
        expect(joinParts(['a', 'b'], ', ')).toBe('a, b');
    });

    it('highlights words holding regex characters literally', () => {
        const pattern = highlightPattern('a.b (x) [1]');

        expect(highlightParts('a.b and axb', pattern).filter(p => p.mark).map(p => p.text)).toEqual(['a.b']);
        expect(highlightParts('(x) [1]', pattern).filter(p => p.mark).map(p => p.text)).toEqual(['(x)', '[1]']);
    });

    it('highlights a quoted phrase as one, and ignores the full-path marker', () => {
        const pattern = highlightPattern('*"big sky", sun');

        expect(highlightParts('a big sky at sunset', pattern).filter(p => p.mark).map(p => p.text)).toEqual(['big sky', 'sun']);
    });

    it('has nothing to highlight for an empty search', () => {
        expect(highlightPattern('')).toBeNull();
        expect(highlightPattern(null)).toBeNull();
        expect(highlightPattern('"" ,')).toBeNull();
        expect(highlightParts('', null)).toEqual([{ text: '', mark: false }]);
    });
});
