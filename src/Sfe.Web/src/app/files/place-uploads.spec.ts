import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionPlace } from '../core/connection.models';
import { Rights } from '../core/rights';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { enterPlace } from '../library/connection-pages';
import { UploadInfo } from './file.models';
import { UploadQueue } from './uploads';

const ID = 'a'.repeat(32);

function upload(overrides: Partial<UploadInfo> = {}): UploadInfo {
    return {
        id: ID,
        target: 'Docs',
        name: 'new.txt',
        size: 10,
        received: 0,
        modified: null,
        ownerId: 2,
        ownerName: 'alice',
        started: '2026-11-14T10:00:00Z',
        lastActivity: '2026-11-14T10:00:00Z',
        ...overrides,
    };
}

describe('Uploads into a connection', () => {
    let api: ApiMock;
    let config: ConfigService;

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        config = await loadConfig(api, fakeConfig());
    });

    afterEach(() => {
        enterPlace(config, null);
        vi.restoreAllMocks();
    });

    /** Enters connection 7 as its first page would set it. */
    function enter(changes: Partial<ConnectionPlace>): void {
        enterPlace(config, 7);
        config.place.update((place) => ({ ...place!, ...changes }));
    }

    it('gives files.upload back only with write access and a provider taking files in parts', () => {
        enter({});
        expect(config.can(Rights.uploadFiles)).toBe(false);
        enter({ access: 'write' });
        expect(config.can(Rights.uploadFiles)).toBe(false);
        enter({ access: 'read', uploadsInParts: true });
        expect(config.can(Rights.uploadFiles)).toBe(false);
        enter({ access: 'write', uploadsInParts: true });
        expect(config.can(Rights.uploadFiles)).toBe(true);
    });

    it('keeps sending an upload started on a connection to it after the listing goes back to the library', async () => {
        enter({ access: 'write', uploadsInParts: true });
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockImplementation(() => {
            // The person leaves the connection while the upload runs.
            enterPlace(config, null);
            return Promise.resolve({ existing: [], canReplace: true });
        });
        api.startUpload.mockResolvedValue(upload());
        api.sendUploadChunk.mockResolvedValue(upload({ received: 10 }));
        api.finishUpload.mockResolvedValue(item('Docs/new.txt', { size: 10 }));

        await queue.add([{ file: new File(['abcdefghij'], 'new.txt'), name: 'new.txt' }], 'Docs');
        await settle();

        expect(config.place()).toBeNull();
        expect(api.checkUploads).toHaveBeenCalledWith('Docs', ['new.txt'], 7);
        expect(api.startUpload.mock.calls[0][1]).toBe(7);
        expect(api.sendUploadChunk.mock.calls[0][4]).toBe(7);
        expect(api.finishUpload).toHaveBeenCalledWith(ID, 'ask', 7);
        expect(queue.batches()[0].connection).toBe(7);
    });

    it("cancels a connection's upload on the connection", async () => {
        enter({ access: 'write', uploadsInParts: true });
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockResolvedValue({ existing: ['new.txt'], canReplace: true });
        api.cancelUpload.mockResolvedValue(undefined);

        await queue.add([{ file: new File(['abcdefghij'], 'new.txt'), name: 'new.txt' }], 'Docs');
        const batch = queue.batches()[0];
        batch.items[0].id = ID;
        await queue.cancel(batch.items[0]);

        expect(api.cancelUpload).toHaveBeenCalledWith(ID, 7);
    });

    it("sends the library's uploads without a connection", async () => {
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockResolvedValue({ existing: [], canReplace: true });
        api.startUpload.mockResolvedValue(upload());
        api.sendUploadChunk.mockResolvedValue(upload({ received: 10 }));
        api.finishUpload.mockResolvedValue(item('Docs/new.txt', { size: 10 }));

        await queue.add([{ file: new File(['abcdefghij'], 'new.txt'), name: 'new.txt' }], 'Docs');
        await settle();

        expect(api.checkUploads).toHaveBeenCalledWith('Docs', ['new.txt'], null);
        expect(api.finishUpload).toHaveBeenCalledWith(ID, 'ask', null);
    });
});

describe('Upload routes', () => {
    let api: Api;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        api = TestBed.inject(Api);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    /** Every upload call, on `connection` (the library's when null). */
    function callAll(connection: number | null): void {
        void api.checkUploads('Docs', ['a.txt'], connection);
        void api.startUpload({ target: 'Docs', name: 'a.txt', size: 1, modified: null }, connection);
        void api.sendUploadChunk(ID, 0, new Blob(['a']), undefined, connection);
        void api.finishUpload(ID, 'ask', connection);
        void api.cancelUpload(ID, connection);
    }

    function requested(): string[] {
        return http.match(() => true).map((request) => `${request.request.method} ${request.request.url}`);
    }

    it("reach the connection's routes, the same paths below them", () => {
        callAll(7);
        expect(requested()).toEqual([
            'POST api/connections/7/files/uploads/check',
            'POST api/connections/7/files/uploads',
            `PUT api/connections/7/files/uploads/${ID}`,
            `POST api/connections/7/files/uploads/${ID}/finish`,
            `DELETE api/connections/7/files/uploads/${ID}`,
        ]);
    });

    it("stay the library's otherwise, and the unfinished uploads listed are the library's", () => {
        callAll(null);
        void api.uploads();
        expect(requested()).toEqual([
            'POST api/files/uploads/check',
            'POST api/files/uploads',
            `PUT api/files/uploads/${ID}`,
            `POST api/files/uploads/${ID}/finish`,
            `DELETE api/files/uploads/${ID}`,
            'GET api/files/uploads',
        ]);
    });
});
