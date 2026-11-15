import { ApiError } from '../../core/api.service';
import { LibraryItem } from '../../core/models';
import { EverywhereLocation, EverywherePage, EverywhereSearch } from './everywhere-search';

const library: EverywhereLocation = { c: null, name: 'Library' };
const dropbox: EverywhereLocation = { c: 3, name: 'Dropbox' };
const slow: EverywhereLocation = { c: 4, name: 'Slow' };

function item(path: string): LibraryItem {
    return { name: path.split('/').pop()!, path, kind: 'image', extension: 'jpg', size: 1, modified: null };
}

function page(paths: string[], next: string | null = null, total = paths.length): EverywherePage {
    return { query: 'q', path: '', items: paths.map(item), limit: 2, hasMore: next !== null, next, total };
}

/** A fetch whose answers the test gives, per location and cursor. */
function deferredFetch() {
    const waiting = new Map<string, { resolve: (p: EverywherePage) => void; reject: (e: unknown) => void }>();
    const signals: AbortSignal[] = [];
    const fetch = (location: EverywhereLocation, after: string | null, signal: AbortSignal) => {
        signals.push(signal);
        return new Promise<EverywherePage>((resolve, reject) => {
            waiting.set(`${location.c}:${after}`, { resolve, reject });
        });
    };
    return { fetch, waiting, signals };
}

const flush = () => new Promise((resolve) => setTimeout(resolve));

describe('EverywhereSearch', () => {
    it('asks every location at once and fills each group as its answer comes', async () => {
        const { fetch, waiting } = deferredFetch();
        const search = new EverywhereSearch(fetch);

        search.start([library, dropbox, slow]);
        expect([...waiting.keys()]).toEqual(['null:null', '3:null', '4:null']);
        expect(search.groups().map((g) => g.status)).toEqual(['loading', 'loading', 'loading']);

        waiting.get('3:null')!.resolve({ ...page(['a.jpg'], null, 1), coverage: 'partial' });
        await flush();
        const [first, second, third] = search.groups();
        expect(first.status).toBe('loading');
        expect(second).toEqual(expect.objectContaining({ status: 'ready', total: 1, coverage: 'partial' }));
        expect(third.status).toBe('loading');
    });

    it('shows a failing location as an error of its own, and retries it alone', async () => {
        const { fetch, waiting } = deferredFetch();
        const search = new EverywhereSearch(fetch);
        search.start([library, dropbox]);

        waiting.get('3:null')!.reject(new ApiError(429, 'Too many'));
        waiting.get('null:null')!.resolve(page(['x.jpg']));
        await flush();
        expect(search.groups().map((g) => g.status)).toEqual(['ready', 'error']);
        expect(search.groups()[1].error).toContain('try again');

        void search.retry(dropbox);
        expect(search.groups()[1].status).toBe('loading');
        waiting.get('3:null')!.resolve(page(['b.jpg']));
        await flush();
        expect(search.groups()[1].items.map((i) => i.path)).toEqual(['b.jpg']);
    });

    it('pages each group alone, more on demand', async () => {
        const { fetch, waiting } = deferredFetch();
        const search = new EverywhereSearch(fetch);
        search.start([library, dropbox]);
        waiting.get('null:null')!.resolve(page(['1.jpg', '2.jpg'], 'n1', 3));
        waiting.get('3:null')!.resolve(page(['d.jpg']));
        await flush();

        void search.more(library);
        expect(search.groups()[0].loadingMore).toBe(true);
        waiting.get('null:n1')!.resolve(page(['3.jpg']));
        await flush();

        const [own, other] = search.groups();
        expect(own.items.map((i) => i.path)).toEqual(['1.jpg', '2.jpg', '3.jpg']);
        expect(own.next).toBeNull();
        expect(own.total).toBe(3);
        expect(other.items.map((i) => i.path)).toEqual(['d.jpg']);
    });

    it('cancels the requests of the previous search when a new one starts', async () => {
        const { fetch, waiting, signals } = deferredFetch();
        const search = new EverywhereSearch(fetch);
        search.start([library]);
        const old = waiting.get('null:null')!;

        search.start([dropbox]);
        old.resolve(page(['late.jpg']));
        await flush();

        expect(signals[0].aborted).toBe(true);
        expect(search.groups().map((g) => g.location.c)).toEqual([3]);
        expect(search.groups()[0].status).toBe('loading');
    });
});
