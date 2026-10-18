import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api.service';
import { TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, FileFormat, PROSE_FORMAT, TEXT_FORMAT } from '../formats';
import { DEFAULT_READING } from '../reading/reading';
import { READING_KEY, ReadingService } from '../reading/reading.service';
import { TextViewComponent } from './text-view.component';

describe('TextViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TextViewComponent>;
    let element: HTMLElement;
    let rights: Set<string>;

    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: 'class A\r\n{\r\n}\r\n', encoding: 'windows-1252', lineEnding: 'crlf', version: 'v1', size: 2048, modified: '2026-10-18T10:00:00Z', ...overrides,
    });
    const button = (label: string) => element.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
    const status = () => element.querySelector('.text-status')?.textContent ?? '';

    async function show(name: string, format: FileFormat = TEXT_FORMAT): Promise<void> {
        fixture = TestBed.createComponent(TextViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(`Code/${name}`, { kind: 'file' }));
        fixture.componentRef.setInput('format', format);
        await settle();
        await fixture.whenStable();
    }

    async function press(key: string, target: Element): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key, ctrlKey: true, bubbles: true, cancelable: true });
        target.dispatchEvent(event);
        await settle();
        await fixture.whenStable();
        return event;
    }

    beforeEach(async () => {
        localStorage.removeItem(READING_KEY);
        api = mockApi();
        rights = new Set(['files.view', 'files.download']);
        TestBed.configureTestingModule({
            providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: (right: string) => rights.has(right) } }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
        localStorage.removeItem(READING_KEY);
        document.head.querySelectorAll('link[data-reader-fonts]').forEach(link => link.remove());
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('shows the text read only, with its language, encoding, line ending and size', async () => {
        api.textFile.mockResolvedValue(text());
        await show('Program.cs');

        expect(api.textFile).toHaveBeenCalledWith('Code/Program.cs', expect.any(AbortSignal));
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
        expect(element.querySelector('.cm-content')!.textContent).toContain('class A');
        expect(element.querySelector('.cm-content')!.getAttribute('aria-readonly')).toBe('true');
        expect(element.querySelectorAll('.cm-lineNumbers .cm-gutterElement').length).toBeGreaterThan(1);
        await vi.waitFor(() => expect(status()).toContain('C#'));
        expect(status()).toContain('Windows-1252');
        expect(status()).toContain('CRLF');
        expect(status()).toContain('Ln 1, Col 1');
    });

    it('wraps long lines and hides line numbers on demand, for the next files too', async () => {
        api.textFile.mockResolvedValue(text({ text: 'plain', encoding: 'utf-8', lineEnding: 'lf' }));
        await show('notes.txt');
        expect(status()).toContain('Plain text');

        button('Wrap long lines').click();
        button('Line numbers').click();
        await settle();

        expect(element.querySelector('.cm-content')!.classList).toContain('cm-lineWrapping');
        expect(element.querySelector('.cm-lineNumbers')).toBeNull();
        fixture.destroy();
        await show('other.txt');
        expect(element.querySelector('.cm-content')!.classList).toContain('cm-lineWrapping');
        expect(button('Wrap long lines').getAttribute('aria-pressed')).toBe('true');
        expect(TestBed.inject(ReadingService).profile('code')).toMatchObject({ wrap: true, lineNumbers: false });
    });

    it('reads notes with the prose preferences: wrapped, in a reading font, without line numbers', async () => {
        api.textFile.mockResolvedValue(text({ text: 'Dear diary', encoding: 'utf-8', lineEnding: 'lf' }));
        await show('notes.txt', PROSE_FORMAT);

        expect(element.classList).toContain('prose');
        expect(element.style.getPropertyValue('--reader-font')).toContain('Literata');
        expect(element.style.getPropertyValue('--reader-width')).toBe('72ch');
        expect(element.querySelector('.cm-content')!.classList).toContain('cm-lineWrapping');
        expect(element.querySelector('.cm-lineNumbers')).toBeNull();
        expect(document.head.querySelector('link[data-reader-fonts]')).not.toBeNull();
    });

    it('changes the size of the text with Ctrl+= / Ctrl+− / Ctrl+0 in it, not the page', async () => {
        api.textFile.mockResolvedValue(text());
        await show('Program.cs');
        const content = element.querySelector('.cm-content')!;

        const larger = await press('=', content);
        await press('+', content);
        expect(larger.defaultPrevented).toBe(true);
        expect(element.style.getPropertyValue('--reader-size')).toBe(`${DEFAULT_READING.code.size + 2}px`);
        await press('-', content);
        expect(element.style.getPropertyValue('--reader-size')).toBe(`${DEFAULT_READING.code.size + 1}px`);
        await press('0', content);
        expect(element.style.getPropertyValue('--reader-size')).toBe(`${DEFAULT_READING.code.size}px`);
    });

    it('opens the reading settings with Aa, and takes their colours', async () => {
        api.textFile.mockResolvedValue(text());
        await show('Program.cs');
        expect(element.getAttribute('data-reader-theme')).toBe('app');

        button('Reading settings').click();
        await settle();
        await fixture.whenStable();
        expect(element.querySelector('sfe-reading-panel')).not.toBeNull();
        expect(button('Reading settings').getAttribute('aria-expanded')).toBe('true');
        element.querySelector<HTMLButtonElement>('.reading-theme[data-reader-theme="dark"]')!.click();
        await settle();
        await fixture.whenStable();

        expect(element.getAttribute('data-reader-theme')).toBe('dark');
        button('Reading settings').click();
        await settle();
        await fixture.whenStable();
        expect(element.querySelector('sfe-reading-panel')).toBeNull();
    });

    it('finds in the text with Ctrl+F, even when the focus is elsewhere in the viewer', async () => {
        api.textFile.mockResolvedValue(text());
        await show('Program.cs');

        const event = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
        window.dispatchEvent(event);
        await settle();

        expect(event.defaultPrevented).toBe(true);
        expect(element.querySelector('.cm-search')).not.toBeNull();
    });

    it('offers to download a file too large to be shown or that is not text', async () => {
        api.textFile.mockRejectedValue(new ApiError(413, 'too large', null, 'too-large'));
        await show('huge.log');

        expect(element.querySelector('.text-message')!.textContent).toContain('too large');
        expect(element.querySelector<HTMLAnchorElement>('.text-message a')!.getAttribute('href')).toContain('download=true');
        expect(element.querySelector('.cm-editor')).toBeNull();

        fixture.destroy();
        rights.delete('files.download');
        api.textFile.mockRejectedValue(new ApiError(415, 'not text', null, 'not-text'));
        await show('data.bin');

        expect(element.querySelector('.text-message')!.textContent).toContain('not text');
        expect(element.querySelector('.text-message a')).toBeNull();
    });
});
