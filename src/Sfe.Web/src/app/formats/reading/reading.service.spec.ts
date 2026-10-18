import { TestBed } from '@angular/core/testing';
import { loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { DEFAULT_READING, fontFamily, FONT_SIZES, sanitizeReading } from './reading';
import { READING_KEY, ReadingService } from './reading.service';
import { ReadingPanelComponent } from './reading-panel.component';

function service(): ReadingService {
    return TestBed.inject(ReadingService);
}

beforeEach(async () => {
    localStorage.removeItem(READING_KEY);
    const api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
});

afterEach(() => {
    localStorage.removeItem(READING_KEY);
    document.head.querySelectorAll('link[data-reader-fonts]').forEach(link => link.remove());
    TestBed.resetTestingModule();
});

describe('reading preferences', () => {
    it('takes what is valid and the defaults for the rest', () => {
        const read = sanitizeReading({ theme: 'neon', customFont: 'Fira Code', code: { size: 99, lineHeight: 1.65, width: 33, wrap: 'yes', font: 'jetbrains-mono' }, prose: null });

        expect(read.theme).toBe('app');
        expect(read.customFont).toBe('Fira Code');
        expect(read.code).toEqual({ ...DEFAULT_READING.code, size: FONT_SIZES.max, lineHeight: 1.65, font: 'jetbrains-mono' });
        expect(read.prose).toEqual(DEFAULT_READING.prose);
        expect(sanitizeReading('garbage')).toEqual(DEFAULT_READING);
    });

    it('puts a custom font first, cleaned, before the usual font of the profile', () => {
        const custom = { ...DEFAULT_READING.prose, font: 'custom' };

        expect(fontFamily('prose', custom, 'My "Font"; x')).toBe('"My Font x", "Literata", Georgia, serif');
        expect(fontFamily('code', custom, '  ')).toBe('var(--mono)');
    });
});

describe('ReadingService', () => {
    it('starts from the defaults, also when what is stored is broken', () => {
        localStorage.setItem(READING_KEY, '{not json');

        expect(service().preferences()).toEqual(DEFAULT_READING);
    });

    it('keeps only what this browser changed, and reads it back', () => {
        service().update('prose', { size: 20 });
        service().setTheme('sepia');

        const stored = JSON.parse(localStorage.getItem(READING_KEY)!);
        expect(stored).toEqual({ version: 1, prose: { ...DEFAULT_READING.prose, size: 20 }, theme: 'sepia' });
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: testProviders(mockApi()) });
        expect(service().profile('prose').size).toBe(20);
        expect(service().profile('code')).toEqual(DEFAULT_READING.code);
        expect(service().preferences().theme).toBe('sepia');
    });

    it('follows changed defaults where this browser changed nothing', () => {
        service().update('code', { wrap: true });

        service().defaults.set({ ...DEFAULT_READING, prose: { ...DEFAULT_READING.prose, size: 22 }, theme: 'dark' });

        expect(service().profile('prose').size).toBe(22);
        expect(service().profile('code').wrap).toBe(true);
        expect(service().preferences().theme).toBe('dark');
    });

    it('changes the size by steps within the limits, and back to the default', () => {
        service().update('code', { size: FONT_SIZES.max });
        service().changeSize('code', 1);
        expect(service().profile('code').size).toBe(FONT_SIZES.max);

        service().changeSize('code', -1);
        expect(service().profile('code').size).toBe(FONT_SIZES.max - 1);
        service().changeSize('code', 0);
        expect(service().profile('code').size).toBe(DEFAULT_READING.code.size);
    });

    it('resets a profile and the colours, keeping the other profile', () => {
        service().update('code', { size: 20 });
        service().update('prose', { size: 21 });
        service().setTheme('dark');

        service().reset('prose');

        expect(service().profile('prose')).toEqual(DEFAULT_READING.prose);
        expect(service().profile('code').size).toBe(20);
        expect(service().preferences().theme).toBe('app');
    });

    it('gives the CSS variables of a profile', () => {
        service().update('code', { width: 0, padding: 16, lineHeight: 1.8 });

        expect(service().style('code')).toEqual({
            '--reader-font': 'var(--mono)', '--reader-size': '14px', '--reader-line-height': '1.8', '--reader-width': 'none', '--reader-padding': '16px',
        });
        expect(service().style('prose')['--reader-width']).toBe('72ch');
    });

    it('downloads the bundled fonts once, and only when one of them is used', () => {
        service().linkFonts('code');
        expect(document.head.querySelector('link[data-reader-fonts]')).toBeNull();

        service().linkFonts('prose');
        service().linkFonts('prose');

        const links = document.head.querySelectorAll<HTMLLinkElement>('link[data-reader-fonts]');
        expect(links.length).toBe(1);
        expect(links[0].getAttribute('href')).toMatch(/^reader-fonts\.css\?v=/);
    });
});

describe('ReadingPanelComponent', () => {
    async function panel(profile: 'code' | 'prose' = 'prose') {
        const fixture = TestBed.createComponent(ReadingPanelComponent);
        fixture.componentRef.setInput('profile', profile);
        const closed = vi.fn();
        fixture.componentInstance.closed.subscribe(closed);
        await settle();
        await fixture.whenStable();
        return { fixture, element: fixture.nativeElement as HTMLElement, closed };
    }

    function choose(select: HTMLSelectElement, value: string): void {
        select.value = value;
        select.dispatchEvent(new Event('change'));
    }

    it('changes the preferences of its profile as they are picked', async () => {
        const { fixture, element } = await panel('prose');
        const selects = element.querySelectorAll('select');

        choose(selects[0], 'atkinson');
        choose(selects[2], '100');
        element.querySelector<HTMLButtonElement>('button[aria-label="Larger"]')!.click();
        element.querySelector<HTMLButtonElement>('.reading-theme[data-reader-theme="sepia"]')!.click();
        await settle();
        await fixture.whenStable();

        expect(service().profile('prose')).toMatchObject({ font: 'atkinson', width: 100, size: DEFAULT_READING.prose.size + 1 });
        expect(service().profile('code')).toEqual(DEFAULT_READING.code);
        expect(service().preferences().theme).toBe('sepia');
        expect(element.querySelector('output')!.textContent).toContain(`${DEFAULT_READING.prose.size + 1} px`);
        expect(element.querySelector('.reading-theme.active')!.getAttribute('data-reader-theme')).toBe('sepia');
    });

    it('asks for the name of a font of the device', async () => {
        const { fixture, element } = await panel('code');
        expect(element.querySelector('input[type="text"]')).toBeNull();

        choose(element.querySelector('select')!, 'custom');
        await settle();
        await fixture.whenStable();
        const name = element.querySelector<HTMLInputElement>('input[type="text"]')!;
        name.value = 'Fira Code';
        name.dispatchEvent(new Event('input'));

        expect(service().preferences().customFont).toBe('Fira Code');
        expect(service().style('code')['--reader-font']).toBe('"Fira Code", var(--mono)');
    });

    it('closes on Esc and on a press outside it, not on one inside it', async () => {
        const { element, closed } = await panel();

        element.querySelector('select')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
        expect(closed).not.toHaveBeenCalled();
        document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
        expect(closed).toHaveBeenCalledTimes(1);
        const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        element.querySelector('select')!.dispatchEvent(escape);
        expect(closed).toHaveBeenCalledTimes(2);
        expect(escape.defaultPrevented).toBe(true);
    });
});
