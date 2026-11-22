import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionsConfig } from '../core/config.models';
import { ConnectionListing, ConnectionPlace, ConnectionsView } from '../core/connection.models';
import { UsableConnections } from '../core/usable-connections';
import { DirectoryListing, LibraryItem } from '../core/models';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    item,
    loadConfig,
    mockApi,
    settle,
    testProviders,
} from '../../testing/fakes';
import { enterPlace } from '../library/connection-pages';
import { ListingStore } from '../library/listing.store';
import { DRAG_ITEMS } from '../library/quick-links/quick-links.service';
import { ConnectionLinksComponent } from '../library/sidebar/connection-links.component';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { dragItemsData, parseDragItems } from './drag-items';
import { FileJobView } from './file.models';
import { FileTransfers } from './file-transfers';
import { OperationsPanelComponent } from './operations-panel.component';
import { TransferDialog } from './transfer.dialog';

const CONNECTIONS: ConnectionsConfig = {
    levels: ['account'],
    kinds: [{ kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} }],
};

const USABLE: ConnectionsView = {
    usable: [
        { id: 7, kind: 'dropbox', name: 'Dropbox', level: 'account', state: 'ready', access: 'write' },
        { id: 8, kind: 'dropbox', name: 'Shared', level: 'library', state: 'ready', access: 'read' },
    ],
    managed: [],
    groups: [],
    libraries: [],
};

const photo = item('Media/beach.jpg', { size: 2048 });
const trip = item('Trips/sunrise.jpg', { size: 4096 });

let api: ApiMock;
let config: ConfigService;
let transfers: FileTransfers;

function listing(path: string, items: LibraryItem[]): DirectoryListing {
    const folders = items.filter((i) => i.kind === 'directory').length;
    return {
        path,
        name: path.split('/').pop()!,
        parentPath: '',
        items,
        offset: 0,
        limit: 200,
        total: items.length,
        directoryCount: folders,
        fileCount: items.length - folders,
    };
}

function connectionPage(path: string, items: LibraryItem[]): ConnectionListing {
    return {
        connectionId: 7,
        name: 'Dropbox',
        kind: 'dropbox',
        listing: listing(path, items),
        access: 'write',
        sorted: true,
        hasMore: false,
        capabilities: {},
    };
}

function dropbox(): ConnectionPlace {
    return {
        id: 7,
        name: 'Dropbox',
        thumbnails: false,
        access: 'write',
        serverSideMove: true,
        serverSideCopy: true,
        ownTrash: true,
        uploadsInParts: false,
    };
}

function job(overrides: Partial<FileJobView> = {}): FileJobView {
    return {
        id: 5,
        kind: 'copy',
        state: 'running',
        target: 'Trips',
        total: 2,
        done: 0,
        bytesTotal: 0,
        bytesDone: 0,
        current: null,
        conflict: null,
        results: [],
        started: '2026-11-14T10:00:00Z',
        finished: null,
        fromConnection: null,
        toConnection: 7,
        ...overrides,
    };
}

beforeEach(async () => {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    config = await loadConfig(api, fakeConfig({ connections: CONNECTIONS }));
    transfers = TestBed.inject(FileTransfers);
    const store = TestBed.inject(ListingStore);
    vi.spyOn(store, 'load').mockResolvedValue();
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.connections.mockResolvedValue(USABLE);
    api.startTransfer.mockResolvedValue(job());
    api.fileJobs.mockResolvedValue([job()]);
});

afterEach(() => {
    enterPlace(config, null);
    vi.restoreAllMocks();
});

describe('the drag data of listed items', () => {
    it('carries their place, and reads the plain list of before as the library', () => {
        expect(parseDragItems(dragItemsData(['a/b.jpg'], 7))).toEqual({ connection: 7, paths: ['a/b.jpg'] });
        expect(parseDragItems(JSON.stringify(['a.jpg', 3]))).toEqual({ connection: null, paths: ['a.jpg'] });
        expect(parseDragItems('not json')).toEqual({ connection: null, paths: [] });
    });
});

