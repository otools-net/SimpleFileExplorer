import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { ConnectionsConfig } from '../core/config.models';
import { ConnectionListing } from '../core/connection.models';
import { DirectoryListing, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { isViewable } from '../formats/formats';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { enterPlace } from './connection-pages';
import { FileDownloads } from '../files/downloads';
import { ListingStore } from './listing.store';
import { ConnectionLinksComponent } from './sidebar/connection-links.component';

const CONNECTIONS: ConnectionsConfig = {
    levels: ['account'],
    kinds: [{ kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} }],
};

function page(items: LibraryItem[], capabilities: ConnectionListing['capabilities'] = {}): ConnectionListing {
    const listing: DirectoryListing = {
        path: '',
        name: 'My Dropbox',
        parentPath: null,
        items,
        offset: 0,
        limit: 100,
        total: items.length,
        directoryCount: 0,
        fileCount: items.length,
    };
    return {
        connectionId: 7,
        name: 'My Dropbox',
        kind: 'dropbox',
        listing,
        access: 'read',
        sorted: true,
        hasMore: false,
        capabilities,
    };
}

describe('a connection in the listing', () => {
    let api: ApiMock;
    let store: ListingStore;
    let config: ConfigService;

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        config = await loadConfig(api, fakeConfig({ connections: CONNECTIONS }));
        store = TestBed.inject(ListingStore);
        api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
    });

    afterEach(() => {
        // The formats of the library again for the other suites.
        enterPlace(config, null);
        vi.restoreAllMocks();
    });

    async function list(capabilities: ConnectionListing['capabilities'] = {}): Promise<void> {
        api.connectionBrowse.mockResolvedValue(page([item('Sunrise.jpg'), item('notes.txt')], capabilities));
        store.route.set({ c: 7, path: 'Trips', q: '', sort: '', desc: false });
        await store.load();
    }

    it('lists its folder through its own route, never the library and its folders around', async () => {
        await list();

        expect(api.connectionBrowse).toHaveBeenCalledWith(
            7,
            { path: 'Trips', offset: 0, limit: 100, sort: 'name', desc: false },
            expect.any(AbortSignal),
        );
        expect(api.browse).not.toHaveBeenCalled();
        expect(api.siblings).not.toHaveBeenCalled();
        expect(store.items().map((entry) => entry.name)).toEqual(['Sunrise.jpg', 'notes.txt']);
    });

    it('gives its files their addresses, and hides what its files cannot do', async () => {
        await list({ thumbnails: true });

        const urls = config.urls()!;
        expect(urls.file('Trips/Sunrise.jpg')).toBe('api/v1/connections/7/files/raw/Trips/Sunrise.jpg');
        expect(urls.download('a b.jpg')).toBe('api/v1/connections/7/files/raw/a%20b.jpg?download=true');
        expect(urls.thumbnail('Sunrise.jpg')).toBe('api/v1/connections/7/thumbnails/Sunrise.jpg');
        expect(urls.player('clip.mp4')).toBe('player?path=clip.mp4&c=7');
        expect(config.config()!.features).toEqual({ thumbnails: true, trickplay: false });
        expect(config.config()!.rootName).toBe('My Dropbox');
        expect(config.can(Rights.viewFiles) && config.can(Rights.download)).toBe(true);
        expect(config.can(Rights.folderDefaultTags) || config.can(Rights.renameFiles) || config.can(Rights.scan)).toBe(
            false,
        );
        // The library's tags on its files: kept by the library's index of it, whatever the access.
        expect(config.can(Rights.viewTags) && config.can(Rights.assignTags)).toBe(true);
        // Rights of the account, not of the files, stay.
        expect(config.can(Rights.useConnections)).toBe(true);
        // The library converts no video here (streaming off in it): nor in the connection.
        expect(config.videoStream('clip.mp4')).toBeNull();
        // Media and the text formats open (read only: connection-previews.spec.ts); no EPUB nor archive reader.
        expect(isViewable(item('Sunrise.jpg')) && isViewable(item('notes.txt'))).toBe(true);
        expect(isViewable(item('book.epub'))).toBe(false);
        // The library's quick links keep their addresses.
        expect(config.libraryUrls()!.file('a.jpg')).toBe('api/v1/files/raw/a.jpg');
    });

    it('downloads several of its files as a zip of its own', async () => {
        await list();
        api.createArchive.mockResolvedValue({
            url: `api/v1/connections/7/archive/${'a'.repeat(48)}`,
            fileName: 'x.zip',
        });

        const saved: string[] = [];
        vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
            saved.push(this.getAttribute('href')!);
        });

        await TestBed.inject(FileDownloads).download([item('a.jpg'), item('b.jpg')]);

        expect(api.createArchive).toHaveBeenCalledWith(['a.jpg', 'b.jpg'], 7);
        expect(saved).toEqual([`api/v1/connections/7/archive/${'a'.repeat(48)}`]);
    });

    it('searches the connection from its index, keeping it the place, with how much the index holds', async () => {
        api.connectionSearch.mockResolvedValue({
            connectionId: 7,
            name: 'My Dropbox',
            kind: 'dropbox',
            results: {
                query: 'beach',
                path: '',
                items: [item('Trips/beach.jpg')],
                limit: 100,
                hasMore: false,
                next: null,
                total: 1,
            },
            access: 'write',
            capabilities: { thumbnails: true },
            coverage: 'partial',
        });
        store.route.set({ c: 7, path: 'Trips', q: 'beach', sort: '', desc: false });

        await store.load();

        expect(api.connectionSearch).toHaveBeenCalledWith(
            7,
            expect.objectContaining({ q: 'beach', path: 'Trips', sort: 'path' }),
            expect.anything(),
        );
        expect(api.search).not.toHaveBeenCalled();
        expect(store.items().map((i) => i.path)).toEqual(['Trips/beach.jpg']);
        expect(config.place()).toEqual(expect.objectContaining({ id: 7, name: 'My Dropbox', access: 'write' }));
        expect(store.listing()).toEqual(expect.objectContaining({ coverage: 'partial' }));
    });

    it('searches nothing itself everywhere: the locations are searched apart', async () => {
        store.route.set({ c: 7, path: 'Trips', q: 'beach', sort: '', desc: false, all: true });

        await store.load();

        expect(store.everywhere()).toBe(true);
        expect(api.connectionSearch).not.toHaveBeenCalled();
        expect(api.search).not.toHaveBeenCalled();
        expect(config.place()).toBeNull();
        expect(store.status()).toBe('ready');
    });

    it('goes back to the library with a folder of it; a search stays in the connection', async () => {
        await list();
        expect(store.browseParams('Sub')).toEqual(expect.objectContaining({ c: 7, path: 'Sub' }));
        expect(store.libraryParams('Sub')).toEqual(expect.objectContaining({ path: 'Sub' }));
        expect(store.libraryParams('Sub')['c']).toBeUndefined();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        store.navigate({ q: 'holiday' });

        const [, extras] = navigate.mock.calls[0];
        expect(extras!.queryParams).toEqual(expect.objectContaining({ q: 'holiday', c: 7 }));

        api.browse.mockResolvedValue({ ...page([]).listing, name: 'Root' });
        store.route.set({ c: null, path: '', q: '', sort: '', desc: false });
        await store.load();
        expect(config.place()).toBeNull();
        expect(config.urls()!.file('a.jpg')).toBe('api/v1/files/raw/a.jpg');
        expect(isViewable(item('notes.txt'))).toBe(true);
    });
});

