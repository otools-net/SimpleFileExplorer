import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EditorView } from '@codemirror/view';
import { ApiError } from '../../core/api.service';
import { TextAccess, TextFile, TextPart } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ChangeFeed } from '../../library/change-feed.service';
import { FILE_VIEW_HOST, isTextFile, setTextExtensions, TEXT_FORMAT } from '../formats';
import { POSITIONS_KEY, readPosition, writePosition } from './positions';
import { FOLLOW_MS, PART_BYTES, TextViewComponent } from './text-view.component';
import { DISK_CHECK_MS } from './text-session';

describe('Text files: large files, following, encodings, changes elsewhere, positions', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TextViewComponent>;
    let element: HTMLElement;

    const ALL: TextAccess = { overwrite: true, saveCopy: true, download: true, permanent: false };
    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: 'one\ntwo\n', encoding: 'utf-8', lineEnding: 'lf', saveLineEnding: 'lf', version: 'v1', size: 8,
        modified: '2026-10-18T10:00:00Z', access: ALL, ...overrides,
    });
    const part = (overrides: Partial<TextPart> = {}): TextPart => ({
        text: 'middle\n', offset: 0, end: 7, size: 50_000_000, encoding: 'utf-8', modified: '2026-10-18T10:00:00Z', ...overrides,
    });
    const doc = () => EditorView.findFromDOM(element.querySelector<HTMLElement>('.cm-editor')!)!.state.doc.toString();
    const button = (selector: string) => element.querySelector<HTMLButtonElement>(selector)!;

    async function show(name = 'notes.txt', path = `Docs/${name}`): Promise<void> {
        fixture = TestBed.createComponent(TextViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(path, { kind: 'file' }));
        fixture.componentRef.setInput('format', TEXT_FORMAT);
        await stable();
    }

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function choose(selector: string, value: string): Promise<void> {
        const select = element.querySelector<HTMLSelectElement>(selector)!;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        await stable();
    }

    beforeEach(async () => {
        localStorage.removeItem(POSITIONS_KEY);
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => true } }) });
        await loadConfig(api);
    });

    afterEach(() => {
        vi.useRealTimers();
        fixture?.destroy();
        TestBed.resetTestingModule();
        localStorage.removeItem(POSITIONS_KEY);
        setTextExtensions([]);
        vi.restoreAllMocks();
    });

    it('reads a file too large to be read whole in parts at whole lines; a log from its end', async () => {
        api.textFile.mockRejectedValue(new ApiError(413, 'too large', null, 'too-large'));
        api.textPart.mockResolvedValueOnce(part({ text: 'first\n', offset: 0, end: 262_000 }))
            .mockResolvedValueOnce(part({ text: 'second\n', offset: 262_000, end: 524_000 }))
            .mockResolvedValueOnce(part({ text: 'last\n', offset: 49_800_000, end: 50_000_000 }));
        await show('huge.txt');

        expect(api.textPart).toHaveBeenLastCalledWith('Docs/huge.txt', 0, PART_BYTES, undefined, expect.any(AbortSignal));
        expect(doc()).toBe('first\n');
        expect(element.querySelector('[data-part-range]')!.textContent).toContain('of 47.7 MB');
        expect(element.querySelector('sfe-text-edit-actions')).toBeNull();
        expect(button('[data-part="previous"]').disabled).toBe(true);

        button('[data-part="next"]').click();
        await stable();
        expect(api.textPart).toHaveBeenLastCalledWith('Docs/huge.txt', 262_000, PART_BYTES, 'utf-8', expect.any(AbortSignal));
        expect(doc()).toBe('second\n');

        button('[data-part="end"]').click();
        await stable();
        expect(api.textPart.mock.lastCall![1]).toBe(-1);
        expect(button('[data-part="next"]').disabled).toBe(true);

        api.textPart.mockResolvedValue(part({ offset: 49_537_856, end: 49_800_000 }));
        button('[data-part="previous"]').click();
        await stable();
        expect(api.textPart.mock.lastCall!.slice(1, 3)).toEqual([49_800_000 - PART_BYTES, PART_BYTES]);

        fixture.destroy();
        api.textPart.mockClear();
        await show('server.log');
        expect(api.textPart.mock.lastCall![1]).toBe(-1);
    });

    it('follows what is added to the end of a file, then reads it whole again', async () => {
        api.textFile.mockResolvedValueOnce(text()).mockResolvedValue(text({ text: 'one\ntwo\nthree\nfour\n', version: 'v3', size: 19 }));
        await show('app.log');
        vi.useFakeTimers({ shouldAdvanceTime: true });
        api.textPart.mockResolvedValueOnce(part({ text: 'three\n', offset: 8, end: 14, size: 14 }))
            .mockResolvedValueOnce(part({ text: '', offset: 14, end: 14, size: 14 }));

        button('[data-follow]').click();
        await stable();
        expect(element.querySelector('[data-edit]')).toBeNull();
        await vi.advanceTimersByTimeAsync(FOLLOW_MS);
        await stable();

        expect(api.textPart).toHaveBeenCalledWith('Docs/app.log', 8, PART_BYTES, 'utf-8', expect.any(AbortSignal));
        expect(doc()).toBe('one\ntwo\nthree\n');
        await vi.advanceTimersByTimeAsync(FOLLOW_MS);
        expect(api.textPart.mock.lastCall![1]).toBe(14);

        button('[data-follow]').click();
        await stable();
        expect(doc()).toBe('one\ntwo\nthree\nfour\n');
        expect(element.querySelector('[data-edit]')).not.toBeNull();
        const calls = api.textPart.mock.calls.length;
        await vi.advanceTimersByTimeAsync(FOLLOW_MS * 2);
        expect(api.textPart.mock.calls.length).toBe(calls);
    });

    it('reads a file again in another encoding, and saves in the encoding and line endings chosen', async () => {
        api.textFile.mockResolvedValueOnce(text({ text: 'cafÃ©\n' })).mockResolvedValueOnce(text({ text: 'café\n', encoding: 'windows-1252' }));
        api.saveText.mockResolvedValue({ version: 'v2', size: 6, modified: '2026-10-18T11:00:00Z' });
        await show();

        await choose('[data-read-encoding]', 'windows-1252');
        expect(api.textFile).toHaveBeenLastCalledWith('Docs/notes.txt', undefined, 'windows-1252');
        expect(doc()).toBe('café\n');
        expect(element.querySelector<HTMLSelectElement>('[data-read-encoding]')!.value).toBe('windows-1252');

        button('[data-edit]').click();
        await stable();
        await choose('[data-save-encoding]', 'utf-8-bom');
        await choose('[data-save-line-ending]', 'crlf');
        const editor = EditorView.findFromDOM(element.querySelector<HTMLElement>('.cm-editor')!)!;
        editor.dispatch({ changes: { from: 0, insert: '!' } });
        await stable();
        button('[data-save]').click();
        await stable();

        expect(api.saveText.mock.lastCall![1]).toEqual({ text: '!café\n', encoding: 'utf-8-bom', lineEnding: 'crlf' });
    });

    it('tells of a change made elsewhere: through SFE at once, otherwise when checked', async () => {
        api.textFile.mockResolvedValueOnce(text()).mockResolvedValueOnce(text({ text: 'theirs\n', version: 'v9' }));
        api.textVersion.mockResolvedValue({ version: 'v1', size: 8, modified: '2026-10-18T10:00:00Z' });
        vi.useFakeTimers({ shouldAdvanceTime: true });
        await show();

        await vi.advanceTimersByTimeAsync(DISK_CHECK_MS);
        expect(api.textVersion).toHaveBeenLastCalledWith('Docs/notes.txt', 'v1');
        expect(element.querySelector('[data-bar]')).toBeNull();

        api.textVersion.mockResolvedValue({ version: 'v9', size: 7, modified: '2026-10-18T12:00:00Z' });
        TestBed.inject(ChangeFeed).changed({ path: 'Docs', origin: 'another-tab' });
        await stable();
        expect(element.querySelector<HTMLElement>('[data-bar]')!.dataset['bar']).toBe('disk');

        [...element.querySelectorAll<HTMLButtonElement>('[data-bar] button')].find(b => b.textContent === 'Reload')!.click();
        await stable();
        expect(doc()).toBe('theirs\n');
        expect(element.querySelector('[data-bar]')).toBeNull();
    });

    it('does not ask again about a version kept', async () => {
        api.textFile.mockResolvedValue(text());
        api.textVersion.mockResolvedValue({ version: 'v9', size: 7, modified: '2026-10-18T12:00:00Z' });
        await show();
        TestBed.inject(ChangeFeed).changed({ path: 'Docs', origin: 'another-tab' });
        await stable();

        [...element.querySelectorAll<HTMLButtonElement>('[data-bar] button')].find(b => b.textContent === 'Keep this one')!.click();
        await stable();
        TestBed.inject(ChangeFeed).changed({ path: 'Docs', origin: 'another-tab' });
        await stable();

        expect(element.querySelector('[data-bar]')).toBeNull();
        expect(doc()).toBe('one\ntwo\n');
    });

    it('opens a file where it was last read, remembering a hundred files at most', async () => {
        writePosition('Docs/notes.txt', 40);
        api.textFile.mockResolvedValue(text({ text: Array.from({ length: 80 }, (_, i) => `line ${i + 1}`).join('\n') }));
        await show();

        expect(element.querySelector('.text-status')!.textContent).toContain('Ln 40, Col 1');

        for (let i = 0; i < 100; i++)
            writePosition(`Docs/${i}.txt`, 2);
        expect(readPosition('Docs/notes.txt')).toBeNull();
        expect(readPosition('Docs/99.txt')).toBe(2);
        writePosition('Docs/99.txt', 1);
        expect(readPosition('Docs/99.txt')).toBeNull();
    });

    it('shows the library\'s own extensions as text', () => {
        expect(isTextFile(item('Data/run.out', { kind: 'file' }))).toBe(false);
        setTextExtensions(['out', '.DAT']);
        expect(isTextFile(item('Data/run.out', { kind: 'file' }))).toBe(true);
        expect(isTextFile(item('Data/a.dat', { kind: 'file' }))).toBe(true);
        expect(isTextFile(item('Data/a.out', { kind: 'video' }))).toBe(false);
    });
});
