import { TestBed } from '@angular/core/testing';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionListing, ConnectionPlace } from '../core/connection.models';
import { DirectoryListing } from '../core/models';
import { PlaceFiles } from '../core/place-files';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
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
import { connectionPage, enterPlace } from '../library/connection-pages';
import { ListingStore } from '../library/listing.store';
import { DeleteDialog } from './delete.dialog';
import { FileTransfers } from './file-transfers';

const FILE_CHANGES = [
    Rights.createFolders,
    Rights.renameFiles,
    Rights.deleteFiles,
    Rights.moveFiles,
    Rights.copyFiles,
    Rights.uploadFiles,
    Rights.createFiles,
    Rights.extractFiles,
    Rights.compressFiles,
    Rights.deletePermanently,
];

const photo = item('Trips/beach.jpg', { size: 2048 });

let api: ApiMock;
let config: ConfigService;

function place(overrides: Partial<ConnectionPlace> = {}): ConnectionPlace {
    return {
        id: 7,
        name: 'My Dropbox',
        thumbnails: false,
        access: 'write',
        serverSideMove: true,
        serverSideCopy: true,
        ownTrash: true,
        uploadsInParts: false,
        ...overrides,
    };
}

function page(access: 'read' | 'write', capabilities: ConnectionListing['capabilities']): ConnectionListing {
    const listing: DirectoryListing = {
        path: '',
        name: 'My Dropbox',
        parentPath: null,
        items: [],
        offset: 0,
        limit: 100,
        total: 0,
        directoryCount: 0,
        fileCount: 0,
    };
    return {
        connectionId: 7,
        name: 'My Dropbox',
        kind: 'dropbox',
        listing,
        access,
        sorted: true,
        hasMore: false,
        capabilities,
    };
}

function fileChanges(): string[] {
    return FILE_CHANGES.filter((right) => config.can(right));
}

beforeEach(async () => {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    config = await loadConfig(api, fakeConfig());
});

afterEach(() => {
    enterPlace(config, null);
    vi.restoreAllMocks();
});

describe('the rights on a connection', () => {
    const query = { path: '', offset: 0, limit: 100, sort: 'name' as const, desc: false };

    it('gives back only copies (out of it) where the session only reads', async () => {
        api.connectionBrowse.mockResolvedValue(page('read', { serverSideMove: true, serverSideCopy: true }));
        await connectionPage(TestBed.inject(Api), config, 7, query, new AbortController().signal);

        expect(config.place()).toEqual(place({ access: 'read', ownTrash: false }));
        expect(fileChanges()).toEqual([Rights.copyFiles]);
        // Copied out of it, never pasted into it.
        expect(TestBed.inject(FileTransfers).canInto('copy')).toBe(false);
        expect(config.can(Rights.viewFiles) && config.can(Rights.download)).toBe(true);
    });

    it('gives back new folders, renames, deletes, moves and copies where it writes', async () => {
        api.connectionBrowse.mockResolvedValue(
            page('write', { serverSideMove: false, serverSideCopy: false, ownTrash: true }),
        );
        await connectionPage(TestBed.inject(Api), config, 7, query, new AbortController().signal);
        expect(config.place()?.ownTrash).toBe(true);
        expect(fileChanges()).toEqual(
            expect.arrayContaining([
                Rights.createFolders,
                Rights.renameFiles,
                Rights.deleteFiles,
                Rights.moveFiles,
                Rights.copyFiles,
            ]),
        );
        expect(config.can(Rights.assignTags)).toBe(false);
    });

    it('starts read-only when a page enters a connection before it is listed', () => {
        enterPlace(config, 7);

        expect(config.place()).toEqual(
            place({ name: '', access: 'read', serverSideMove: false, serverSideCopy: false, ownTrash: false }),
        );
        expect(fileChanges()).toEqual([Rights.copyFiles]);
    });
});

