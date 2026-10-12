import { TestBed } from '@angular/core/testing';
import { ApiMock, fakeConfig, loadConfig, mockApi, testProviders } from '../../testing/fakes';
import { CopyPathService } from './copy-path.service';
import { ToastService } from './toast.service';

describe('CopyPathService', () => {
    const secure = (value: boolean) => Object.defineProperty(window, 'isSecureContext', { value, configurable: true });
    let api: ApiMock;
    let service: CopyPathService;
    let toasts: ToastService;

    beforeEach(() => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        service = TestBed.inject(CopyPathService);
        toasts = TestBed.inject(ToastService);
    });

    afterEach(() => vi.restoreAllMocks());

    it('is off without a root path from the server', async () => {
        expect(service.enabled()).toBe(false);
        await loadConfig(api, fakeConfig({ rootPath: null }));
        expect(service.enabled()).toBe(false);
        await loadConfig(api, fakeConfig({ rootPath: '/srv/media/' }));
        expect(service.enabled()).toBe(true);
    });

    it('joins the path with the server\'s separator', async () => {
        await loadConfig(api, fakeConfig({ rootPath: 'D:\\Media\\' }));
        expect(service.fullPath('Films/2020/a b.mkv')).toBe('D:\\Media\\Films\\2020\\a b.mkv');

        await loadConfig(api, fakeConfig({ rootPath: '/srv/media/' }));
        expect(service.fullPath('Films/a.mkv')).toBe('/srv/media/Films/a.mkv');
    });

    it('gives the root without its separator, unless that leaves a bare drive or nothing', async () => {
        await loadConfig(api, fakeConfig({ rootPath: '/srv/media/' }));
        expect(service.fullPath('')).toBe('/srv/media');

        await loadConfig(api, fakeConfig({ rootPath: '/' }));
        expect(service.fullPath('')).toBe('/');
        expect(service.fullPath('a')).toBe('/a');

        await loadConfig(api, fakeConfig({ rootPath: 'C:\\' }));
        expect(service.fullPath('')).toBe('C:\\');
        expect(service.fullPath('a/b')).toBe('C:\\a\\b');

        await loadConfig(api, fakeConfig({ rootPath: '\\\\nas\\share\\' }));
        expect(service.fullPath('')).toBe('\\\\nas\\share');
    });

    it('copies with the clipboard on secure pages and says so', async () => {
        await loadConfig(api, fakeConfig({ rootPath: '/m/' }));
        const writeText = vi.fn(() => Promise.resolve());
        secure(true);
        Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
        const show = vi.spyOn(toasts, 'show');

        await service.copy('a/b.jpg');

        expect(writeText).toHaveBeenCalledWith('/m/a/b.jpg');
        expect(show).toHaveBeenCalledWith('Copied /m/a/b.jpg');
    });

    it('falls back to a selected text box inside the open dialog (the page around it is inert)', async () => {
        await loadConfig(api, fakeConfig({ rootPath: '/m/' }));
        secure(false);
        const dialog = document.body.appendChild(document.createElement('dialog'));
        dialog.showModal();
        let copiedFrom: Element | null = null;
        let copiedText = '';
        document.execCommand = vi.fn(() => {
            const box = document.querySelector('textarea')!;
            copiedFrom = box.parentElement;
            copiedText = box.value;
            return true;
        });

        await service.copy('x.jpg');

        expect(copiedFrom).toBe(dialog);
        expect(copiedText).toBe('/m/x.jpg');
        expect(document.querySelector('textarea')).toBeNull();
        dialog.remove();
    });

    it('also falls back when the clipboard refuses', async () => {
        await loadConfig(api, fakeConfig({ rootPath: '/m/' }));
        secure(true);
        Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('no focus')) }, configurable: true });
        document.execCommand = vi.fn(() => true);

        await service.copy('x.jpg');

        expect(document.execCommand).toHaveBeenCalledWith('copy');
    });

    it('reports a copy the browser refused', async () => {
        await loadConfig(api, fakeConfig({ rootPath: '/m/' }));
        secure(false);
        document.execCommand = vi.fn(() => false);
        const error = vi.spyOn(toasts, 'error');

        await service.copy('x.jpg');

        expect(error).toHaveBeenCalledWith('Could not copy /m/x.jpg');
        expect(document.querySelector('textarea')).toBeNull();
    });

    it('remembers the copied paths in memory only, and not a refused copy', async () => {
        await loadConfig(api, fakeConfig({ rootPath: '/m/' }));
        secure(false);
        document.execCommand = vi.fn(() => true);

        await service.copy('a/x.jpg');
        await service.copy('a');
        document.execCommand = vi.fn(() => false);
        await service.copy('y.jpg');

        expect([...service.copied()]).toEqual(['a/x.jpg', 'a']);
        expect(localStorage.length + sessionStorage.length).toBe(0);
    });
});
