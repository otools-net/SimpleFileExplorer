import { TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { DirectoryListing, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { ListingStore } from '../library/listing.store';
import { SelectionBarComponent } from '../library/selection-bar.component';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { FileDownloads } from './downloads';
import { ItemMenu } from './item-menu';

let api: ApiMock;
let store: ListingStore;
/** The links the browser was asked to download: [href, download]. */
let saved: [string, string][];

const photo = item('Media/beach.jpg');
const clip = item('Media/clip.mp4');
const folder = item('Media/Holidays', { kind: 'directory' });

function listing(items: LibraryItem[], overrides: Partial<DirectoryListing> = {}): DirectoryListing {
    return { path: 'Media', name: 'Media', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: 1, fileCount: items.length - 1, ...overrides };
}

async function setUp(config = fakeConfig()): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing([folder, photo, clip]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
    saved = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        saved.push([this.getAttribute('href')!, this.download]);
    });
}

afterEach(() => vi.restoreAllMocks());

describe('FileDownloads', () => {
    it('downloads one file as it is', async () => {
        await setUp();

        expect(await TestBed.inject(FileDownloads).download([photo])).toBe(true);

        expect(saved).toEqual([['api/files/raw/Media/beach.jpg?download=true', 'beach.jpg']]);
        expect(api.createArchive).not.toHaveBeenCalled();
    });

    it('downloads several files or a folder as a zip from the link the server answers', async () => {
        await setUp();
        api.createArchive.mockResolvedValue({ url: 'api/files/archive/' + 'a'.repeat(48), fileName: 'Media.zip' });
        const downloads = TestBed.inject(FileDownloads);

        await downloads.download([photo, clip]);
        await downloads.download([folder]);

        expect(api.createArchive.mock.calls).toEqual([[['Media/beach.jpg', 'Media/clip.mp4']], [['Media/Holidays']]]);
        expect(saved).toEqual([['api/files/archive/' + 'a'.repeat(48), 'Media.zip'], ['api/files/archive/' + 'a'.repeat(48), 'Media.zip']]);
        expect(TestBed.inject(ToastService).toasts().at(-1)!.message).toContain('Downloading Media.zip');
    });

    it('says why a zip is refused', async () => {
        await setUp();
        api.createArchive.mockRejectedValue(new ApiError(400, 'This is 5 MB: a zip may hold at most 1 MB here. Choose fewer files.'));

        await TestBed.inject(FileDownloads).download([folder]);

        expect(saved).toEqual([]);
        expect(TestBed.inject(ToastService).toasts().at(-1)!.message).toBe('Could not download: This is 5 MB: a zip may hold at most 1 MB here. Choose fewer files.');
    });

    it('leaves out what folder rules keep from downloading', async () => {
        await setUp();
        api.browse.mockResolvedValue(listing([folder, photo, clip], {
            access: { rights: [Rights.browse, Rights.viewFiles, Rights.download], pathOnly: false, children: { 'clip.mp4': { rights: [Rights.browse, Rights.viewFiles], pathOnly: false } } },
        }));
        await store.load();
        const downloads = TestBed.inject(FileDownloads);

        expect(downloads.canDownload(clip)).toBe(false);
        expect(downloads.isZip([photo, clip])).toBe(false);
        await downloads.download([photo, clip]);

        expect(saved).toEqual([['api/files/raw/Media/beach.jpg?download=true', 'beach.jpg']]);
    });

    it('offers nothing without the right to download', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.viewFiles]));

        expect(TestBed.inject(FileDownloads).canDownload(photo)).toBe(false);
        expect(await TestBed.inject(FileDownloads).download([photo])).toBe(false);
        expect(saved).toEqual([]);
    });
});

describe('Download in the selection bar and the item menu', () => {
    it('downloads the selection from the selection bar', async () => {
        await setUp();
        api.createArchive.mockResolvedValue({ url: 'api/files/archive/' + 'b'.repeat(48), fileName: 'Media.zip' });
        const fixture = TestBed.createComponent(SelectionBarComponent);
        store.select(photo, true);
        store.select(folder, true);
        await settle();
        fixture.detectChanges();

        const button = buttonNamed(fixture.nativeElement, 'Download')!;
        expect(button.title).toContain('zip');
        button.click();
        await settle();

        expect(api.createArchive).toHaveBeenCalledWith(['Media/Holidays', 'Media/beach.jpg']);
    });

    it('has no Download button for those who may not download', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.viewFiles]));
        const fixture = TestBed.createComponent(SelectionBarComponent);
        store.select(photo, true);
        await settle();
        fixture.detectChanges();

        expect(buttonNamed(fixture.nativeElement, 'Download')).toBeUndefined();
    });

    it('names a folder\'s download a zip in its menu, and acts on the whole selection', async () => {
        await setUp();
        const menu = TestBed.inject(ItemMenu);

        expect(menu.entries(folder).find(entry => entry.id === 'download')!.label).toBe('Download as zip');
        expect(menu.entries(photo).find(entry => entry.id === 'download')!.label).toBe('Download');
        store.select(photo, true);
        store.select(clip, true);
        expect(menu.entries(photo).find(entry => entry.id === 'download')!.label).toBe('Download as zip');
    });
});
