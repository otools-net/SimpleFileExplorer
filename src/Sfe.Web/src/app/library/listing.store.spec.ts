import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ApiError } from '../core/api.service';
import { DirectoryListing, LibraryItem, SearchResults } from '../core/models';
import { ToastService } from '../core/toast.service';
import { ApiMock, deferred, fakeConfig, fileTags, item, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
import { Dialogs } from './dialogs.service';
import { ListingStore } from './listing.store';
import { SidebarService } from './sidebar/sidebar.service';
import { ViewerComponent } from './viewer/viewer.component';

function listing(items: LibraryItem[], overrides: Partial<DirectoryListing> = {}): DirectoryListing {
    return { path: '', name: 'Root', parentPath: null, items, offset: 0, limit: items.length, total: items.length, directoryCount: 0, fileCount: items.length, ...overrides };
}

function results(items: LibraryItem[], next: string | null = null): SearchResults {
    return { query: 'q', path: '', items, limit: items.length, hasMore: next !== null, next };
}

describe('ListingStore', () => {
    let api: ApiMock;
    let store: ListingStore;
    let sidebar: SidebarService;

    const folder = item('Folder', { kind: 'directory' });
    const a = item('a.jpg');
    const b = item('b.mp4');
    const c = item('c.txt');
    const d = item('d.jpg');

    beforeEach(async () => {
        localStorage.clear();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig({ pageSize: 3, searchPageSize: 2 }));
        store = TestBed.inject(ListingStore);
        sidebar = TestBed.inject(SidebarService);
        vi.spyOn(sidebar, 'refresh').mockImplementation(() => { });
        api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
    });

    afterEach(() => vi.restoreAllMocks());

    describe('folder rules', () => {
        it('narrows the rights of the space to those the folder rules give here and on each entry', async () => {
            api.browse.mockResolvedValue(listing([folder, a], {
                access: { rights: ['library.browse', 'tags.view', 'tags.assign'], pathOnly: false, children: { folder: { rights: ['library.browse'], pathOnly: true } } },
            }));
            await store.load();

            expect(store.can('tags.assign')).toBe(true);
            expect(store.can('files.edit')).toBe(false);
            expect(store.can('tags.assign', a)).toBe(true);
            expect(store.can('tags.assign', folder)).toBe(false);
        });

        it('leaves the rights of the space as they are without rules, and in searches', async () => {
            api.browse.mockResolvedValue(listing([a]));
            await store.load();
            expect(store.can('files.edit', a)).toBe(true);

            api.search.mockResolvedValue(results([a]));
            store.route.set({ path: '', q: 'x', sort: '', desc: false });
            await store.load();
            expect(store.folderAccess()).toBeNull();
            expect(store.can('files.edit', a)).toBe(true);
        });
    });

    describe('loading', () => {
        it('browses a folder in its sort, a page at a time', async () => {
            api.browse.mockResolvedValue(listing([folder, a, b], { total: 5 }));
            store.route.set({ path: 'x', q: '', sort: 'size', desc: true });

            await store.load();

            expect(api.browse).toHaveBeenCalledWith({ path: 'x', offset: 0, limit: 3, sort: 'size', desc: true }, expect.any(AbortSignal));
            expect(store.status()).toBe('ready');
            expect(store.items()).toEqual([folder, a, b]);
            expect(store.folders()).toEqual([folder]);
            expect(store.files()).toEqual([a, b]);
            expect(store.media()).toEqual([a, b]);
            expect(store.hasMore()).toBe(true);
        });

        it('lists a folder by name when asked for the path order', async () => {
            api.browse.mockResolvedValue(listing([]));
            store.route.set({ path: '', q: '', sort: 'path', desc: false });

            await store.load();

            expect(api.browse.mock.lastCall![0].sort).toBe('name');
        });

        it('loads the next pages from where the last one ended', async () => {
            api.browse.mockResolvedValueOnce(listing([a, b, c], { total: 4 })).mockResolvedValueOnce(listing([d], { offset: 3, total: 4 }));
            await store.load();

            expect(await store.loadMore()).toBe(true);
            expect(api.browse.mock.lastCall![0].offset).toBe(3);
            expect(store.items()).toEqual([a, b, c, d]);
            expect(store.hasMore()).toBe(false);
            expect(await store.loadMore()).toBe(false);
            expect(api.browse).toHaveBeenCalledTimes(2);
        });

        it('loads a page once when asked again while it loads', async () => {
            const page = deferred<DirectoryListing>();
            api.browse.mockResolvedValueOnce(listing([a, b, c], { total: 6 })).mockReturnValueOnce(page.promise);
            await store.load();

            const first = store.loadMore();
            const second = store.loadMore();
            page.resolve(listing([d], { offset: 3, total: 6 }));

            expect(second).toBe(first);
            expect(await first).toBe(true);
            expect(api.browse).toHaveBeenCalledTimes(2);
        });

        it('searches with the search page size, following the cursor', async () => {
            api.search.mockResolvedValueOnce(results([a, b], 'b.mp4')).mockResolvedValueOnce(results([c]));
            store.route.set({ path: 'x', q: 'cat', sort: '', desc: false });

            await store.load();
            await store.loadMore();

            expect(api.search.mock.calls.map(([query]) => [query.after, query.limit, query.sort])).toEqual([[null, 2, 'path'], ['b.mp4', 2, 'path']]);
            expect(store.items()).toEqual([a, b, c]);
            expect(store.hasMore()).toBe(false);
            expect(store.searching()).toBe(true);
            expect(api.browse).not.toHaveBeenCalled();
        });

        it('loads at least as many items as before when coming back', async () => {
            api.browse.mockResolvedValue(listing([a]));

            await store.load({ loaded: 250, scrollY: 900 });

            expect(api.browse.mock.lastCall![0].limit).toBe(250);
        });

        it('takes more pages when coming back to more items than the server gives at once', async () => {
            api.browse
                .mockResolvedValueOnce(listing([a, b], { total: 5 }))
                .mockResolvedValueOnce(listing([c], { offset: 2, total: 5 }))
                .mockResolvedValueOnce(listing([d], { offset: 3, total: 5 }));

            await store.load({ loaded: 4, scrollY: 900 });

            expect(api.browse.mock.calls.map(([query]) => query.offset)).toEqual([0, 2, 3]);
            expect(store.items()).toEqual([a, b, c, d]);
            expect(store.status()).toBe('ready');
        });

        it('shows what came back when a page of the way back fails', async () => {
            vi.spyOn(TestBed.inject(ToastService), 'error').mockImplementation(() => { });
            api.browse.mockResolvedValueOnce(listing([a, b], { total: 5 })).mockRejectedValueOnce(new ApiError(500, 'Boom'));

            await store.load({ loaded: 4 });

            expect(store.items()).toEqual([a, b]);
            expect(store.status()).toBe('ready');
        });

        it('knows the total of a folder, and of a search from its first page', async () => {
            api.browse.mockResolvedValue(listing([a, b, c], { total: 40 }));
            await store.load();
            expect(store.total()).toEqual({ count: 40, capped: false });

            api.search
                .mockResolvedValueOnce({ ...results([a, b], 'b.mp4'), total: 10000, totalCapped: true })
                .mockResolvedValueOnce({ ...results([c]), total: null });
            store.route.set({ path: '', q: 'cat', sort: '', desc: false });
            await store.load();
            await store.loadMore();

            expect(store.total()).toEqual({ count: 10000, capped: true });
        });

        it('drops the answer of a listing left meanwhile', async () => {
            const slow = deferred<DirectoryListing>();
            api.browse.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(listing([b]));
            const first = store.load();
            store.route.set({ path: 'other', q: '', sort: '', desc: false });
            await store.load();

            slow.resolve(listing([a]));
            await first;

            expect(store.items()).toEqual([b]);
        });

        it('cancels the requests of the listing left', async () => {
            api.browse.mockReturnValue(new Promise(() => { }));
            void store.load();
            const signal = api.browse.mock.lastCall![1]!;

            void store.load();

            expect(signal.aborted).toBe(true);
            expect(store.signal.aborted).toBe(false);
        });

        it('reports a failed listing', async () => {
            api.browse.mockRejectedValue(new ApiError(404, 'Not Found'));

            await store.load();

            expect(store.status()).toBe('error');
            expect(store.error()!.message).toBe('Not Found');
            expect(store.items()).toEqual([]);
        });

        it('wraps a failure that is not an API error', async () => {
            api.browse.mockRejectedValue('weird');

            await store.load();

            expect(store.error()).toBeInstanceOf(ApiError);
            expect(store.error()!.message).toBe('weird');
        });

        it('stops paging after a failed page, saying so', async () => {
            const error = vi.spyOn(TestBed.inject(ToastService), 'error');
            api.browse.mockResolvedValueOnce(listing([a, b, c], { total: 9 })).mockRejectedValueOnce(new ApiError(500, 'Boom'));
            await store.load();

            expect(await store.loadMore()).toBe(false);

            expect(error).toHaveBeenCalledWith('Could not load more items: Boom');
            expect(store.hasMore()).toBe(false);
            expect(store.items()).toEqual([a, b, c]);
        });

        it('asks for the sibling folders after a folder, never for a search or the root', async () => {
            api.browse.mockResolvedValue(listing([]));
            api.search.mockResolvedValue(results([]));
            const siblings = { path: 'x/y', previous: folder, next: null, position: 1, count: 2 };
            api.siblings.mockResolvedValue(siblings);

            store.route.set({ path: 'x/y', q: '', sort: 'modified', desc: true });
            await store.load();
            await settle();
            expect(api.siblings).toHaveBeenCalledWith({ path: 'x/y', sort: 'modified', desc: true }, expect.any(AbortSignal));
            expect(store.siblings()).toEqual(siblings);

            store.route.set({ path: '', q: '', sort: '', desc: false });
            await store.load();
            store.route.set({ path: 'x/y', q: 'cat', sort: '', desc: false });
            await store.load();
            expect(api.siblings).toHaveBeenCalledTimes(1);
            expect(store.siblings()).toBeNull();
        });

        it('refreshes the sidebar after a search naming tags', async () => {
            api.search.mockResolvedValue(results([]));
            for (const q of ['t:cat', 'dog -tag:x', 'a,T:b', 'cat', 'st:x', 'e:mp4']) {
                store.route.set({ path: '', q, sort: '', desc: false });
                await store.load();
            }

            expect(sidebar.refresh).toHaveBeenCalledTimes(3);
        });
    });

    describe('selection', () => {
        beforeEach(async () => {
            api.browse.mockResolvedValue(listing([folder, a, b, c, d]));
            await store.load();
        });

        it('selects and deselects files', () => {
            store.select(a, true);
            store.select(c, true);
            store.select(a, false);

            expect([...store.selection()]).toEqual([c.path]);
            expect(store.isSelected(c.path)).toBe(true);
            expect(store.selectedFiles()).toEqual([c]);
        });

        it('extends from the file clicked last with a range, in both directions', () => {
            store.select(b, true);
            store.select(d, true, true);
            expect(store.selectedFiles()).toEqual([b, c, d]);

            store.select(a, false, true);
            expect(store.selectedFiles()).toEqual([]);
        });

        it('selects only the file when there is no anchor for a range', () => {
            store.select(c, true, true);

            expect(store.selectedFiles()).toEqual([c]);
        });

        it('selects all the files, never the folders', () => {
            store.selectAll();

            expect(store.selectedFiles()).toEqual([a, b, c, d]);
            expect(store.isSelected(folder.path)).toBe(false);
        });

        it('is cleared by a new listing, the anchor too', async () => {
            store.select(a, true);
            await store.load();
            expect(store.selection().size).toBe(0);

            store.select(c, true, true);
            expect(store.selectedFiles()).toEqual([c]);
        });

        it('keeps the same set when cleared while empty', () => {
            const empty = store.selection();
            store.clearSelection();

            expect(store.selection()).toBe(empty);
        });
    });

    describe('tags of the listed files', () => {
        const sun = summary(tag('Sun'));
        const sea = summary(tag('Sea'));

        beforeEach(async () => {
            api.browse.mockResolvedValue(listing([folder, { ...a, tags: [sun] }, { ...b, tags: [sun, sea] }, c]));
            await store.load();
        });

        it('replaces the tags of the changed files, by new objects', () => {
            const before = store.items();

            store.applyFileTags([fileTags(c.path, [sea]), fileTags('not/listed.jpg', [sea])]);

            expect(store.items()[3].tags).toEqual([sea]);
            expect(store.items()[3]).not.toBe(before[3]);
            expect(store.items()[1]).toBe(before[1]);
            expect(sidebar.refresh).toHaveBeenCalled();
        });

        it('renames an edited tag wherever it is shown', () => {
            const renamed = tag('Sunny', { id: sun.id, color: 'yellow' });

            store.applyTagChanged(renamed);

            expect(store.items()[1].tags).toEqual([summary(renamed)]);
            expect(store.items()[2].tags!.map(t => t.name)).toEqual(['Sunny', 'Sea']);
        });

        it('takes a deleted tag off the files', () => {
            store.applyTagDeleted(sun.id);

            expect(store.items()[1].tags).toEqual([]);
            expect(store.items()[2].tags).toEqual([sea]);
        });

        it('gives a tag added to a folder to its listed files, and to those of sub-folders when recursive', () => {
            const nested = item('Sub/n.jpg');
            store.items.update(items => [...items, nested]);

            store.applyScopeTags({ path: '', recursive: false, query: null }, { add: sea });
            expect(store.items().map(i => i.tags?.map(t => t.name))).toEqual([undefined, ['Sun', 'Sea'], ['Sun', 'Sea'], ['Sea'], []]);

            store.applyScopeTags({ path: '', recursive: true, query: null }, { remove: sun.id });
            expect(store.items().map(i => i.tags?.map(t => t.name))).toEqual([undefined, ['Sea'], ['Sea'], ['Sea'], []]);

            store.applyScopeTags({ path: 'Sub', recursive: false, query: null }, { add: sun });
            expect(store.items()[4].tags).toEqual([sun]);
            expect(sidebar.refresh).toHaveBeenCalled();
        });

        it('drops the not-indexed mark of the files tagging indexed, and only theirs', () => {
            store.items.update(items => items.map(i => ({ ...i, indexed: false })));
            const nested = { ...item('Sub/n.jpg'), indexed: false };
            store.items.update(items => [...items, nested]);

            store.applyFileTags([fileTags(c.path, [sea])]);
            expect(store.items().map(i => i.indexed)).toEqual([false, false, false, true, false]);

            store.applyScopeTags({ path: '', recursive: false, query: null }, { add: sea });
            expect(store.items().map(i => i.indexed)).toEqual([false, true, true, true, false]);
        });

        it('gives a tag added to the results of a search to the files listed for that search only', () => {
            const before = store.items();

            store.applyScopeTags({ path: '', recursive: false, query: 'beach' }, { add: sea });
            expect(store.items()).toBe(before);

            store.route.set({ path: '', q: 'beach ', sort: '', desc: false });
            store.applyScopeTags({ path: '', recursive: false, query: 'beach' }, { add: sea });
            expect(store.items()[3].tags).toEqual([sea]);
        });

        it('keeps the same item list when no listed file has the tag', () => {
            const before = store.items();

            store.applyTagDeleted(99999);
            store.applyTagChanged(tag('Other'));

            expect(store.items()).toBe(before);
        });

        it('records a viewed file only when it has tags, and refreshes the sidebar then', async () => {
            api.fileViewed.mockResolvedValue(undefined);

            store.reportView(c);
            store.reportView({ ...a, tags: null });
            store.reportView(store.items()[1]);
            await settle();

            expect(api.fileViewed).toHaveBeenCalledTimes(1);
            expect(api.fileViewed).toHaveBeenCalledWith(a.path);
            expect(sidebar.refresh).toHaveBeenCalled();
        });

        it('does not bother the user when recording a view fails', async () => {
            const error = vi.spyOn(TestBed.inject(ToastService), 'error');
            api.fileViewed.mockRejectedValue(new ApiError(500, 'Boom'));

            store.reportView(store.items()[1]);
            await settle();

            expect(error).not.toHaveBeenCalled();
        });
    });

    describe('revealing a file (quick links)', () => {
        let viewer: { open: ReturnType<typeof vi.fn> };
        let navigate: ReturnType<typeof vi.spyOn>;

        beforeEach(() => {
            viewer = { open: vi.fn() };
            TestBed.inject(Dialogs).viewer.set(viewer as unknown as ViewerComponent);
            navigate = vi.spyOn(store, 'navigate').mockImplementation(() => { });
        });

        /** What the listing page does on the navigation `reveal` starts. */
        async function arrive(path: string): Promise<void> {
            store.route.set({ path, q: '', sort: '', desc: false });
            await store.load();
            await settle();
            TestBed.inject(ApplicationRef).tick();
        }

        it('lists the folder of the file, takes pages until it is there, and opens media in the viewer', async () => {
            const target = item('Media/d.jpg');
            api.browse
                .mockResolvedValueOnce(listing([item('Media/a.jpg'), item('Media/b.mp4'), item('Media/c.txt')], { path: 'Media', total: 4 }))
                .mockResolvedValueOnce(listing([target], { path: 'Media', offset: 3, total: 4 }));
            store.route.set({ path: 'Other', q: 'cats', sort: 'size', desc: false });

            store.reveal('Media/d.jpg');
            expect(navigate).toHaveBeenCalledWith({ path: 'Media', q: '' });
            await arrive('Media');

            expect(api.browse).toHaveBeenCalledTimes(2);
            expect(viewer.open).toHaveBeenCalledWith(target);
            expect(store.revealed()).toBe('Media/d.jpg');
        });

        it('only highlights other files', async () => {
            api.browse.mockResolvedValue(listing([item('Docs/notes.txt')], { path: 'Docs' }));
            store.reveal('Docs/notes.txt');
            await arrive('Docs');

            expect(viewer.open).not.toHaveBeenCalled();
            expect(store.revealed()).toBe('Docs/notes.txt');
        });

        it('shows a file of the folder already listed without listing it again', async () => {
            api.browse.mockResolvedValue(listing([a, b], { total: 2 }));
            await store.load();

            store.reveal('b.mp4');
            await settle();
            TestBed.inject(ApplicationRef).tick();

            expect(navigate).not.toHaveBeenCalled();
            expect(api.browse).toHaveBeenCalledTimes(1);
            expect(viewer.open).toHaveBeenCalledWith(b);
        });

        it('says so when the file is not in its folder any more', async () => {
            const show = vi.spyOn(TestBed.inject(ToastService), 'show');
            api.browse.mockResolvedValue(listing([a], { total: 1 }));
            store.reveal('gone.jpg');
            await arrive('');

            expect(viewer.open).not.toHaveBeenCalled();
            expect(show).toHaveBeenCalledWith(expect.stringContaining('“gone.jpg” is not in this folder any more'));
        });

        it('forgets the file when another folder is listed meanwhile', async () => {
            api.browse.mockResolvedValue(listing([a], { path: 'Elsewhere', total: 1 }));
            store.reveal('Media/a.jpg');
            await arrive('Elsewhere');
            await arrive('Media');

            expect(viewer.open).not.toHaveBeenCalled();
        });
    });

    describe('navigation and view', () => {
        it('navigates by changing the query string, leaving the defaults out', () => {
            const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
            store.route.set({ path: 'x', q: '', sort: 'size', desc: true });

            store.navigate({ q: 't:cat' }, { replace: true });

            expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { path: 'x', q: 't:cat', sort: 'size', desc: '1' }, replaceUrl: true });
        });

        it('links to a folder keeping the sort but not the search', () => {
            store.route.set({ path: 'x', q: 'cat', sort: 'size', desc: false });

            expect(store.browseParams('y/z')).toEqual({ path: 'y/z', q: null, sort: 'size', desc: null });
        });

        it('remembers the view per browser, ignoring unknown ones', () => {
            store.setView('list');
            expect(localStorage.getItem('sfe.view')).toBe('list');

            TestBed.resetTestingModule();
            localStorage.setItem('sfe.view', 'mosaic');
            TestBed.configureTestingModule({ providers: testProviders(mockApi()) });
            expect(TestBed.inject(ListingStore).view()).toBe('grid');
        });
    });

    describe('file management', () => {
        const zoo = item('Zoo', { kind: 'directory' });

        it('puts a new folder where the name sort puts it among the folders loaded, and counts it', async () => {
            api.browse.mockResolvedValue(listing([folder, zoo, a, b], { directoryCount: 2, fileCount: 2 }));
            await store.load();

            store.applyAdded(item('Photos', { kind: 'directory' }));
            store.applyAdded(item('Aardvark', { kind: 'directory' }));
            store.applyAdded(item('Zzz', { kind: 'directory' }));

            expect(store.items().map(i => i.name)).toEqual(['Aardvark', 'Folder', 'Photos', 'Zoo', 'Zzz', 'a.jpg', 'b.mp4']);
            expect(store.listing()).toMatchObject({ total: 7, directoryCount: 5, fileCount: 2 });
            expect(store.total()).toEqual({ count: 7, capped: false });
        });

        it('puts it first among its kind in other sorts, and ignores one made elsewhere', async () => {
            store.route.set({ path: '', q: '', sort: 'modified', desc: false });
            api.browse.mockResolvedValue(listing([a, b]));
            await store.load();

            store.applyAdded(item('New', { kind: 'directory' }));
            store.applyAdded(item('Other/New', { kind: 'directory' }));

            expect(store.items().map(i => i.path)).toEqual(['New', 'a.jpg', 'b.mp4']);
        });

        it('leaves out of the next page what was added meanwhile', async () => {
            api.browse.mockResolvedValueOnce(listing([folder, a, b], { total: 5, limit: 3 }));
            await store.load();
            store.applyAdded(item('New', { kind: 'directory' }));
            // The folder listed again from disk: everything after the new folder moved one place.
            api.browse.mockResolvedValueOnce(listing([b, c, d], { offset: 3, total: 6, limit: 3 }));

            await store.loadMore();

            expect(store.items().map(i => i.path)).toEqual(['Folder', 'New', 'a.jpg', 'b.mp4', 'c.txt', 'd.jpg']);
        });
    });
});
