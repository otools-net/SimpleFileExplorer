import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { MenuEntry } from '../../menus/context-menu';
import { FILE_VIEW_HOST, PDF_FORMAT } from '../formats';
import { NotPdfError, PDF_LOADER, PdfEvents, PdfOutlineItem } from './pdf-document';
import { PDF_DARK_KEY, PDF_OUTLINE_KEY, PDF_PAGES_KEY, PdfViewComponent } from './pdf-view.component';

/** A document as the view sees it, its moves recorded. */
function fakeHandle(go: () => void) {
    return {
        pages: 12,
        title: 'Hiking guide' as string | null,
        outline: [{ title: 'Before leaving', items: [{ title: 'Weather', items: [], go }], go: () => undefined }] as PdfOutlineItem[],
        goTo: vi.fn<(page: number) => void>(),
        setScale: vi.fn<(scale: string | number) => void>(),
        find: vi.fn<(query: string, options: { previous?: boolean; again?: boolean }) => void>(),
        refresh: vi.fn<() => void>(),
        destroy: vi.fn<() => void>(),
    };
}

describe('PdfViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<PdfViewComponent>;
    let element: HTMLElement;
    let events: PdfEvents;
    let opened: { container: HTMLDivElement; url: string; start: number | null }[];
    let handle: ReturnType<typeof fakeHandle>;
    let failure: Error | null;
    let locked: boolean;
    const outlineGo = vi.fn();

    const status = () => element.querySelector('[data-page-status]')?.textContent;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(name = 'Docs/Guide.pdf'): Promise<void> {
        fixture = TestBed.createComponent(PdfViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', { ...item(name, { kind: 'file' }), size: 2_400_000 });
        fixture.componentRef.setInput('format', PDF_FORMAT);
        await stable();
        await stable();
    }

    async function keydown(target: Element, key: string, init: KeyboardEventInit = {}): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
        target.dispatchEvent(event);
        await stable();
        return event;
    }

    beforeEach(async () => {
        for (const key of [PDF_PAGES_KEY, PDF_DARK_KEY, PDF_OUTLINE_KEY])
            localStorage.removeItem(key);
        document.head.querySelectorAll('link[data-pdf-viewer]').forEach(link => link.remove());
        api = mockApi();
        opened = [];
        failure = null;
        locked = false;
        outlineGo.mockReset();
        handle = fakeHandle(outlineGo);
        const loader = async (container: HTMLDivElement, url: string, given: PdfEvents, start: number | null) => {
            events = given;
            opened.push({ container, url, start });
            if (locked) {
                let password = await given.password(false);
                while (password !== 'secret' && password !== null)
                    password = await given.password(true);
                if (password === null)
                    throw new Error('given up');
            }
            if (failure)
                throw failure;
            return handle;
        };
        TestBed.configureTestingModule({
            providers: testProviders(api,
                { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => true } },
                { provide: PDF_LOADER, useValue: loader }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
        for (const key of [PDF_PAGES_KEY, PDF_DARK_KEY, PDF_OUTLINE_KEY])
            localStorage.removeItem(key);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('opens the file from the app itself, with pdf.js\' styles linked once', async () => {
        await show();

        expect(opened).toHaveLength(1);
        expect(opened[0].url).toBe('api/files/raw/Docs/Guide.pdf');
        expect(opened[0].container.classList).toContain('pdf-container');
        expect(opened[0].start).toBeNull();
        expect(element.querySelector('.text-status')!.textContent).toContain('PDF  ·  12 pages  ·  2.3 MB  ·  Hiking guide');
        expect(status()).toBe('Page 1 of 12');
        expect(document.head.querySelectorAll('link[data-pdf-viewer]')).toHaveLength(1);
        expect(element.hasAttribute('data-owns-keys')).toBe(true);

        fixture.destroy();
        expect(handle.destroy).toHaveBeenCalled();
    });

    it('turns the pages, and opens again where it was read', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-next-page]')!.click();
        expect(handle.goTo).toHaveBeenLastCalledWith(2);
        await keydown(element.querySelector('.pdf-container')!, 'ArrowRight');
        expect(handle.goTo).toHaveBeenLastCalledWith(2);
        const box = element.querySelector<HTMLInputElement>('[data-page]')!;
        box.value = '7';
        await keydown(box, 'Enter');
        expect(handle.goTo).toHaveBeenLastCalledWith(7);

        events.page(7);
        await stable();
        expect(status()).toBe('Page 7 of 12');
        await vi.waitFor(() => expect(localStorage.getItem(PDF_PAGES_KEY)).toContain('"Docs/Guide.pdf":7'));

        fixture.destroy();
        await show();
        expect(opened.at(-1)!.start).toBe(7);
    });

    it('zooms in steps, to the width or the whole page, Ctrl+0 back to automatic', async () => {
        await show();
        events.scale(1);
        await stable();

        element.querySelector<HTMLButtonElement>('[data-zoom-in]')!.click();
        expect(handle.setScale).toHaveBeenLastCalledWith('1.25');
        const select = element.querySelector<HTMLSelectElement>('[data-scale]')!;
        select.value = 'page-width';
        select.dispatchEvent(new Event('change'));
        expect(handle.setScale).toHaveBeenLastCalledWith('page-width');
        const reset = await keydown(element.querySelector('.pdf-container')!, '0', { ctrlKey: true });
        expect(reset.defaultPrevented).toBe(true);
        expect(handle.setScale).toHaveBeenLastCalledWith('auto');
    });

    it('finds in the document, Enter for the next match', async () => {
        await show();
        const find = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
        window.dispatchEvent(find);
        expect(find.defaultPrevented).toBe(true);
        await stable();         // focused once shown (a phone's field shows under the bar first)
        const box = element.querySelector<HTMLInputElement>('[data-find]')!;
        expect(document.activeElement).toBe(box);
        expect(element.querySelector('.pdf-find')!.classList).toContain('open');

        box.value = 'ridge';
        box.dispatchEvent(new Event('input'));
        events.matches({ current: 1, total: 7 });
        await stable();
        expect(handle.find).toHaveBeenLastCalledWith('ridge', {});
        expect(element.querySelector('[data-matches]')!.textContent).toBe('1 of 7');

        await keydown(box, 'Enter', { shiftKey: true });
        expect(handle.find).toHaveBeenLastCalledWith('ridge', { again: true, previous: true });
    });

    it('shows its outline, which goes to its headings', async () => {
        await show();

        const links = [...element.querySelectorAll<HTMLAnchorElement>('.pdf-outline a')];
        expect(links.map(link => link.textContent)).toEqual(['Before leaving', 'Weather']);
        links[1].click();
        expect(outlineGo).toHaveBeenCalled();

        element.querySelector<HTMLButtonElement>('[data-outline]')!.click();
        await stable();
        expect(element.querySelector('.pdf-outline')).toBeNull();
        expect(localStorage.getItem(PDF_OUTLINE_KEY)).toBe('0');
    });

    it('asks for the password of a locked document, again when it is wrong', async () => {
        locked = true;
        await show();
        expect(element.querySelector('.pdf-password')!.textContent).toContain('protected by a password');

        element.querySelector<HTMLInputElement>('[data-password]')!.value = 'guess';
        element.querySelector<HTMLFormElement>('.pdf-password')!.dispatchEvent(new Event('submit'));
        await stable();
        expect(element.querySelector('.pdf-password')!.textContent).toContain('not its password');

        element.querySelector<HTMLInputElement>('[data-password]')!.value = 'secret';
        element.querySelector<HTMLFormElement>('.pdf-password')!.dispatchEvent(new Event('submit'));
        await stable();
        expect(status()).toBe('Page 1 of 12');
    });

    it('darkens the pages when asked, and remembers it', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-dark]')!.click();
        await stable();

        expect(element.classList).toContain('dark-pages');
        expect(localStorage.getItem(PDF_DARK_KEY)).toBe('1');
    });

    it('on a phone, opens the find field under the bar with its magnifier; Esc hides it again', async () => {
        await show();
        element.querySelector<HTMLButtonElement>('[data-find-toggle]')!.click();
        await stable();
        const box = element.querySelector<HTMLInputElement>('[data-find]')!;
        expect(element.querySelector('.pdf-find')!.classList).toContain('open');
        expect(document.activeElement).toBe(box);

        box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
        await stable();
        expect(element.querySelector('.pdf-find')!.classList).not.toContain('open');
    });

    it('gives the zoom list to the "⋯" menu as a submenu of sizes, the one used checked', async () => {
        await show();
        const more = (fixture.componentInstance as unknown as { more: { zoom: () => MenuEntry } }).more;
        const sizes = more.zoom().submenu!();
        expect(sizes.find(size => size.checked)?.label).toBe('Automatic');
        sizes.find(size => size.label === '150 %')!.run!();
        await stable();
        expect(more.zoom().label).toBe('Zoom: 150 %');
        expect(element.querySelector<HTMLSelectElement>('[data-scale]')!.value).toBe('1.5');
    });

    it('offers a download when the file isn\'t a PDF it can show', async () => {
        failure = new NotPdfError('Invalid PDF structure.');
        await show();

        expect(element.querySelector('.text-message')!.textContent).toContain('not a PDF that can be shown');
        expect(element.querySelector('.text-message a')!.getAttribute('href')).toContain('download=true');
    });
});
