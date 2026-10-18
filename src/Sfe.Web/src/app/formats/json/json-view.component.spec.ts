import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EditorView } from '@codemirror/view';
import { ApiError } from '../../core/api.service';
import { TextAccess, TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, JSON_FORMAT } from '../formats';
import { READING_KEY } from '../reading/reading.service';
import { JSON_MODE_KEY, JsonViewComponent } from './json-view.component';

const DOCUMENT = '{\n  "name": "Trip",\n  "id": 12345678901234567890,\n  "days": [\n    { "town": "Annecy", "km": 42 },\n    { "town": "Chamonix", "km": 18 }\n  ]\n}\n';

describe('JsonViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<JsonViewComponent>;
    let element: HTMLElement;

    const ALL: TextAccess = { overwrite: true, saveCopy: true, download: true, permanent: false };
    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: DOCUMENT, encoding: 'utf-8', lineEnding: 'lf', version: 'v1', size: 140, modified: '2026-10-18T10:00:00Z', access: ALL, ...overrides,
    });
    const tree = () => element.querySelector<HTMLElement>('.json-tree')!;
    const rows = () => [...element.querySelectorAll<HTMLElement>('.json-row[role="treeitem"]')];
    const row = (path: string) => element.querySelector<HTMLElement>(`.json-row[data-path="${path}"]`)!;
    const keysShown = () => rows().map(shown => shown.querySelector('.json-key')?.textContent ?? '$');
    const mode = (label: string) => [...element.querySelectorAll<HTMLButtonElement>('.markdown-mode')].find(button => button.textContent === label)!;
    const view = () => EditorView.findFromDOM(element.querySelector<HTMLElement>('.json-source .cm-editor')!)!;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(name = 'trip.json'): Promise<void> {
        fixture = TestBed.createComponent(JsonViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(`Docs/${name}`, { kind: 'file' }));
        fixture.componentRef.setInput('format', JSON_FORMAT);
        await stable();
    }

    async function keydown(target: Element, key: string, init: KeyboardEventInit = {}): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
        target.dispatchEvent(event);
        await stable();
        return event;
    }

    beforeEach(async () => {
        for (const key of [READING_KEY, JSON_MODE_KEY])
            localStorage.removeItem(key);
        api = mockApi();
        api.textFile.mockResolvedValue(text());
        TestBed.configureTestingModule({ providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => true } }) });
        await loadConfig(api);
    });

    afterEach(() => {
        for (const key of [READING_KEY, JSON_MODE_KEY])
            localStorage.removeItem(key);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('shows a small file as a tree, all open, values as written', async () => {
        await show();

        expect(keysShown()).toEqual(['$', 'name', 'id', 'days', '0', 'town', 'km', '1', 'town', 'km']);
        expect(row('/id').querySelector('.json-value')!.textContent).toBe('12345678901234567890');
        expect(row('/name').querySelector('.json-value')!.classList).toContain('tok-string');
        expect(row('/days').querySelector('.json-size')!.textContent).toBe('2 items');
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
        expect(element.querySelector('.text-status')!.textContent).toContain('JSON');
        expect(element.querySelector('.cm-editor')).toBeNull();
    });

    it('opens and closes with the mouse and the arrows, and tells the path selected', async () => {
        await show();

        row('/days').querySelector<HTMLButtonElement>('.json-toggle')!.click();
        await stable();
        expect(keysShown()).toEqual(['$', 'name', 'id', 'days']);
        expect(row('/days').querySelector('.json-preview')!.textContent).toBe('[ {…}, {…} ]');

        // Open again: what was open inside stays open.
        await keydown(tree(), 'ArrowRight');
        expect(keysShown()).toHaveLength(10);
        await keydown(tree(), 'ArrowRight');
        await keydown(tree(), 'ArrowRight');
        expect(row('/days/0/town').getAttribute('aria-selected')).toBe('true');
        expect(tree().getAttribute('aria-activedescendant')).toBe(row('/days/0/town').id);
        expect(element.querySelector('[data-selected-path]')!.textContent).toBe('$.days[0].town');

        await keydown(tree(), 'ArrowLeft');
        expect(row('/days/0').getAttribute('aria-selected')).toBe('true');
        const up = await keydown(tree(), 'ArrowUp');
        expect(up.defaultPrevented).toBe(true);
        expect(row('/days').getAttribute('aria-selected')).toBe('true');
    });

    it('filters on keys and values', async () => {
        await show();
        const box = element.querySelector<HTMLInputElement>('.json-filter')!;
        box.value = 'chamonix';
        box.dispatchEvent(new Event('input'));

        await vi.waitFor(async () => {
            await stable();
            expect(keysShown()).toEqual(['$', 'days', '1', 'town']);
        });
        expect(element.querySelector('.json-filter-count')!.textContent).toBe('1 match');

        const escape = await keydown(box, 'Escape');
        expect(escape.defaultPrevented).toBe(true);
        await vi.waitFor(async () => {
            await stable();
            expect(keysShown()).toHaveLength(10);
        });
    });

    it('copies the path and the value as written', async () => {
        const writeText = vi.fn().mockResolvedValue(undefined);
        Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true });
        Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
        await show();

        row('/days/1').click();
        await stable();
        element.querySelector<HTMLButtonElement>('[data-copy-path]')!.click();
        await stable();
        element.querySelector<HTMLButtonElement>('[data-copy-value]')!.click();
        await stable();

        expect(writeText.mock.calls).toEqual([['$.days[1]'], ['{ "town": "Chamonix", "km": 18 }']]);
    });

    it('tells where it isn\'t JSON and shows that place in the source', async () => {
        api.textFile.mockResolvedValue(text({ text: '{\n  "a": 1\n  "b": 2\n}' }));
        await show();

        expect(element.querySelector('[data-problem]')!.textContent).toContain('Not JSON at line 3, column 3: A comma is expected');
        expect(keysShown()).toEqual(['$', 'a', 'b']);

        element.querySelector<HTMLButtonElement>('[data-problem] button')!.click();
        await stable();
        await vi.waitFor(() => expect(view().state.selection.main.from).toBe(13));
        expect(element.getAttribute('data-mode')).toBe('split');
    });

    it('reads comments and trailing commas as JSON with comments', async () => {
        api.textFile.mockResolvedValue(text({ text: '{\n  // the port\n  "port": 80,\n}' }));
        await show('appsettings.json');

        expect(element.querySelector('[data-problem]')).toBeNull();
        expect(element.querySelector('.text-status')!.textContent).toContain('JSON with comments');
    });

    it('shows its source, or both, and remembers the choice', async () => {
        await show();

        mode('Source').click();
        await stable();
        expect(element.querySelector<HTMLElement>('.json-pane')!.hidden).toBe(true);
        expect(element.querySelector('.cm-content')!.textContent).toContain('"Chamonix"');
        mode('Split').click();
        await stable();
        expect(element.querySelector<HTMLElement>('.json-pane')!.hidden).toBe(false);
        expect(localStorage.getItem(JSON_MODE_KEY)).toBe('split');

        const find = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
        window.dispatchEvent(find);
        await stable();
        expect(find.defaultPrevented).toBe(true);
        expect(element.querySelector('.cm-search')).not.toBeNull();
    });

    it('edits the source beside the tree, which follows it; Format lays it out', async () => {
        api.textFile.mockResolvedValue(text({ text: '{"a":1}' }));
        await show();

        element.querySelector<HTMLButtonElement>('[data-edit]')!.click();
        await stable();
        expect(element.getAttribute('data-mode')).toBe('split');
        await vi.waitFor(() => expect(view().state.readOnly).toBe(false));

        view().dispatch({ changes: { from: 6, insert: ',"b":[true]' } });
        // A container added while typing shows closed.
        await vi.waitFor(() => expect(keysShown()).toEqual(['$', 'a', 'b']));
        expect(row('/b').querySelector('.json-preview')!.textContent).toBe('[ true ]');

        element.querySelector<HTMLButtonElement>('[data-format]')!.click();
        await stable();
        expect(view().state.doc.toString()).toBe('{\n  "a": 1,\n  "b": [\n    true\n  ]\n}');
        expect(element.querySelector('[data-edited]')).not.toBeNull();
        expect(localStorage.getItem(JSON_MODE_KEY)).toBeNull();
    });

    it('offers a download when the file cannot be shown', async () => {
        api.textFile.mockRejectedValue(new ApiError(413, 'too large', null, 'too-large'));
        await show('huge.json');

        expect(element.querySelector('.text-message')!.textContent).toContain('too large');
        expect(element.querySelector('.text-message a')!.getAttribute('href')).toContain('download=true');
    });
});
