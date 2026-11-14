import { convertToParamMap } from '@angular/router';
import { currentSort, readRestoreState, readRoute, routeParams } from './library-route';

describe('library route', () => {
    it('reads the query string, ignoring unknown sorts', () => {
        expect(readRoute(convertToParamMap({ path: 'a/b', q: 'x', sort: 'size', desc: '1' }))).toEqual({
            c: null,
            path: 'a/b',
            q: 'x',
            sort: 'size',
            desc: true,
        });
        expect(readRoute(convertToParamMap({ sort: 'nope' }))).toEqual({
            c: null,
            path: '',
            q: '',
            sort: '',
            desc: false,
        });
    });

    it('reads a connection, and writes it only when there is one', () => {
        expect(readRoute(convertToParamMap({ c: '7', path: 'a' })).c).toBe(7);
        for (const c of ['0', '-1', 'x', '1.5']) expect(readRoute(convertToParamMap({ c })).c).toBeNull();
        expect(routeParams({ c: 7, path: 'a', q: '', sort: '', desc: false })['c']).toBe(7);
        expect('c' in routeParams({ c: null, path: 'a', q: '', sort: '', desc: false })).toBe(false);
    });

    it('leaves the defaults out of the URL', () => {
        expect(routeParams({ path: '', q: '', sort: '', desc: false })).toEqual({
            path: null,
            q: null,
            sort: null,
            desc: null,
        });
    });

    it('sorts searches by path and folders by name by default; a folder has no path order', () => {
        expect(currentSort({ path: '', q: 'x', sort: '', desc: false })).toBe('path');
        expect(currentSort({ path: '', q: '', sort: '', desc: false })).toBe('name');
        expect(currentSort({ path: '', q: '', sort: 'path', desc: false })).toBe('name');
    });

    it('keeps the duration and resolution sorts for searches, and lists folders by name instead', () => {
        expect(readRoute(convertToParamMap({ q: 'x', sort: 'duration' })).sort).toBe('duration');
        expect(readRoute(convertToParamMap({ q: 'x', sort: 'resolution' })).sort).toBe('resolution');
        expect(currentSort({ path: '', q: 'x', sort: 'duration', desc: true })).toBe('duration');
        expect(currentSort({ path: '', q: 'x', sort: 'resolution', desc: false })).toBe('resolution');
        expect(currentSort({ path: 'a', q: '', sort: 'duration', desc: false })).toBe('name');
        expect(currentSort({ path: 'a', q: '', sort: 'resolution', desc: false })).toBe('name');
    });

    it('reads only the values of the right type from a history state', () => {
        expect(
            readRestoreState({ scrollY: 120, loaded: 'x', anchor: 'a/b.jpg', anchorTop: '5', navigationId: 3 }),
        ).toEqual({ scrollY: 120, loaded: undefined, anchor: 'a/b.jpg', anchorTop: undefined });
        expect(readRestoreState(null)).toEqual({});
    });
});