describe('ConnectionLinksComponent', () => {
    @Component({
        selector: 'sfe-host',
        imports: [ConnectionLinksComponent],
        changeDetection: ChangeDetectionStrategy.OnPush,
        template: '<section sfeConnectionLinks></section>',
    })
    class Host {}

    let api: ApiMock;

    beforeEach(() => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
    });

    it('lists the connections the session uses, each opening its files', async () => {
        await loadConfig(api, fakeConfig({ connections: CONNECTIONS }));
        api.connections.mockResolvedValue({
            usable: [
                { id: 7, kind: 'dropbox', name: 'My Dropbox', level: 'account', state: 'ready', access: 'write' },
                { id: 8, kind: 'dropbox', name: 'Old', level: 'account', state: 'needsReconnecting', access: 'read' },
            ],
            managed: [],
            groups: [],
            libraries: [],
        });
        const fixture = TestBed.createComponent(Host);
        await settle();
        fixture.detectChanges();

        const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('a')];
        expect(links.map((link) => link.textContent!.trim())).toEqual(['My Dropbox', 'Old']);
        expect(links[0].getAttribute('href')).toBe('/?c=7');
        expect(links[1].title).toContain('signed in to again');
    });

    it('stays hidden and asks nothing without connections', async () => {
        await loadConfig(api, fakeConfig());
        const fixture = TestBed.createComponent(Host);
        await settle();

        expect((fixture.nativeElement as HTMLElement).querySelector('section')!.hidden).toBe(true);
        expect(api.connections).not.toHaveBeenCalled();
    });
});
