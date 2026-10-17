import { TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { DirectoryListing, LibraryItem, UploadInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { OperationsPanelComponent } from './operations-panel.component';
import { UploadsPanelComponent } from './uploads-panel.component';
import { UploadsSectionComponent } from './uploads-section.component';
import { UploadQueue } from './uploads';

let api: ApiMock;
let store: ListingStore;

const photo = item('Media/beach.jpg', { size: 2048 });
const modified = Date.parse('2026-10-01T10:00:00Z');

function listing(items: LibraryItem[]): DirectoryListing {
    return { path: 'Media', name: 'Media', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: 0, fileCount: items.length };
}

function upload(overrides: Partial<UploadInfo> = {}): UploadInfo {
    return {
        id: 'a'.repeat(32), target: 'Media', name: 'new.txt', size: 10, received: 0, modified: null, ownerId: 2, ownerName: 'alice',
        started: '2026-10-17T10:00:00Z', lastActivity: '2026-10-17T10:00:00Z', ...overrides,
    };
}

function file(name: string, content = 'abcdefghij'): File {
    return new File([content], name, { lastModified: modified });
}

async function setUp(config = fakeConfig()): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing([photo]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('UploadQueue', () => {
    it('checks, starts, sends and finishes a file, which shows in the folder listed', async () => {
        await setUp();
        const queue = TestBed.inject(UploadQueue);
        const added = item('Media/new.txt', { size: 10 });
        api.checkUploads.mockResolvedValue({ existing: [], canReplace: true });
        api.startUpload.mockResolvedValue(upload());
        // The server had only half of the first chunk: the rest is sent from there.
        api.sendUploadChunk.mockResolvedValueOnce(upload({ received: 4 })).mockResolvedValueOnce(upload({ received: 10 }));
        api.finishUpload.mockResolvedValue(added);

        await queue.add([{ file: file('new.txt'), name: 'new.txt' }], 'Media');
        await settle();

        expect(api.startUpload).toHaveBeenCalledWith({ target: 'Media', name: 'new.txt', size: 10, modified: new Date(modified).toISOString() });
        expect(api.sendUploadChunk.mock.calls.map(call => call[1])).toEqual([0, 4]);
        expect(await (api.sendUploadChunk.mock.calls[1][2] as Blob).text()).toBe('efghij');
        expect(api.finishUpload).toHaveBeenCalledWith('a'.repeat(32), 'ask');
        expect(store.items().map(i => i.name)).toContain('new.txt');
        expect(TestBed.inject(ToastService).toasts().at(-1)!.message).toBe('1 file uploaded to “Media”.');
        expect(queue.busy()).toBe(false);
    });

    it('asks about names taken once for all, and skips without sending', async () => {
        await setUp();
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockResolvedValue({ existing: ['a.txt', 'b.txt'], canReplace: false });
        api.startUpload.mockImplementation(start => Promise.resolve(upload({ id: start.name[0].repeat(32), name: start.name })));
        api.sendUploadChunk.mockImplementation(id => Promise.resolve(upload({ id, received: 10 })));
        api.finishUpload.mockResolvedValue(item('Media/x.txt'));

        await queue.add([{ file: file('a.txt'), name: 'a.txt' }, { file: file('b.txt'), name: 'b.txt' }, { file: file('c.txt'), name: 'c.txt' }], 'Media');
        await settle();
        const batch = queue.batches()[0];
        expect(queue.question(batch)!.name).toBe('a.txt');
        expect(api.startUpload).toHaveBeenCalledTimes(1);

        queue.answer(batch, queue.question(batch)!, 'keepBoth', true);
        await settle();
        expect(api.finishUpload.mock.calls.map(call => call[1]).sort()).toEqual(['ask', 'keepBoth', 'keepBoth']);

    });

    it('retries a chunk that failed, and marks the file failed after the last try', async () => {
        await setUp();
        vi.useFakeTimers({ shouldAdvanceTime: true });
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockResolvedValue({ existing: [], canReplace: false });
        api.startUpload.mockResolvedValue(upload());
        api.sendUploadChunk.mockRejectedValueOnce(new ApiError(502, 'Bad gateway')).mockResolvedValueOnce(upload({ received: 10 }));
        api.finishUpload.mockResolvedValue(item('Media/new.txt'));

        await queue.add([{ file: file('new.txt'), name: 'new.txt' }], 'Media');
        await vi.advanceTimersByTimeAsync(1500);
        vi.useRealTimers();
        await settle();

        expect(api.sendUploadChunk).toHaveBeenCalledTimes(2);
        expect(queue.batches()[0].items[0].state).toBe('done');
    });

    it('goes on with an upload a reload interrupted when the same file is picked again', async () => {
        await setUp();
        const queue = TestBed.inject(UploadQueue);
        api.uploads.mockResolvedValue([upload({ received: 6, modified: new Date(modified).toISOString() })]);
        api.sendUploadChunk.mockResolvedValue(upload({ received: 10 }));
        api.finishUpload.mockResolvedValue(item('Media/new.txt'));

        await queue.resume();
        expect(queue.batches()[0].items[0].state).toBe('interrupted');
        await queue.add([{ file: file('new.txt'), name: 'new.txt' }], 'Media');
        await settle();

        expect(api.checkUploads).not.toHaveBeenCalled();
        expect(api.startUpload).not.toHaveBeenCalled();
        expect(api.sendUploadChunk.mock.calls[0][1]).toBe(6);
        expect(queue.batches()[0].items[0].state).toBe('done');
    });

    it('leaves out files larger than the space allows', async () => {
        await setUp(fakeConfig({ files: { deletePermanently: false, trashRetentionDays: 30, maxUploadMegabytes: 1 } }));
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockResolvedValue({ existing: [], canReplace: false });

        await queue.add([{ file: file('big.bin', 'x'.repeat(1024 * 1024 + 1)), name: 'big.bin' }], 'Media');

        expect(api.checkUploads).not.toHaveBeenCalled();
        expect(TestBed.inject(ToastService).toasts().at(-1)!.message).toContain('larger than the 1 MB');
    });

    it('offers uploads only with the right, where the folder rules allow', async () => {
        await setUp(limitedConfig([Rights.browse]));
        expect(TestBed.inject(UploadQueue).canUploadInto()).toBe(false);
        await loadConfig(api, limitedConfig([Rights.browse, Rights.uploadFiles]));
        expect(TestBed.inject(UploadQueue).canUploadInto()).toBe(true);
        api.browse.mockResolvedValue({ ...listing([photo]), access: { rights: [Rights.browse], pathOnly: false } });
        await store.load();
        expect(TestBed.inject(UploadQueue).canUploadInto()).toBe(false);
    });
});

describe('UploadsPanelComponent', () => {
    it('shows the progress, the question about a name taken, and pauses and cancels', async () => {
        await setUp();
        const queue = TestBed.inject(UploadQueue);
        api.checkUploads.mockResolvedValue({ existing: ['a.txt'], canReplace: true });
        api.startUpload.mockResolvedValue(upload({ name: 'b.txt' }));
        api.sendUploadChunk.mockReturnValue(new Promise(() => { }));
        api.cancelUpload.mockResolvedValue(undefined);
        await queue.add([{ file: file('a.txt'), name: 'a.txt' }, { file: file('b.txt'), name: 'b.txt' }], 'Media');
        const fixture = TestBed.createComponent(UploadsPanelComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        const element: HTMLElement = fixture.nativeElement;

        expect(element.textContent).toContain('Uploading 2 files to “Media”');
        expect(element.textContent).toContain('“a.txt” is already there.');
        expect(buttonNamed(element, 'Replace')).toBeDefined();
        buttonNamed(element, 'Skip')!.click();
        fixture.detectChanges();
        expect(element.textContent).toContain('Uploading 1 file to “Media”');

        buttonNamed(element.querySelector('.operation-head')!, 'Pause')!.click();
        fixture.detectChanges();
        expect(element.textContent).toContain('paused at 0%');
        buttonNamed(element, 'Cancel')!.click();
        await settle();
        fixture.detectChanges();
        expect(api.cancelUpload).toHaveBeenCalledWith('a'.repeat(32));
        expect(element.textContent).toContain('0 files uploaded to “Media”');
    });
});

describe('UploadsSectionComponent', () => {
    it('lists everyone\'s unfinished uploads and cancels one', async () => {
        await setUp();
        api.uploads.mockResolvedValue([upload({ received: 5, name: 'Trip/clip.mp4' })]);
        api.cancelUpload.mockResolvedValue(undefined);
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const fixture = TestBed.createComponent(UploadsSectionComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        const element: HTMLElement = fixture.nativeElement;

        expect(api.uploads).toHaveBeenCalledWith(true);
        expect(element.textContent).toContain('clip.mp4');
        expect(element.textContent).toContain('(50%) · by alice');
        buttonNamed(element, 'Cancel')!.click();
        await settle();
        fixture.detectChanges();
        expect(api.cancelUpload).toHaveBeenCalledWith('a'.repeat(32));
        expect(element.textContent).toContain('No upload under way.');
    });
});

describe('Dropping files from the computer', () => {
    it('uploads them into the folder dropped on, else the folder listed', async () => {
        await setUp();
        const fixture = TestBed.createComponent(OperationsPanelComponent);
        fixture.detectChanges();
        const add = vi.spyOn(TestBed.inject(UploadQueue), 'add').mockResolvedValue();
        const page = document.createElement('main');
        page.className = 'page';
        const folder = document.createElement('div');
        folder.setAttribute('data-drop', 'Media/Holidays');
        page.append(folder);
        document.body.append(page);
        const dropped = file('a.txt');

        const drop = (target: Element) => {
            const event = new Event('drop', { bubbles: true, cancelable: true });
            Object.assign(event, { dataTransfer: { types: ['Files'], items: [], files: [dropped] } });
            target.dispatchEvent(event);
            return event;
        };
        expect(drop(folder).defaultPrevented).toBe(true);
        drop(page);
        await settle();
        page.remove();

        expect(add.mock.calls).toEqual([[[{ file: dropped, name: 'a.txt' }], 'Media/Holidays'], [[{ file: dropped, name: 'a.txt' }], 'Media']]);
    });
});

describe('UploadQueue behind a proxy', () => {
    it('sends smaller chunks when a chunk is refused as too large', async () => {
        await setUp();
        const queue = TestBed.inject(UploadQueue);
        const size = 9 * 1024 * 1024;
        api.checkUploads.mockResolvedValue({ existing: [], canReplace: false });
        api.startUpload.mockResolvedValue(upload({ size }));
        api.sendUploadChunk
            .mockRejectedValueOnce(new ApiError(413, 'Request Entity Too Large'))
            .mockImplementation((_id, offset, chunk) => Promise.resolve(upload({ size, received: offset + chunk.size })));
        api.finishUpload.mockResolvedValue(item('Media/big.bin'));

        await queue.add([{ file: new File([new Uint8Array(size)], 'big.bin'), name: 'big.bin' }], 'Media');
        await settle();

        const sizes = api.sendUploadChunk.mock.calls.map(call => (call[2] as Blob).size);
        expect(sizes[0]).toBe(8 * 1024 * 1024);
        expect(sizes.slice(1)).toEqual([4 * 1024 * 1024, 4 * 1024 * 1024, 1024 * 1024]);
        expect(queue.batches()[0].items[0].state).toBe('done');
    });
});