describe('pastes between places', () => {
    it('copies from the library into a connection through a transfer job, followed in the panel', async () => {
        transfers.keep('copy', [photo]);
        config.place.set(dropbox());
        expect(transfers.paste('Trips')).toBe(true);
        await settle();

        expect(api.startTransfer).toHaveBeenCalledWith({
            kind: 'copy',
            from: { connection: null, paths: [photo.path] },
            to: { connection: 7, path: 'Trips' },
            conflicts: 'ask',
        });
        expect(api.connectionFiles).not.toHaveBeenCalled();
        expect(api.startFileJob).not.toHaveBeenCalled();
        expect(transfers.jobs().map((j) => j.id)).toEqual([5]);
    });

    it('moves from a connection into the library, and the cut is used once', async () => {
        config.place.set(dropbox());
        transfers.keep('move', [trip]);
        enterPlace(config, null);
        transfers.paste('Media');
        await settle();

        expect(api.startTransfer).toHaveBeenCalledWith({
            kind: 'move',
            from: { connection: 7, paths: [trip.path] },
            to: { connection: null, path: 'Media' },
            conflicts: 'ask',
        });
        expect(api.checkMove).not.toHaveBeenCalled();
        expect(transfers.clipboard()).toBeNull();
    });

    it('keeps the routes of one place: a library job, or the connection’s own transfer', async () => {
        api.checkMove.mockResolvedValue({ gains: [] });
        api.startFileJob.mockResolvedValue(job({ fromConnection: null, toConnection: null }));
        transfers.keep('move', [photo]);
        transfers.paste('Other');
        await settle();
        expect(api.startFileJob).toHaveBeenCalledWith({ kind: 'move', paths: [photo.path], target: 'Other' });

        config.place.set(dropbox());
        api.connectionFiles.mockResolvedValue({ items: [{ path: trip.path, status: 'done' }] });
        transfers.keep('copy', [trip]);
        transfers.paste('Saved');
        await settle();
        expect(api.connectionFiles).toHaveBeenCalledWith(7, 'transfer', {
            kind: 'copy',
            paths: [trip.path],
            target: 'Saved',
            conflict: 'fail',
        });
        expect(api.startTransfer).not.toHaveBeenCalled();
    });
});

describe('the folder picker with places', () => {
    function create() {
        const fixture = TestBed.createComponent(TransferDialog);
        fixture.detectChanges();
        return { fixture, element: fixture.nativeElement as HTMLElement };
    }

    const button = (element: HTMLElement, text: string) =>
        [...element.querySelectorAll<HTMLButtonElement>('.transfer-folder')].find((b) =>
            b.textContent!.includes(text),
        )!;

    it('lists the library and the connections written to, browses one and moves there', async () => {
        api.browse.mockResolvedValue(listing('Media', []));
        const { fixture, element } = create();
        TestBed.inject(ListingStore).route.set({ path: 'Media', q: '', sort: '', desc: false });
        transfers.pick('move', [photo]);
        await settle();
        fixture.detectChanges();

        buttonNamed(element.querySelector('.transfer-crumbs')!, 'Places')!.click();
        fixture.detectChanges();
        const places = [...element.querySelectorAll('.transfer-folder')].map((b) => b.textContent!.trim());
        expect(places).toEqual(['Root', 'Dropbox']);

        api.connectionBrowse.mockResolvedValue(connectionPage('', [item('Trips', { kind: 'directory' })]));
        button(element, 'Dropbox').click();
        await settle();
        fixture.detectChanges();
        expect(api.connectionBrowse).toHaveBeenCalledWith(7, expect.objectContaining({ path: '', sort: 'name' }));
        api.connectionBrowse.mockResolvedValue(connectionPage('Trips', []));
        button(element, 'Trips').click();
        await settle();
        fixture.detectChanges();

        expect(element.querySelector('[data-tags-travel]')).not.toBeNull();
        expect(element.textContent).not.toContain('Copy their tags too');
        buttonNamed(element, 'Move here')!.click();
        await settle();

        expect(api.checkMove).not.toHaveBeenCalled();
        expect(api.startTransfer).toHaveBeenCalledWith({
            kind: 'move',
            from: { connection: null, paths: [photo.path] },
            to: { connection: 7, path: 'Trips' },
            conflicts: 'ask',
        });
        expect(element.querySelector('dialog')!.open).toBe(false);
    });

    it('says why a connection that cannot be used lists nothing, and offers no folder there', async () => {
        api.browse.mockResolvedValue(listing('Media', []));
        const { fixture, element } = create();
        TestBed.inject(ListingStore).route.set({ path: 'Media', q: '', sort: '', desc: false });
        transfers.pick('move', [photo]);
        await settle();
        fixture.detectChanges();
        buttonNamed(element.querySelector('.transfer-crumbs')!, 'Places')!.click();
        fixture.detectChanges();

        api.connectionBrowse.mockRejectedValue(
            new ApiError(409, 'The connection must be signed in to again.', null, 'connection-needs-reconnecting'),
        );
        button(element, 'Dropbox').click();
        await settle();
        fixture.detectChanges();

        expect(element.querySelector('[data-place-problem]')!.textContent).toContain('must be signed in to again');
        expect(buttonNamed(element, 'Move here')!.disabled).toBe(true);
    });

    it('starts on the connection listed, whose items it moves and copies', async () => {
        config.place.set(dropbox());
        api.connectionBrowse.mockResolvedValue(connectionPage('Trips', []));
        TestBed.inject(ListingStore).route.set({ c: 7, path: 'Trips', q: '', sort: '', desc: false });
        const { fixture, element } = create();

        expect(transfers.pick('copy', [trip])).toBe(true);
        await settle();
        fixture.detectChanges();

        expect(api.connectionBrowse).toHaveBeenCalledWith(7, expect.objectContaining({ path: 'Trips' }));
        expect(element.querySelector('.transfer-crumbs')!.textContent).toContain('Dropbox');
        expect(element.textContent).not.toContain('Copy their tags too');
    });
});

