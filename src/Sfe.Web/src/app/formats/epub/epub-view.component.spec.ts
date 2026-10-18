import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { EPUB_FORMAT, FILE_VIEW_HOST } from '../formats';
import { READING_KEY } from '../reading/reading.service';
import { bookToken, EPUB_LOADER, EpubEvents, EpubMode, EpubOptions, EpubStyle } from './epub-document';
import { EPUB_MODE_KEY, EPUB_PLACES_KEY, EPUB_TOC_KEY, EpubViewComponent, readPlace, writePlace } from './epub-view.component';

/** A book as the view sees it, its moves recorded. */
function fakeHandle() {
    return {
        title: 'Hut stories' as string | null,
        author: 'Ana Ruiz' as string | null,
        toc: [
            { label: 'The climb', href: 'Text/one.xhtml', depth: 0 },
            { label: 'Night at the hut', href: 'Text/two.xhtml', depth: 0 },
            { label: 'Storm', href: 'Text/two.xhtml#storm', depth: 1 },
        ],
        next: vi.fn<() => void>(),
        prev: vi.fn<() => void>(),
        display: vi.fn<(target: string) => void>(),
        setMode: vi.fn<(mode: EpubMode) => Promise<void>>().mockResolvedValue(undefined),
        setStyle: vi.fn<(style: EpubStyle) => void>(),
        scrollBy: vi.fn<(pixels: number, pages?: boolean) => void>(),
        resize: vi.fn<() => void>(),
        destroy: vi.fn<() => void>(),
    };
}

