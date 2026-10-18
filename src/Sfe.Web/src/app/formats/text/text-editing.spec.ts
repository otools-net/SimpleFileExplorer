import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EditorView } from '@codemirror/view';
import { ApiError } from '../../core/api.service';
import { TextAccess, TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, MARKDOWN_FORMAT, TEXT_FORMAT } from '../formats';
import { MARKDOWN_MODE_KEY, MarkdownViewComponent } from '../markdown/markdown-view.component';
import { TextViewComponent } from './text-view.component';
import { copyName, UNSAVED_QUESTION } from './text-session';

describe('Editing text', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TextViewComponent | MarkdownViewComponent>;
    let element: HTMLElement;
    let guard: (() => boolean) | null;
    let released: boolean;
    const open = vi.fn();

    const ALL: TextAccess = { overwrite: true, saveCopy: true, download: true, permanent: false };
    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: 'first\r\nsecond\r\n', encoding: 'windows-1252', lineEnding: 'crlf', saveLineEnding: 'crlf', version: 'v1', size: 15,
        modified: '2026-10-18T10:00:00Z', access: ALL, ...overrides,
    });
    const action = (name: string) => element.querySelector<HTMLButtonElement>(`[data-${name}]`);
    const bar = () => element.querySelector<HTMLElement>('[data-bar]');
    const view = () => EditorView.findFromDOM(element.querySelector<HTMLElement>('.text-editor .cm-editor')!)!;

    async function show(name = 'notes.txt', edit = false, component: Type<TextViewComponent | MarkdownViewComponent> = TextViewComponent): Promise<void> {
        fixture = TestBed.createComponent(component);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(`Docs/${name}`, { kind: 'file' }));
        fixture.componentRef.setInput('format', component === TextViewComponent ? TEXT_FORMAT : MARKDOWN_FORMAT);
        fixture.componentRef.setInput('edit', edit);
        await stable();
    }

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function type(insert: string): Promise<void> {
        const editor = view();
        editor.dispatch({ changes: { from: editor.state.doc.length, insert } });
        await stable();
    }

    async function key(key: string, init: KeyboardEventInit = {}): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
        (element.querySelector('.cm-content') ?? element).dispatchEvent(event);
        await stable();
        return event;
    }

    beforeEach(async () => {
        api = mockApi();
        guard = null;
        released = false;
        open.mockReset();
        TestBed.configureTestingModule({
            providers: testProviders(api, {
                provide: FILE_VIEW_HOST,
                useValue: {
                    place: 'viewer',
                    can: () => true,
                    open,
                    guard: (canLeave: () => boolean) => {
                        guard = canLeave;
                        return () => released = true;
                    },
                },
            }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
        fixture?.destroy();
        TestBed.resetTestingModule();
        localStorage.removeItem(MARKDOWN_MODE_KEY);
        vi.restoreAllMocks();
    });

    it('names a copy after the file', () => {
        expect(copyName('notes.txt')).toBe('notes (copy).txt');
        expect(copyName('Makefile')).toBe('Makefile (copy)');
        expect(copyName('.env')).toBe('.env (copy)');
    });

    it('changes the text after Edit and saves it over the file, as it was encoded, with the version it started from', async () => {
        api.textFile.mockResolvedValue(text());
        api.saveText.mockResolvedValue({ version: 'v2', size: 21, modified: '2026-10-18T11:00:00Z', trashId: 7 });
        await show();
        expect(view().state.readOnly).toBe(true);

        action('edit')!.click();
        await stable();
        expect(view().state.readOnly).toBe(false);
        expect(action('save')!.disabled).toBe(true);

        await type('third\n');
        expect(element.querySelector('[data-edited]')).not.toBeNull();
        action('save')!.click();
        await stable();

        const [path, saved, session, version] = api.saveText.mock.calls[0];
        expect(path).toBe('Docs/notes.txt');
        expect(saved).toEqual({ text: 'first\nsecond\nthird\n', encoding: 'windows-1252', lineEnding: 'crlf' });
        expect(session).toMatch(/^[0-9a-f]{32}$/);
        expect(version).toBe('v1');
        expect(element.querySelector('[data-edited]')).toBeNull();

        // The next save names the new version, in the same session (one previous version kept in the trash).
        await type('fourth');
        await key('s', { ctrlKey: true });
        expect(api.saveText.mock.calls[1][2]).toBe(session);
        expect(api.saveText.mock.calls[1][3]).toBe('v2');
    });

    it('takes Ctrl+S from the browser even with nothing to save, and starts editing with E', async () => {
        api.textFile.mockResolvedValue(text());
        await show();

        expect((await key('s', { ctrlKey: true })).defaultPrevented).toBe(true);
        expect(api.saveText).not.toHaveBeenCalled();
        expect((await key('e')).defaultPrevented).toBe(true);
        expect(view().state.readOnly).toBe(false);
    });

    it('offers nothing to edit when nothing could be done with the changes', async () => {
        api.textFile.mockResolvedValue(text({ access: { overwrite: false, saveCopy: false, download: false, permanent: false } }));
        await show();

        expect(action('edit')).toBeNull();
        expect((await key('e')).defaultPrevented).toBe(false);
        expect(view().state.readOnly).toBe(true);
    });

    it('saves a copy when the file may not be saved over, then opens the copy ready to be changed', async () => {
        api.textFile.mockResolvedValue(text({ access: { overwrite: false, saveCopy: true, download: true, permanent: false } }));
        const copy = item('Docs/mine.txt', { kind: 'file' });
        api.saveTextCopy.mockResolvedValueOnce({ type: 'file-exists', detail: 'A file named “notes (copy).txt” is there already.' }).mockResolvedValueOnce(copy);
        await show('notes.txt', true);

        expect(action('save')).toBeNull();
        expect(bar()!.dataset['bar']).toBe('copy-only');
        await type('more');
        await key('s', { ctrlKey: true });
        const input = bar()!.querySelector('input')!;
        expect(bar()!.dataset['bar']).toBe('copy');
        expect(input.value).toBe('notes (copy).txt');

        bar()!.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
        await stable();
        expect(bar()!.textContent).toContain('is there already');
        expect(open).not.toHaveBeenCalled();

        input.value = 'mine.txt';
        input.dispatchEvent(new Event('input'));
        bar()!.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
        await stable();

        expect(api.saveTextCopy).toHaveBeenLastCalledWith('Docs', 'mine.txt', { text: 'first\nsecond\nmore', encoding: 'windows-1252', lineEnding: 'crlf' });
        expect(open).toHaveBeenCalledWith(copy, { edit: true });
        expect(guard!()).toBe(true);
    });

    it('asks what to do when the file changed elsewhere: overwrite it, or read it again', async () => {
        api.textFile.mockResolvedValueOnce(text()).mockResolvedValueOnce(text({ text: 'theirs\r\n', version: 'v9' }));
        api.saveText.mockRejectedValueOnce(new ApiError(412, 'It changed.', null, 'file-changed'))
            .mockResolvedValueOnce({ version: 'v3', size: 20, modified: '2026-10-18T11:00:00Z' });
        await show('notes.txt', true);
        await type('mine');

        await key('s', { ctrlKey: true });
        expect(bar()!.dataset['bar']).toBe('changed');
        expect(element.querySelector('[data-edited]')).not.toBeNull();

        [...bar()!.querySelectorAll('button')].find(button => button.textContent === 'Overwrite')!.click();
        await stable();
        expect(api.saveText.mock.calls[1][3]).toBe('*');
        expect(bar()).toBeNull();

        await type(' again');
        api.saveText.mockRejectedValueOnce(new ApiError(412, 'It changed.', null, 'file-changed'));
        await key('s', { ctrlKey: true });
        [...bar()!.querySelectorAll('button')].find(button => button.textContent === 'Reload')!.click();
        await stable();
        expect(view().state.doc.toString()).toBe('theirs\n');
        expect(element.querySelector('[data-edited]')).toBeNull();
    });

    it('offers UTF-8 when the encoding of the file can not hold the text', async () => {
        api.textFile.mockResolvedValue(text());
        api.saveText.mockRejectedValueOnce(new ApiError(422, 'Some characters can’t be written in windows-1252: 東', null, 'not-encodable'))
            .mockResolvedValueOnce({ version: 'v2', size: 20, modified: '2026-10-18T11:00:00Z' });
        await show('notes.txt', true);
        await type('東');

        await key('s', { ctrlKey: true });
        expect(bar()!.textContent).toContain('東');
        bar()!.querySelector<HTMLButtonElement>('button.button')!.click();
        await stable();

        expect(api.saveText.mock.calls[1][1].encoding).toBe('utf-8');
        expect(bar()).toBeNull();
    });

    it('asks before changes are lost: leaving the file, or Done', async () => {
        api.textFile.mockResolvedValue(text());
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        await show('notes.txt', true);
        expect(guard!()).toBe(true);

        await type('draft');
        expect(guard!()).toBe(false);
        expect(confirm).toHaveBeenCalledWith(UNSAVED_QUESTION);
        const unload = new Event('beforeunload', { cancelable: true });
        window.dispatchEvent(unload);
        expect(unload.defaultPrevented).toBe(true);

        action('done')!.click();
        await stable();
        expect(view().state.doc.toString()).toContain('draft');

        confirm.mockReturnValue(true);
        action('done')!.click();
        await stable();
        expect(view().state.doc.toString()).toBe('first\nsecond\n');
        expect(view().state.readOnly).toBe(true);
        expect(guard!()).toBe(true);

        fixture.destroy();
        expect(released).toBe(true);
    });

    it('edits Markdown beside the document, which follows the source', async () => {
        api.textFile.mockResolvedValue(text({ text: '# Title\n', encoding: 'utf-8', lineEnding: 'lf', saveLineEnding: 'lf' }));
        await show('readme.md', false, MarkdownViewComponent);
        expect(element.dataset['mode']).toBe('rendered');

        action('edit')!.click();
        await stable();
        expect(element.dataset['mode']).toBe('split');
        await vi.waitFor(() => expect(view().state.readOnly).toBe(false));

        await type('\n## Added\n');
        await vi.waitFor(() => expect(element.querySelector('.markdown-content h2')?.textContent).toBe('Added'));
        expect(localStorage.getItem(MARKDOWN_MODE_KEY)).toBeNull();
    });
});
