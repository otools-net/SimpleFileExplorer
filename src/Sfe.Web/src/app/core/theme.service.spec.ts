import { TestBed } from '@angular/core/testing';
import { setMediaQuery } from '../../testing/fakes';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
    const theme = () => document.documentElement.dataset['theme'];
    const create = () => TestBed.inject(ThemeService);

    beforeEach(() => {
        localStorage.clear();
        setMediaQuery('(prefers-color-scheme: light)', false);
        TestBed.configureTestingModule({});
    });

    it('follows the device by default', () => {
        const service = create();

        expect(theme()).toBe('dark');
        expect([service.current(), service.defaultName()]).toEqual(['', 'follows device']);
    });

    it('follows the device live while on the default', () => {
        create();

        setMediaQuery('(prefers-color-scheme: light)', true);

        expect(theme()).toBe('light');
    });

    it('starts from the server default it saw last time, as theme-boot.ts does', () => {
        localStorage.setItem('sfe.theme.default', 'light');

        expect(create().defaultName()).toBe('light');
        expect(theme()).toBe('light');
    });

    it('applies the server default and remembers it for the next page load', () => {
        const service = create();

        service.setDefault('light');

        expect(theme()).toBe('light');
        expect(localStorage.getItem('sfe.theme.default')).toBe('light');
        service.setDefault(null);
        expect(localStorage.getItem('sfe.theme.default')).toBe('system');
        expect(theme()).toBe('dark');
    });

    it('applies this browser\x27s pick over the default, and goes back to the default', () => {
        const service = create();
        service.setDefault('dark');

        service.choose('light');
        expect([theme(), service.current()]).toEqual(['light', 'light']);
        expect(localStorage.getItem('sfe.theme')).toBe('light');
        service.choose('');
        expect([theme(), service.current(), service.defaultName()]).toEqual(['dark', '', 'dark']);
        expect(localStorage.getItem('sfe.theme')).toBe('');
    });

    it('keeps this browser\'s pick when the server default changes', () => {
        const service = create();
        service.choose('light');
        expect(theme()).toBe('light');

        service.setDefault('dark');
        setMediaQuery('(prefers-color-scheme: light)', false);

        expect(theme()).toBe('light');
    });

    it('ignores unknown stored values', () => {
        localStorage.setItem('sfe.theme', 'purple');
        localStorage.setItem('sfe.theme.default', 'sepia');

        const service = create();
        expect([service.current(), service.defaultName()]).toEqual(['', 'follows device']);
    });

    it('still works when storage is unavailable', () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied'); });
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied'); });
        const service = create();

        service.setDefault('light');
        expect(theme()).toBe('light');
        // The pick can't be stored, so it doesn't stick.
        service.choose('dark');
        expect(theme()).toBe('light');
        vi.restoreAllMocks();
    });
});