describe('EpubViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<EpubViewComponent>;
    let element: HTMLElement;
    let events: EpubEvents;
    let opened: { stage: HTMLElement; url: string; options: EpubOptions }[];
    let handle: ReturnType<typeof fakeHandle>;
    let failure: Error | null;

    const KEYS = [READING_KEY, EPUB_MODE_KEY, EPUB_PLACES_KEY, EPUB_TOC_KEY];

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(name = 'Books/Hut stories.epub'): Promise<void> {
        fixture = TestBed.createComponent(EpubViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', { ...item(name, { kind: 'file' }), size: 480_000 });
        fixture.componentRef.setInput('format', EPUB_FORMAT);
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
        for (const key of KEYS)
            localStorage.removeItem(key);
        api = mockApi();
        opened = [];
        failure = null;
        handle = fakeHandle();
        const loader = async (stage: HTMLElement, url: string, options: EpubOptions, given: EpubEvents) => {
            events = given;
            opened.push({ stage, url, options });
            if (failure)
                throw failure;
            return handle;
        };
        TestBed.configureTestingModule({
            providers: testProviders(api,
                { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => true } },
                { provide: EPUB_LOADER, useValue: loader }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
        for (const key of KEYS)
            localStorage.removeItem(key);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('opens the book from its files served by the app, page by page at first', async () => {
        await show();

        expect(opened).toHaveLength(1);
        expect(opened[0].url).toMatch(new RegExp(`api/files/epub/${bookToken('Books/Hut stories.epub')}/$`));
        expect(opened[0].stage.classList).toContain('epub-stage');
        expect(opened[0].options.mode).toBe('pages');
        expect(opened[0].options.start).toBeNull();
        expect(opened[0].options.style.fontSize).toBe(17);
        // The prose font, even before the view's own style is applied (not reader.css' monospace default).
        expect(opened[0].options.style.fontFamily).not.toContain('mono');
        expect(opened[0].options.style.fontFamily).not.toContain('var(');
        expect(element.querySelector('.text-status')!.textContent).toContain('EPUB  ·  Hut stories  ·  Ana Ruiz');
        expect(element.querySelector('[data-progress]')!.textContent).toBe('…');
        expect(element.classList).toContain('prose');
        expect(element.hasAttribute('data-owns-keys')).toBe(true);

        fixture.destroy();
        expect(handle.destroy).toHaveBeenCalled();
    });

    it('turns the pages with the buttons, the arrows and keys pressed in the pages', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-next]')!.click();
        await keydown(element.querySelector('.epub-stage')!, 'ArrowLeft');
        events.key(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
        events.swipe(1);

        expect(handle.next).toHaveBeenCalledTimes(3);
        expect(handle.prev).toHaveBeenCalledTimes(1);
    });

    it('scrolls instead in the scroll layout, and keeps the choice', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('.markdown-mode[data-mode="scroll"]')!.click();
        await stable();
        expect(handle.setMode).toHaveBeenCalledWith('scroll');
        expect(localStorage.getItem(EPUB_MODE_KEY)).toBe('scroll');
        expect(element.getAttribute('data-mode')).toBe('scroll');

        await keydown(element.querySelector('.epub-stage')!, 'ArrowDown');
        await keydown(element.querySelector('.epub-stage')!, 'PageDown');
        expect(handle.scrollBy.mock.calls).toEqual([[48], [1, true]]);
        expect(handle.next).not.toHaveBeenCalled();

        element.querySelector<HTMLButtonElement>('.markdown-mode[data-mode="spread"]')!.click();
        await stable();
        expect(handle.setMode).toHaveBeenLastCalledWith('spread');

        fixture.destroy();
        await show();
        expect(opened.at(-1)!.options.mode).toBe('spread');
    });

    it('shows how far, the chapter, and opens again where it was read', async () => {
        await show();

        events.relocated({ cfi: 'epubcfi(/6/4!/4/2/1:0)', href: 'Text/two.xhtml', percentage: 0.42, atStart: false, atEnd: false });
        await stable();
        expect(element.querySelector('[data-progress]')!.textContent).toBe('42 %');
        expect(element.querySelector('[data-chapter]')!.textContent).toBe('Night at the hut');
        await vi.waitFor(() => expect(readPlace('Books/Hut stories.epub')).toBe('epubcfi(/6/4!/4/2/1:0)'));

        fixture.destroy();
        await show();
        expect(opened.at(-1)!.options.start).toBe('epubcfi(/6/4!/4/2/1:0)');
    });

    it('goes to the headings of its contents', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-toc]')!.click();
        await stable();
        const links = [...element.querySelectorAll<HTMLAnchorElement>('.epub-toc a')];
        expect(links.map(link => link.textContent)).toEqual(['The climb', 'Night at the hut', 'Storm']);
        links[2].click();

        expect(handle.display).toHaveBeenCalledWith('Text/two.xhtml#storm');
        expect(localStorage.getItem(EPUB_TOC_KEY)).toBe('1');
    });

    it('gives the pages the reading preferences as they change', async () => {
        await show();

        await keydown(element.querySelector('.epub-stage')!, '=', { ctrlKey: true });

        expect(handle.setStyle).toHaveBeenLastCalledWith(expect.objectContaining({ fontSize: 18 }));
    });

    it('remembers the latest hundred books only', () => {
        for (let index = 0; index < 101; index++)
            writePlace(`Books/${index}.epub`, `epubcfi(/6/${index})`);

        expect(readPlace('Books/0.epub')).toBeNull();
        expect(readPlace('Books/100.epub')).toBe('epubcfi(/6/100)');
        expect(readPlace('Books/none.epub')).toBeNull();
    });

    it('offers a download when the book cannot be opened', async () => {
        failure = new Error('Not a book');
        await show();

        expect(element.querySelector('.text-message')!.textContent).toContain('could not be opened');
        expect(element.querySelector('.text-message a')!.getAttribute('href')).toContain('download=true');
    });

    it('writes the book\'s path as the server reads it', () => {
        expect(bookToken('Books/Hut stories.epub')).toBe('Qm9va3MvSHV0IHN0b3JpZXMuZXB1Yg');
        expect(bookToken('Libros/Año?.epub')).not.toMatch(/[+/=]/);
    });
});
