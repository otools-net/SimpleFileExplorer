import { convertToParamMap } from '@angular/router';
import { currentSort, readRestoreState, readRoute, routeParams } from './library-route';

describe('library route', () => {
    it('reads the query string, ignoring unknown sorts', () => {
        expect(readRoute(convertToParamMap({ path: 'a/b', q: 'x', sort: 'size', desc: '1' }))).toEqual({ path: 'a/b', q: 'x', sort: 'size', desc: true });
        expect(readRoute(convertToParamMap({ sort: 'nope' }))).toEqual({ path: '', q: '', sort: '', desc: false });
    });

    it('leaves the defaults out of the URL', () => {
        expect(routeParams({ path: '', q: '', sort: '', desc: false })).toEqual({ path: null, q: null, sort: null, desc: null });
    });

    it('sorts searches by path and folders by name by default; a folder has no path order', () => {
        expect(currentSort({ path: '', q: 'x', sort: '', desc: false })).toBe('path');
        expect(currentSort({ path: '', q: '', sort: '', desc: false })).toBe('name');
        expect(currentSort({ path: '', q: '', sort: 'path', desc: false })).toBe('name');
    });

    it('reads only the values of the right type from a history state', () => {
        expect(readRestoreState({ scrollY: 120, loaded: 'x', anchor: 'a/b.jpg', anchorTop: '5', navigationId: 3 }))
            .toEqual({ scrollY: 120, loaded: undefined, anchor: 'a/b.jpg', anchorTop: undefined });
        expect(readRestoreState(null)).toEqual({});
    });
});
