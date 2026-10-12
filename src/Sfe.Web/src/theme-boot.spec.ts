import { setMediaQuery } from './testing/fakes';

/** Runs the script again, as on a page load. */
async function boot(): Promise<string | undefined> {
    vi.resetModules();
    // @ts-expect-error: a script, not a module (it has no export)
    await import('./theme-boot');
    return document.documentElement.dataset['theme'];
}

describe('theme boot script', () => {
    beforeEach(() => {
        localStorage.clear();
        delete document.documentElement.dataset['theme'];
        setMediaQuery('(prefers-color-scheme: light)', false);
        history.replaceState(null, '', '/');
    });

    afterEach(() => vi.restoreAllMocks());

    it('follows the device without a pick or a known default', async () => {
        expect(await boot()).toBe('dark');
        setMediaQuery('(prefers-color-scheme: light)', true);
        expect(await boot()).toBe('light');
    });

    it('prefers this browser\'s pick over the last server default', async () => {
        localStorage.setItem('sfe.theme.default', 'dark');
        expect(await boot()).toBe('dark');
        localStorage.setItem('sfe.theme', 'light');
        expect(await boot()).toBe('light');
    });

    it('ignores unknown stored values, and "system" follows the device', async () => {
        setMediaQuery('(prefers-color-scheme: light)', true);
        localStorage.setItem('sfe.theme', 'purple');
        expect(await boot()).toBe('light');
        localStorage.setItem('sfe.theme', '');
        localStorage.setItem('sfe.theme.default', 'system');
        expect(await boot()).toBe('light');
    });

    it('keeps the player page dark whatever was picked, also under a virtual directory', async () => {
        localStorage.setItem('sfe.theme', 'light');
        history.replaceState(null, '', '/player?path=a.mp4');
        expect(await boot()).toBe('dark');
        history.replaceState(null, '', '/sfe/player');
        expect(await boot()).toBe('dark');
        // Only the page itself, not a folder named so.
        history.replaceState(null, '', '/player/other');
        expect(await boot()).toBe('light');
    });

    it('still sets a theme when storage is unavailable', async () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('denied', 'SecurityError'); });
        setMediaQuery('(prefers-color-scheme: light)', true);
        expect(await boot()).toBe('light');
    });
});