describe('a move within a connection that lets more people in', () => {
    it('asks as the library does, then moves keeping the access when asked', async () => {
        config.place.set(dropbox());
        const fixture = TestBed.createComponent(TransferDialog);
        fixture.detectChanges();
        const element = fixture.nativeElement as HTMLElement;
        const folder = item('Private/Plans', { kind: 'directory' });
        const gains = [{ folder: folder.path, who: 'Visitors', isGroup: true, before: 'hidden', after: 'read' }];
        api.connectionFiles
            .mockResolvedValueOnce({ type: 'access-gains', detail: 'More people would see it.', gains })
            .mockResolvedValueOnce({ items: [{ path: folder.path, status: 'done' }] });

        transfers.keep('move', [folder]);
        transfers.paste('Public');
        await settle();
        fixture.detectChanges();

        expect(element.querySelector('dialog')!.open).toBe(true);
        expect(element.textContent).toContain("The group “Visitors” would see “Plans”, which they don't see now.");
        expect(api.connectionFiles).toHaveBeenCalledTimes(1);
        (element.querySelector('.transfer-gains + label input') as HTMLInputElement).click();
        buttonNamed(element, 'Move')!.click();
        await settle();

        expect(api.connectionFiles).toHaveBeenLastCalledWith(7, 'transfer', {
            kind: 'move',
            paths: [folder.path],
            target: 'Public',
            conflict: 'fail',
            keepAccess: true,
        });
        expect(api.startFileJob).not.toHaveBeenCalled();
        expect(api.startTransfer).not.toHaveBeenCalled();
        expect(element.querySelector('dialog')!.open).toBe(false);
    });
});

describe('dropping items on a connection of the sidebar', () => {
    @Component({
        selector: 'sfe-host',
        imports: [ConnectionLinksComponent],
        changeDetection: ChangeDetectionStrategy.OnPush,
        template: '<section sfeConnectionLinks></section>',
    })
    class Host {}

    function drop(target: Element, data: string, ctrlKey = false): Event {
        const event = new Event('drop', { bubbles: true, cancelable: true });
        Object.assign(event, {
            ctrlKey,
            dataTransfer: { types: [DRAG_ITEMS], getData: (type: string) => (type === DRAG_ITEMS ? data : '') },
        });
        target.dispatchEvent(event);
        return event;
    }

    it('moves them to its root (copies with Ctrl), only where the session writes', async () => {
        const transfer = vi.spyOn(transfers, 'transfer').mockResolvedValue();
        const fixture = TestBed.createComponent(Host);
        await settle();
        fixture.detectChanges();
        const [mine, shared] = [...(fixture.nativeElement as HTMLElement).querySelectorAll('li')];

        expect(drop(mine, dragItemsData([photo.path], null)).defaultPrevented).toBe(true);
        drop(mine, JSON.stringify([photo.path]), true);
        expect(drop(shared, dragItemsData([photo.path], null)).defaultPrevented).toBe(false);

        expect(transfer.mock.calls).toEqual([
            ['move', [photo.path], '', { from: null, to: 7 }],
            ['copy', [photo.path], '', { from: null, to: 7 }],
        ]);
    });
});

describe('the operations panel', () => {
    it('names both places of a transfer job', async () => {
        api.fileJobs.mockResolvedValue([
            job({ results: [{ path: 'Media/Holidays/a.jpg', newPath: 'Trips/a.jpg', status: 'done' }] }),
            job({ id: 6, kind: 'move', fromConnection: 7, toConnection: null, target: '', current: 'Trips/b.jpg' }),
        ]);
        await TestBed.inject(UsableConnections).load();
        const fixture = TestBed.createComponent(OperationsPanelComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        const titles = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.operation-title')].map((title) =>
            title.textContent!.trim(),
        );

        expect(titles).toEqual([
            'Copying 2 items from Root › Holidays to Dropbox › Trips',
            'Moving 2 items from Dropbox › Trips to Root',
        ]);
    });
});
