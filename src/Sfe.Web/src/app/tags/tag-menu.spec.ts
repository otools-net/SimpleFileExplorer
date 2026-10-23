import { TestBed } from '@angular/core/testing';
import { Rights } from '../core/rights';
import { ApiMock, fakeConfig, item, limitedConfig, loadConfig, mockApi, tag, testProviders } from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { TagMenu } from './tag-menu';

describe('TagMenu', () => {
    let api: ApiMock;
    let store: ListingStore;
    let menu: TagMenu;
    const beach = tag('Beach', { id: 3 });
    const photo = item('Media/beach.jpg');
    const ids = (file?: typeof photo) => menu.target(beach, file).entries().map(entry => entry.id);
    const entry = (id: string, file?: typeof photo) => menu.target(beach, file).entries().find(e => e.id === id)!;

    async function setUp(rights?: string[]): Promise<void> {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, rights ? limitedConfig(rights) : fakeConfig());
        store = TestBed.inject(ListingStore);
        store.route.set({ path: 'Media', q: '', sort: '', desc: false });
        menu = TestBed.inject(TagMenu);
    }

    afterEach(() => vi.restoreAllMocks());

    it('searches the tag, here or in a new tab, or the files without it', async () => {
        await setUp([Rights.browse, Rights.viewTags]);
        const navigate = vi.spyOn(store, 'navigate').mockImplementation(() => { });

        expect(menu.target(beach)).toMatchObject({ kind: 'tag', name: 'Beach' });
        expect(ids()).toEqual(['search', 'newTab', 'exclude']);
        entry('search').run!();
        expect(navigate).toHaveBeenCalledWith({ q: 't:"Beach"' });
        expect(entry('newTab').href).toMatch(/\?path=Media&q=t:%22Beach%22$/);
        expect(entry('exclude').label).toBe('Search without this tag');
        entry('exclude').run!();
        expect(navigate).toHaveBeenLastCalledWith({ q: '-t:"Beach"' });
    });

    it('adds the tag to the search, or leaves its files out of it', async () => {
        await setUp([Rights.browse, Rights.viewTags]);
        const navigate = vi.spyOn(store, 'navigate').mockImplementation(() => { });
        store.route.set({ path: 'Media', q: 'sunset', sort: '', desc: false });

        expect(ids()).toEqual(['search', 'newTab', 'addToSearch', 'exclude']);
        entry('addToSearch').run!();
        expect(navigate).toHaveBeenLastCalledWith({ q: 'sunset t:"Beach"' });
        entry('exclude').run!();
        expect(navigate).toHaveBeenLastCalledWith({ q: 'sunset -t:"Beach"' });

        // Already in the search: not added again.
        store.route.set({ path: 'Media', q: 'sunset t:"Beach"', sort: '', desc: false });
        expect(ids()).not.toContain('addToSearch');
    });

    it('takes the tag off the file, and edits it, for those who may', async () => {
        await setUp();
        const open = vi.fn().mockResolvedValue({});
        TestBed.inject(Dialogs).tagEditor.set({ open } as never);
        api.changeFileTags.mockResolvedValue({ files: [{ path: photo.path, tags: [] }] } as never);
        const apply = vi.spyOn(store, 'applyFileTags');

        expect(ids(photo)).toEqual(['search', 'newTab', 'exclude', 'remove', 'edit']);
        entry('remove', photo).run!();
        await vi.waitFor(() => expect(apply).toHaveBeenCalled());
        expect(api.changeFileTags).toHaveBeenCalledWith({ paths: [photo.path], remove: [3] });
        entry('edit').run!();
        expect(open).toHaveBeenCalledWith(3);

        TestBed.resetTestingModule();
        await setUp([Rights.browse, Rights.viewTags]);
        TestBed.inject(Dialogs).tagEditor.set({ open } as never);
        expect(ids(photo)).toEqual(['search', 'newTab', 'exclude']);
    });
});
