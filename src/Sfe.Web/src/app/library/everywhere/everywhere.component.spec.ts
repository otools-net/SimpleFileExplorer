import { TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api.service';
import { Rights } from '../../core/rights';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    item,
    loadConfig,
    mockApi,
    settle,
    testProviders,
} from '../../../testing/fakes';
import { SearchResults } from '../library.models';
import { ListingStore } from '../listing.store';
import { EverywhereComponent } from './everywhere.component';

function results(paths: string[], next: string | null = null, total = paths.length): SearchResults {
    return { query: 'beach', path: '', items: paths.map((p) => item(p)), limit: 20, hasMore: !!next, next, total };
}

describe('EverywhereComponent', () => {
    let api: ApiMock;
    let store: ListingStore;
    let element: HTMLElement;

    const groups = () => [...element.querySelectorAll<HTMLElement>('.everywhere-group')];
    const names = (group: HTMLElement) =>
        [...group.querySelectorAll('.everywhere-item-name')].map((name) => name.textContent);

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        const connections = { levels: ['account' as const], kinds: [] };
        await loadConfig(api, fakeConfig({ connections, rights: [Rights.browse, Rights.useConnections] }));
        api.connections.mockResolvedValue({
            usable: [
                { id: 3, kind: 'dropbox', name: 'Dropbox', level: 'account', state: 'ready', access: 'read' },
                { id: 4, kind: 'dropbox', name: 'Team', level: 'library', state: 'ready', access: 'read' },
            ],
            managed: [],
            groups: [],
            libraries: [],
        });
        store = TestBed.inject(ListingStore);
        store.route.set({ path: 'Photos', q: 'beach', sort: '', desc: false, all: true });
    });

    async function render(): Promise<void> {
        const fixture = TestBed.createComponent(EverywhereComponent);
        element = fixture.nativeElement;
        await settle();
    }

    it('heads a group per location with its count; a slow or failing one holds no other', async () => {
        api.search.mockResolvedValue(results(['Photos/beach.jpg', 'beach 2.jpg'], 'n1', 7));
        api.connectionSearch.mockImplementation((id: number) =>
            id === 3 ? Promise.reject(new ApiError(502, 'Dropbox could not be reached.')) : new Promise(() => {}),
        );

        await render();

        expect(groups().map((g) => g.querySelector('.everywhere-name')!.textContent)).toEqual([
            fakeConfig().rootName,
            'Dropbox',
            'Team',
        ]);
        const [library, dropbox, team] = groups();
        expect(library.querySelector('.everywhere-count')!.textContent).toBe('7 files');
        expect(names(library)).toEqual(['beach.jpg', 'beach 2.jpg']);
        expect(dropbox.textContent).toContain('Dropbox could not be reached.');
        expect(buttonNamed(dropbox, 'Retry')).toBeDefined();
        expect(team.getAttribute('aria-busy')).toBe('true');
        expect(api.search).toHaveBeenCalledWith(expect.objectContaining({ q: 'beach', path: '' }), expect.anything());
    });

    it('loads more of one group on demand, and tells a connection not indexed whole', async () => {
        api.search.mockResolvedValueOnce(results(['a beach.jpg'], 'n1', 2));
        api.connectionSearch.mockResolvedValue({
            connectionId: 3,
            name: 'Dropbox',
            kind: 'dropbox',
            results: results([]),
            access: 'read',
            capabilities: {},
            coverage: 'none',
        });
        await render();
        api.search.mockResolvedValueOnce(results(['b beach.jpg']));

        buttonNamed(groups()[0], 'Show more')!.click();
        await settle();

        expect(api.search).toHaveBeenLastCalledWith(expect.objectContaining({ after: 'n1' }), expect.anything());
        expect(names(groups()[0])).toEqual(['a beach.jpg', 'b beach.jpg']);
        expect(buttonNamed(groups()[0], 'Show more')).toBeUndefined();
        expect(groups()[1].querySelector('.everywhere-note')!.textContent).toContain('Not indexed here yet');
        expect(groups()[1].textContent).toContain('No results here.');
    });

    it('opens a file where it is: its folder in its location', async () => {
        api.search.mockResolvedValue(results([]));
        api.connectionSearch.mockImplementation((id: number) =>
            Promise.resolve({
                connectionId: id,
                name: 'x',
                kind: 'dropbox',
                results: results(id === 3 ? ['Trips/beach.jpg'] : []),
                access: 'read',
                capabilities: {},
                coverage: 'complete',
            }),
        );
        const reveal = vi.spyOn(store, 'reveal').mockImplementation(() => {});
        await render();

        groups()[1].querySelector<HTMLButtonElement>('.everywhere-item')!.click();

        expect(reveal).toHaveBeenCalledWith('Trips/beach.jpg', 3);
    });
});
