import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api.service';
import { TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, TEXT_FORMAT } from '../formats';
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

    async function show(name: string): Promise<void> {
        fixture = TestBed.createComponent(TextViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(`Code/${name}`, { kind: 'file' }));
        fixture.componentRef.setInput('format', TEXT_FORMAT);
        await settle();
        await fixture.whenStable();
    }

    beforeEach(async () => {
        api = mockApi();
        rights = new Set(['files.view', 'files.download']);
        TestBed.configureTestingModule({
            providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: (right: string) => rights.has(right) } }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
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
        // Back as it was, for the other tests.
        button('Wrap long lines').click();
        button('Line numbers').click();
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
