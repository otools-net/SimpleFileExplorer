import { takeSetupLink } from './setup-link';

function view(search: string, hash = ''): { location: Location; history: History; replaced: string[] } {
    const replaced: string[] = [];
    return {
        location: { search, pathname: '/sfe/', hash } as Location,
        history: {
            state: null,
            replaceState: (_state: unknown, _title: string, url: string) => replaced.push(url),
        } as unknown as History,
        replaced,
    };
}

function storage(): Storage {
    const values = new Map<string, string>();
    return {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => void values.set(key, value),
    } as Storage;
}

describe('takeSetupLink', () => {
    it('takes the token from the address and removes it from there', () => {
        const opened = view('?setup=the-token');

        expect(takeSetupLink(opened, storage())).toBe('the-token');
        expect(opened.replaced).toEqual(['/sfe/']);
    });

    it('keeps the rest of the address', () => {
        const opened = view('?path=Media&setup=the-token', '#top');

        takeSetupLink(opened, storage());

        expect(opened.replaced).toEqual(['/sfe/?path=Media#top']);
    });

    it('still has the token after a reload of the tab', () => {
        const tab = storage();
        takeSetupLink(view('?setup=the-token'), tab);
        const reloaded = view('');

        expect(takeSetupLink(reloaded, tab)).toBe('the-token');
        expect(reloaded.replaced).toEqual([]);
    });

    it('has none when the page was opened without it', () => {
        expect(takeSetupLink(view('?path=Media'), storage())).toBeNull();
    });

    it('works without storage', () => {
        expect(takeSetupLink(view('?setup=the-token'), null)).toBe('the-token');
    });
});
