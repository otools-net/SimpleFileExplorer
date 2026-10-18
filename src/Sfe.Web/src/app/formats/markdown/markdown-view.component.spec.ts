import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api.service';
import { LibraryItem, TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, MARKDOWN_FORMAT } from '../formats';
import { READING_KEY } from '../reading/reading.service';
import { MARKDOWN_MODE_KEY, MARKDOWN_OUTLINE_KEY, MarkdownViewComponent } from './markdown-view.component';

const DOCUMENT = [
    '# Trip notes',
    '',
    'Read the [packing list](Packing%20list.txt) first, then [the route](#the-route).',
    '',
    '![Summit](https://maps/summit.png)',
    '',
    '## The route',
    '',
    'Up the ridge.',
    '',
].join('\n');

describe('MarkdownViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<MarkdownViewComponent>;
    let element: HTMLElement;
    let opened: LibraryItem[];

    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: DOCUMENT, encoding: 'utf-8', lineEnding: 'lf', version: 'v1', size: 300, modified: '2026-10-18T10:00:00Z', ...overrides,
    });
    const article = () => element.querySelector<HTMLElement>('article.markdown-body')!;
    const mode = (label: string) => [...element.querySelectorAll<HTMLButtonElement>('.markdown-mode')].find(button => button.textContent === label)!;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(): Promise<void> {
        fixture = TestBed.createComponent(MarkdownViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item('Docs/Trip notes.md', { kind: 'file' }));
        fixture.componentRef.setInput('format', MARKDOWN_FORMAT);
        await stable();
        await vi.waitFor(() => expect(element.querySelector('.text-status')).not.toBeNull());
    }

    function click(target: Element, init: MouseEventInit = {}): MouseEvent {
        const event = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ...init });
        target.dispatchEvent(event);
        return event;
    }

    beforeEach(async () => {
        for (const key of [READING_KEY, MARKDOWN_MODE_KEY, MARKDOWN_OUTLINE_KEY])
            localStorage.removeItem(key);
        api = mockApi();
        api.textFile.mockResolvedValue(text());
        opened = [];
        TestBed.configureTestingModule({
            providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => true, open: (shown: LibraryItem) => opened.push(shown) } }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
        for (const key of [READING_KEY, MARKDOWN_MODE_KEY, MARKDOWN_OUTLINE_KEY])
            localStorage.removeItem(key);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('shows the document rendered, with an outline of its headings and the prose settings', async () => {
        await show();

        expect(article().querySelector('h1')!.textContent).toBe('Trip notes');
        expect([...element.querySelectorAll('.markdown-outline a')].map(a => a.textContent)).toEqual(['Trip notes', 'The route']);
        expect(element.classList).toContain('prose');
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
        expect(element.querySelector('.text-status')!.textContent).toContain('Markdown');
        expect(element.querySelector('.cm-editor')).toBeNull();
    });

    it('opens the documents it links to in its place, unless the click asks for a new tab', async () => {
        await show();
        const link = [...article().querySelectorAll('a')].find(a => a.textContent === 'packing list')!;

        const plain = click(link);
        const newTab = click(link, { ctrlKey: true });

        expect(plain.defaultPrevented).toBe(true);
        expect(newTab.defaultPrevented).toBe(false);
        expect(opened.map(shown => [shown.path, shown.kind])).toEqual([['Docs/Packing list.txt', 'file']]);
        expect(link.getAttribute('href')).toBe('view?path=Docs%2FPacking+list.txt');
    });

    it('scrolls to the headings its links and its outline name', async () => {
        await show();
        const heading = article().querySelector<HTMLElement>('#md-the-route')!;
        const scrolled = vi.fn();
        heading.scrollIntoView = scrolled;

        const fromLink = click([...article().querySelectorAll('a')].find(a => a.textContent === 'the route')!);
        click([...element.querySelectorAll('.markdown-outline a')].find(a => a.textContent === 'The route')!);

        expect(fromLink.defaultPrevented).toBe(true);
        expect(scrolled).toHaveBeenCalledTimes(2);
    });

    it('holds back the images of other sites until asked', async () => {
        await show();
        expect(article().querySelector('img')).toBeNull();
        expect(element.querySelector('.markdown-external')!.textContent).toContain('1 image of other sites not shown');

        element.querySelector<HTMLButtonElement>('.markdown-external button')!.click();
        await stable();

        expect(article().querySelector('img')!.getAttribute('src')).toBe('https://maps/summit.png');
        expect(element.querySelector('.markdown-external')).toBeNull();
    });

    it('shows its source, or both, and remembers the choice', async () => {
        await show();

        mode('Source').click();
        await stable();
        expect(article().hidden).toBe(true);
        expect(element.querySelector('.cm-content')!.textContent).toContain('# Trip notes');
        mode('Split').click();
        await stable();

        expect(article().hidden).toBe(false);
        expect(element.querySelector<HTMLElement>('.markdown-source')!.hidden).toBe(false);
        expect(element.getAttribute('data-mode')).toBe('split');
        expect(localStorage.getItem(MARKDOWN_MODE_KEY)).toBe('split');
        const find = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
        window.dispatchEvent(find);
        await stable();
        expect(find.defaultPrevented).toBe(true);
        expect(element.querySelector('.cm-search')).not.toBeNull();
    });

    it('leaves Ctrl+F to the browser while only the document is shown', async () => {
        await show();

        const find = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
        window.dispatchEvent(find);

        expect(find.defaultPrevented).toBe(false);
    });

    it('offers a download when the file cannot be shown', async () => {
        api.textFile.mockRejectedValue(new ApiError(413, 'too large', null, 'too-large'));
        fixture = TestBed.createComponent(MarkdownViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item('Docs/huge.md', { kind: 'file' }));
        await stable();

        expect(element.querySelector('.text-message')!.textContent).toContain('too large');
        expect(element.querySelector('.text-message a')!.getAttribute('href')).toContain('download=true');
    });
});
