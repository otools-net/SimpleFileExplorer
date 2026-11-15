import { LibraryRoute } from '../library-route';
import { scopeEntries, scopeText, scopeTitle } from './search-scope';

const route = (change: Partial<LibraryRoute>): LibraryRoute => ({
    path: '',
    q: 'cats',
    sort: '',
    desc: false,
    ...change,
});
const library = { location: 'Media', everywhere: true };

describe('the search scope', () => {
    it('names the folder, the location or everywhere', () => {
        expect(scopeText(route({ path: 'Photos/2020' }), library)).toBe('in 2020');
        expect(scopeText(route({}), library)).toBe('in Media');
        expect(scopeText(route({ c: 3 }), { location: 'Dropbox', everywhere: false })).toBe('in Dropbox');
        expect(scopeText(route({ all: true, path: 'Photos' }), library)).toBe('everywhere');
        expect(scopeText(route({}), { location: 'Media', everywhere: false }), 'nothing to choose').toBe('');
        expect(scopeText(route({ q: '' }), library), 'not searching, at the root').toBe('');
        expect(scopeTitle(route({ c: 3 }), { location: 'Dropbox', everywhere: true })).toContain('Searching Dropbox');
    });

    it('offers the folder, the whole location and everywhere, the current one checked', () => {
        const go = vi.fn();
        const entries = scopeEntries(route({ c: 3, path: 'Photos' }), { location: 'Dropbox', everywhere: true }, go);

        expect(entries.map((e) => [e.id, e.checked])).toEqual([
            ['folder', true],
            ['location', false],
            ['everywhere', false],
        ]);
        expect(entries[1].label).toBe('All of Dropbox');
        entries[1].run!();
        expect(go).toHaveBeenCalledWith({ path: '', all: false });
        entries[2].run!();
        expect(go).toHaveBeenCalledWith({ all: true });
    });

    it('offers no everywhere without connections, and checks it while searching everywhere', () => {
        const without = scopeEntries(route({}), { location: 'Media', everywhere: false }, vi.fn());
        const everywhere = scopeEntries(route({ all: true }), library, vi.fn());

        expect(without.map((e) => e.id)).toEqual(['location']);
        expect(everywhere.find((e) => e.checked)?.id).toBe('everywhere');
    });
});
