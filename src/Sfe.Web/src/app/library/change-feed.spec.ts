import { TestBed } from '@angular/core/testing';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { TAB_ID } from '../core/api.service';
import { ClientConfig } from '../core/config.models';
import { ChangeFeed } from './change-feed.service';
import { ListingStore } from './listing.store';
import { SidebarService } from './sidebar/sidebar.service';

/** A stand-in for the browser's EventSource that the test opens. */
class FakeSource extends EventTarget {
    static readonly CONNECTING = 0;
    static readonly OPEN = 1;
    static readonly CLOSED = 2;
    static all: FakeSource[] = [];
    readyState = FakeSource.CONNECTING;
    constructor(readonly url: string) {
        super();
        FakeSource.all.push(this);
    }
    close(): void {
        this.readyState = FakeSource.CLOSED;
    }
    emit(type: 'open' | 'error', state: number): void {
        this.readyState = state;
        this.dispatchEvent(new Event(type));
    }
}

let api: ApiMock;
let store: ListingStore;
let feed: ChangeFeed;
const last = () => FakeSource.all[FakeSource.all.length - 1];

async function setUp(config: ClientConfig = fakeConfig()): Promise<void> {
    FakeSource.all = [];
    vi.stubGlobal('EventSource', FakeSource);
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue({
        path: 'Media',
        name: 'Media',
        parentPath: '',
        items: [item('Media/a.jpg')],
        offset: 0,
        limit: 100,
        total: 1,
        directoryCount: 0,
        fileCount: 1,
    });
    api.watchFolder.mockResolvedValue({ watching: true });
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
    feed = TestBed.inject(ChangeFeed);
    feed.start();
}

/** The folders named to the server so far. */
const named = () =>
    api.watchFolder.mock.calls.map(([path, connection]) => (connection == null ? path : `${connection}:${path}`));

afterEach(() => {
    feed.stop();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
});

describe('ChangeFeed and changes made outside SFE', () => {
    it('opens the feed for this tab and names the folder shown once it is open, then on each move', async () => {
        await setUp();
        expect(last().url).toBe(`api/v1/files/changes?tab=${TAB_ID}`);
        TestBed.tick();
        expect(named()).toEqual([]);

        last().emit('open', FakeSource.OPEN);
        store.route.set({ path: 'Media/Holidays', q: '', sort: '', desc: false });
        TestBed.tick();
        // Sorted otherwise: the same folder, not named again.
        store.route.set({ path: 'Media/Holidays', q: '', sort: 'size', desc: true });
        TestBed.tick();
        // A search: nothing followed. A connection's folder: with its connection, even when its path is the same.
        store.route.set({ path: 'Media', q: 'beach', sort: '', desc: false });
        TestBed.tick();
        store.route.set({ path: 'Docs', q: '', sort: '', desc: false, c: 7 });
        TestBed.tick();
        store.route.set({ path: 'Docs', q: '', sort: '', desc: false });
        TestBed.tick();

        expect(named()).toEqual(['Media', 'Media/Holidays', null, '7:Docs', 'Docs']);
    });

    it('names the folder again when the feed opens again (the server forgot it)', async () => {
        await setUp();
        last().emit('open', FakeSource.OPEN);
        last().emit('error', FakeSource.CONNECTING);
        last().emit('open', FakeSource.OPEN);

        expect(named()).toEqual(['Media', 'Media']);
    });

    it('refreshes the folder listed when it changed outside SFE, unless the user switched that off', async () => {
        await setUp();
        feed.changed({ path: 'Media', origin: null, outside: true });
        await settle(450);
        expect(api.browse).toHaveBeenCalledTimes(2);
        feed.stop();

        TestBed.resetTestingModule();
        await setUp({ ...fakeConfig(), liveChanges: false });
        last().emit('open', FakeSource.OPEN);
        TestBed.tick();
        feed.changed({ path: 'Media', origin: null, outside: true });
        await settle(450);

        expect(api.browse).toHaveBeenCalledTimes(1);
        expect(named()).toEqual([]);
    });

    it("refreshes a connection's folder listed when it changed at the storage service, not the library's", async () => {
        await setUp();
        store.route.set({ path: 'Media', q: '', sort: '', desc: false, c: 7 });
        TestBed.tick();
        const browse = vi.spyOn(store, 'refresh').mockResolvedValue();

        feed.changed({ path: 'Media', origin: null, outside: true });
        await settle(450);
        expect(browse).not.toHaveBeenCalled();
        feed.changed({ path: 'Media', origin: null, connection: 7, outside: true });
        await settle(450);

        expect(browse).toHaveBeenCalledTimes(1);
    });
});
