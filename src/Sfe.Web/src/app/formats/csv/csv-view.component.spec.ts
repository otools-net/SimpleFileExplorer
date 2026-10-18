import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EditorView } from '@codemirror/view';
import { ApiError } from '../../core/api.service';
import { TextAccess, TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { CSV_FORMAT, FILE_VIEW_HOST } from '../formats';
import { READING_KEY } from '../reading/reading.service';
import { CSV_HEADER_KEY, CSV_MODE_KEY, CsvViewComponent } from './csv-view.component';

const TABLE = 'day,town,km\n1,Annecy,12\n2,Chamonix,18\n3,Chamonix,"1,014"\n4,Zermatt,9\n';

describe('CsvViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<CsvViewComponent>;
    let element: HTMLElement;

    const ALL: TextAccess = { overwrite: true, saveCopy: true, download: true, permanent: false };
    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: TABLE, encoding: 'utf-8', lineEnding: 'lf', version: 'v1', size: 90, modified: '2026-10-18T10:00:00Z', access: ALL, ...overrides,
    });
    const scroller = () => element.querySelector<HTMLElement>('.csv-scroller')!;
    const headers = () => [...element.querySelectorAll('.csv-head .csv-name')].map(name => name.textContent);
    const column = (index: number) => [...element.querySelectorAll(`.csv-body [data-column="${index}"]`)].map(cell => cell.textContent);
    const status = () => element.querySelector('[data-counts]')!.textContent;
    const mode = (label: string) => [...element.querySelectorAll<HTMLButtonElement>('.markdown-mode')].find(button => button.textContent === label)!;
    const view = () => EditorView.findFromDOM(element.querySelector<HTMLElement>('.json-source .cm-editor')!)!;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(name = 'trip.csv'): Promise<void> {
        fixture = TestBed.createComponent(CsvViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(`Docs/${name}`, { kind: 'file' }));
        fixture.componentRef.setInput('format', CSV_FORMAT);
        await stable();
    }

    async function keydown(target: Element, key: string, init: KeyboardEventInit = {}): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
        target.dispatchEvent(event);
        await stable();
        return event;
    }

    beforeEach(async () => {
        for (const key of [READING_KEY, CSV_MODE_KEY, CSV_HEADER_KEY])
            localStorage.removeItem(key);
        api = mockApi();
        api.textFile.mockResolvedValue(text());
        TestBed.configureTestingModule({ providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => true } }) });
        await loadConfig(api);
    });

    afterEach(() => {
        for (const key of [READING_KEY, CSV_MODE_KEY, CSV_HEADER_KEY])
            localStorage.removeItem(key);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('shows a table: the header row naming the columns, numbers to the right', async () => {
        await show();

        expect(headers()).toEqual(['day', 'town', 'km']);
        expect(column(1)).toEqual(['Annecy', 'Chamonix', 'Chamonix', 'Zermatt']);
        expect(column(2)).toEqual(['12', '18', '1,014', '9']);
        expect(element.querySelector('.csv-body [data-column="2"]')!.classList).toContain('numeric');
        expect(status()).toBe('4 rows × 3 columns · comma');
        expect(element.querySelector('.text-status')!.textContent).toContain('CSV');
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
        expect(element.querySelector('.cm-editor')).toBeNull();
    });

    it('sorts by a column: ascending, descending, then as in the file', async () => {
        await show();
        const sort = () => element.querySelector<HTMLButtonElement>('.csv-sort[data-column="2"]')!;

        sort().click();
        await stable();
        expect(column(2)).toEqual(['9', '12', '18', '1,014']);
        expect(sort().parentElement!.getAttribute('aria-sort')).toBe('ascending');
        sort().click();
        await stable();
        expect(column(2)).toEqual(['1,014', '18', '12', '9']);
        sort().click();
        await stable();
        expect(column(2)).toEqual(['12', '18', '1,014', '9']);
    });

    it('filters rows, and says how many of all', async () => {
        await show();
        const box = element.querySelector<HTMLInputElement>('.json-filter')!;
        box.value = 'chamonix';
        box.dispatchEvent(new Event('input'));

        await vi.waitFor(async () => {
            await stable();
            expect(column(0)).toEqual(['2', '3']);
        });
        expect(status()).toBe('2 of 4 rows × 3 columns · comma');
        await keydown(box, 'Escape');
        await vi.waitFor(async () => {
            await stable();
            expect(column(0)).toHaveLength(4);
        });
    });

    it('reads the first row as a row when asked, and remembers it', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-header]')!.click();
        await stable();

        expect(headers()).toEqual(['A', 'B', 'C']);
        expect(column(1)[0]).toBe('town');
        expect(localStorage.getItem(CSV_HEADER_KEY)).toBe('0');
    });

    it('reads tab separated files, and another separator when chosen', async () => {
        api.textFile.mockResolvedValue(text({ text: 'a\tb;c\n1\t2;3\n' }));
        await show('data.tsv');
        expect(headers()).toEqual(['a', 'b;c']);
        expect(element.querySelector('.text-status')!.textContent).toContain('TSV');

        const select = element.querySelector<HTMLSelectElement>('[data-delimiter]')!;
        select.value = ';';
        select.dispatchEvent(new Event('change'));
        await stable();
        expect(headers()).toEqual(['a\tb', 'c']);
    });

    it('moves between cells with the arrows and copies the one selected', async () => {
        const writeText = vi.fn().mockResolvedValue(undefined);
        Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true });
        Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
        await show();

        element.querySelector<HTMLElement>('.csv-body [data-record="1"] [data-column="1"]')!.click();
        await stable();
        await keydown(scroller(), 'ArrowDown');
        await keydown(scroller(), 'ArrowDown');
        await keydown(scroller(), 'ArrowRight');
        expect(element.querySelector('.csv-cell.selected')!.textContent).toBe('1,014');
        expect(element.querySelector('[data-selected-cell]')!.textContent).toBe('Row 3 · km: 1,014');

        const copy = await keydown(scroller(), 'c', { ctrlKey: true });
        expect(copy.defaultPrevented).toBe(true);
        expect(writeText).toHaveBeenCalledWith('1,014');
    });

    it('draws only the rows shown of a long table', async () => {
        const lines = ['n,square'];
        for (let n = 1; n <= 5000; n++)
            lines.push(`${n},${n * n}`);
        api.textFile.mockResolvedValue(text({ text: lines.join('\n') }));
        await show('squares.csv');

        const drawn = element.querySelectorAll('.csv-body .csv-row').length;
        expect(drawn).toBeGreaterThan(10);
        expect(drawn).toBeLessThan(100);
        expect(status()).toBe('5,000 rows × 2 columns · comma');

        await keydown(scroller(), 'End', { ctrlKey: true });
        expect(element.querySelector('[data-selected-cell]')!.textContent).toBe('Row 5,000 · square: 25000000');

        // Scrolled to the end (jsdom doesn't scroll by itself): the last rows are drawn instead of the first.
        Object.defineProperty(scroller(), 'scrollTop', { value: 28 * 4990, configurable: true });
        scroller().dispatchEvent(new Event('scroll'));
        await vi.waitFor(async () => {
            await stable();
            expect(element.querySelector('.csv-cell.selected')?.textContent).toBe('25000000');
        });
        expect(element.querySelector('.csv-body [data-record="1"]')).toBeNull();
    });

    it('tells which record is wrong', async () => {
        api.textFile.mockResolvedValue(text({ text: 'a,b\n1,"unclosed\n' }));
        await show();

        expect(element.querySelector('[data-problem]')!.textContent).toContain('Record 2:');
    });

    it('edits the source beside the table, which follows it', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-edit]')!.click();
        await stable();
        expect(element.getAttribute('data-mode')).toBe('split');
        await vi.waitFor(() => expect(view().state.readOnly).toBe(false));

        view().dispatch({ changes: { from: view().state.doc.length, insert: '5,Bern,7\n' } });
        await vi.waitFor(async () => {
            await stable();
            expect(column(1)).toContain('Bern');
        });
        expect(element.querySelector('[data-edited]')).not.toBeNull();
        expect(localStorage.getItem(CSV_MODE_KEY)).toBeNull();
    });

    it('shows its source and remembers the choice', async () => {
        await show();

        mode('Source').click();
        await stable();
        expect(element.querySelector<HTMLElement>('.json-pane')!.hidden).toBe(true);
        expect(element.querySelector('.cm-content')!.textContent).toContain('Zermatt');
        expect(localStorage.getItem(CSV_MODE_KEY)).toBe('source');
    });

    it('offers a download when the file cannot be shown', async () => {
        api.textFile.mockRejectedValue(new ApiError(413, 'too large', null, 'too-large'));
        await show('huge.csv');

        expect(element.querySelector('.text-message')!.textContent).toContain('too large');
        expect(element.querySelector('.text-message a')!.getAttribute('href')).toContain('download=true');
    });
});