describe('PlaceFiles', () => {
    let files: PlaceFiles;

    beforeEach(() => {
        files = TestBed.inject(PlaceFiles);
        for (const method of ['createFolder', 'renameItem', 'deleteItems', 'connectionFiles'] as const)
            api[method].mockResolvedValue({} as never);
    });

    it('uses the library routes in the library', async () => {
        await files.createFolder('Trips', 'New');
        await files.rename('Trips/a.jpg', 'b.jpg');
        api.deleteItems.mockResolvedValue({ items: [], permanent: true });
        await files.delete(['Trips/a.jpg'], true);

        expect(api.createFolder).toHaveBeenCalledWith('Trips', 'New');
        expect(api.renameItem).toHaveBeenCalledWith('Trips/a.jpg', 'b.jpg');
        expect(api.deleteItems).toHaveBeenCalledWith(['Trips/a.jpg'], true);
        expect(api.connectionFiles).not.toHaveBeenCalled();
        await expect(files.transfer('move', ['a'], 'b', 'fail')).rejects.toThrow();
    });

    it("uses the connection's routes on a connection", async () => {
        config.place.set(place());
        await files.createFolder('Trips', 'New');
        await files.rename('Trips/a.jpg', 'b.jpg');
        api.connectionFiles.mockResolvedValue({ items: [], permanent: false });
        await files.delete(['Trips/a.jpg'], false);
        await files.transfer('copy', ['Trips/a.jpg'], 'Saved', 'keepBoth');

        expect(api.connectionFiles.mock.calls).toEqual([
            [7, 'folders', { path: 'Trips', name: 'New' }],
            [7, 'rename', { path: 'Trips/a.jpg', name: 'b.jpg' }],
            [7, 'delete', { paths: ['Trips/a.jpg'], permanent: false }],
            [7, 'transfer', { kind: 'copy', paths: ['Trips/a.jpg'], target: 'Saved', conflict: 'keepBoth' }],
        ]);
        expect(api.createFolder).not.toHaveBeenCalled();
        expect(api.renameItem).not.toHaveBeenCalled();
        expect(api.deleteItems).not.toHaveBeenCalled();
    });

    it('fails a delete answered by a problem (409: sign in again)', async () => {
        config.place.set(place());
        api.connectionFiles.mockResolvedValue({ type: 'connection-needs-reconnecting', detail: 'Sign in again.' });

        await expect(files.delete(['Trips/a.jpg'], false)).rejects.toThrow('Sign in again.');
    });
});

describe('the delete dialog on a connection', () => {
    function open(ownTrash: boolean) {
        config.place.set(place({ ownTrash }));
        const fixture = TestBed.createComponent(DeleteDialog);
        fixture.detectChanges();
        fixture.componentInstance.open([photo], true);
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    it('says the provider keeps them, without a permanent choice, and deletes through the connection', async () => {
        const element = open(true);
        const text = element.textContent!;
        api.connectionFiles.mockResolvedValue({ items: [{ path: photo.path, status: 'done' }], permanent: false });

        expect(text).toContain('Delete 1 file (2 KB) from My Dropbox?');
        expect(text).toContain('The provider keeps deleted files for a while.');
        expect(text).not.toContain('permanently');
        buttonNamed(element, 'Delete')!.click();
        await settle();

        expect(api.connectionFiles).toHaveBeenCalledWith(7, 'delete', { paths: [photo.path], permanent: false });
        expect(TestBed.inject(ToastService).toasts()[0]).toMatchObject({ message: '1 item deleted.', actions: [] });
    });

    it("says it can't be undone where the provider keeps nothing", () => {
        const text = open(false).textContent!;

        expect(text).toContain("This can't be undone.");
        expect(text).not.toContain('keeps deleted files');
    });
});

describe('moves and copies on a connection', () => {
    let transfers: FileTransfers;
    let toasts: ToastService;

    beforeEach(() => {
        transfers = TestBed.inject(FileTransfers);
        toasts = TestBed.inject(ToastService);
        const store = TestBed.inject(ListingStore);
        vi.spyOn(store, 'load').mockResolvedValue();
    });

    it('pastes on the same connection through the provider, asking about names taken', async () => {
        config.place.set(place());
        api.connectionFiles
            .mockResolvedValueOnce({
                items: [
                    { path: 'Trips/a.jpg', status: 'done', item: item('Saved/a.jpg') },
                    { path: photo.path, status: 'exists' },
                ],
            })
            .mockResolvedValueOnce({ items: [{ path: photo.path, status: 'done' }] });
        transfers.keep('copy', [item('Trips/a.jpg'), photo]);
        transfers.paste('Saved');
        await settle();

        expect(api.connectionFiles).toHaveBeenCalledWith(7, 'transfer', {
            kind: 'copy',
            paths: ['Trips/a.jpg', photo.path],
            target: 'Saved',
            conflict: 'fail',
        });
        expect(api.checkMove).not.toHaveBeenCalled();
        expect(api.startFileJob).not.toHaveBeenCalled();
        const asked = toasts.toasts().find((toast) => toast.message.includes('already in'))!;
        expect(asked.message).toBe('“beach.jpg” already in “Saved”: not copied.');
        toasts.act(
            asked,
            asked.actions.find((action) => action.label === 'Keep both'),
        );
        await settle();

        expect(api.connectionFiles).toHaveBeenLastCalledWith(7, 'transfer', {
            kind: 'copy',
            paths: [photo.path],
            target: 'Saved',
            conflict: 'keepBoth',
        });
        expect(TestBed.inject(ListingStore).load).toHaveBeenCalledTimes(2);
    });
});
