import { TestBed } from '@angular/core/testing';
import { DirectoryListing, FileJobView, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { isExtractable } from '../formats/formats';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { ExtractDialog, stemOf } from './extract.dialog';
import { FileTransfers } from './file-transfers';
import { ItemMenu } from './item-menu';
import { OperationsPanelComponent } from './operations-panel.component';

let api: ApiMock;
let store: ListingStore;

const folder = item('Media/Holidays', { kind: 'directory' });
const zip = item('Media/trip.zip', { size: 4096 });
const photo = item('Media/beach.jpg', { size: 2048 });
const archives = { archives: { maxCompressedTarMegabytes: 2048 } };

function listing(path: string, items: LibraryItem[], access: DirectoryListing['access'] = null): DirectoryListing {
    const folders = items.filter(i => i.kind === 'directory').length;
    return { path, name: path.split('/').pop()!, parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: folders, fileCount: items.length - folders, access };
}

function job(overrides: Partial<FileJobView> = {}): FileJobView {
    return {
        id: 1, kind: 'extract', state: 'running', target: 'Media', total: 1, done: 0, bytesTotal: 4096, bytesDone: 1024, current: zip.path, conflict: null,
        results: [], started: '2026-10-26T10:00:00Z', finished: null, ...overrides,
    };
}

async function setUp(config = fakeConfig(archives)): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing('Media', [folder, zip, photo]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('Extracting archives', () => {
    it('knows archives by their names', async () => {
        await setUp();
        for (const name of ['a.zip', 'a.7z', 'a.rar', 'a.tar', 'a.tar.gz', 'a.tgz', 'a.tar.xz', 'notes.txt.gz', 'a.bz2'])
            expect(isExtractable(item(`x/${name}`)), name).toBe(true);
        for (const name of ['a.txt', '.zip', 'a.zip.txt'])
            expect(isExtractable(item(`x/${name}`)), name).toBe(false);
        expect(isExtractable(item('x/a.zip', { kind: 'directory' }))).toBe(false);
        expect(stemOf('trip.tar.gz')).toBe('trip');
        expect(stemOf('trip.zip')).toBe('trip');
        expect(stemOf('.zip')).toBe('.zip');
    });

    it('offers Extract to “name” and Extract… in an archive\'s menu, by rights', async () => {
        await setUp();
        const open = vi.fn();
        TestBed.inject(Dialogs).extract.set({ open } as never);
        const menu = TestBed.inject(ItemMenu);
        api.startExtract.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job()]);

        const entries = menu.entries(zip);
        expect(entries.map(e => e.id)).toContain('extractBeside');
        expect(entries.find(e => e.id === 'extractBeside')!.label).toBe('Extract to “trip”');
        expect(menu.entries(photo).map(e => e.id)).not.toContain('extract');
        entries.find(e => e.id === 'extract')!.run();
        expect(open).toHaveBeenCalledWith({ path: zip.path, name: zip.name });
        entries.find(e => e.id === 'extractBeside')!.run();
        await settle();
        expect(api.startExtract).toHaveBeenCalledWith({ archive: zip.path, target: 'Media', newFolder: true });

        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewFiles, Rights.download], archives));
        expect(menu.entries(zip).map(e => e.id)).not.toContain('extract');
    });

    it('says when it is done, with what came out and a way to open it', async () => {
        await setUp();
        const toasts = TestBed.inject(ToastService);
        api.startExtract.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job({
            state: 'completed', done: 1, finished: '2026-10-26T10:00:01Z',
            results: [{ path: zip.path, newPath: 'Media/trip', status: 'done', message: '3 files extracted (2 KB).' }],
        })]);

        await TestBed.inject(FileTransfers).extract({ archive: zip.path, target: 'Media', newFolder: true });
        await settle();

        const done = toasts.toasts().find(t => t.message.includes('extracted'))!;
        expect(done.message).toBe('“trip.zip” extracted to “trip”. 3 files extracted (2 KB).');
        const navigate = vi.spyOn(store, 'navigate').mockImplementation(() => undefined as never);
        toasts.act(done, done.actions[0]);
        expect(navigate).toHaveBeenCalledWith({ path: 'Media/trip' });
    });

    it('says why it failed', async () => {
        await setUp();
        const toasts = TestBed.inject(ToastService);
        api.startExtract.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job({
            state: 'completed', done: 1, results: [{ path: zip.path, newPath: null, status: 'failed', message: 'That password doesn\'t open “trip.zip”: nothing was extracted.' }],
        })]);

        await TestBed.inject(FileTransfers).extract({ archive: zip.path, target: 'Media', newFolder: true, password: 'wrong' });
        await settle();

        expect(toasts.toasts().map(t => t.message)).toContain('That password doesn\'t open “trip.zip”: nothing was extracted.');
    });
});

describe('ExtractDialog', () => {
    function create() {
        const fixture = TestBed.createComponent(ExtractDialog);
        fixture.detectChanges();
        return { fixture, element: fixture.nativeElement as HTMLElement };
    }

    it('picks the folder, starting with the archive\'s, and extracts there with the password', async () => {
        await setUp();
        const { fixture, element } = create();
        api.startExtract.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job()]);

        TestBed.inject(Dialogs).extract()!.open({ path: zip.path, name: zip.name });
        await settle();
        fixture.detectChanges();

        expect(api.browse.mock.lastCall![0]).toMatchObject({ path: 'Media' });
        expect(element.textContent).toContain('Extract “trip.zip” into:');
        expect(element.textContent).toContain('Into a new folder “trip”');
        const password = element.querySelector<HTMLInputElement>('[data-extract-password]')!;
        expect(password.type).toBe('password');
        password.value = 'secret';
        password.dispatchEvent(new Event('input'));
        buttonNamed(element, 'Extract here')!.click();
        await settle();

        expect(api.startExtract).toHaveBeenCalledWith({ archive: zip.path, target: 'Media', newFolder: true, folders: undefined, files: undefined, unlock: null, password: 'secret' });
    });

    it('takes the entries chosen in the viewer with its unlock, and refuses a folder where the session may not extract', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.viewFiles, Rights.download, Rights.extractFiles], archives));
        const { fixture, element } = create();
        api.startExtract.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job()]);
        api.browse.mockResolvedValue(listing('Media', [folder], { rights: [Rights.browse], pathOnly: false }));

        TestBed.inject(Dialogs).extract()!.open({ path: zip.path, name: zip.name, folders: [4], what: '“Day 1”', unlock: 'abc' });
        await settle();
        fixture.detectChanges();

        expect(element.textContent).toContain('Extract “Day 1” of “trip.zip” into:');
        expect(element.querySelector('[data-extract-password]'), 'the viewer has its password').toBeNull();
        expect(element.textContent).toContain('You may not extract archives into this folder');
        expect(buttonNamed(element, 'Extract here')!.disabled).toBe(true);

        api.browse.mockResolvedValue(listing('Media/Holidays', [], { rights: [Rights.browse, Rights.extractFiles], pathOnly: false }));
        [...element.querySelectorAll<HTMLButtonElement>('.transfer-folder')].find(b => b.textContent!.includes('Holidays'))!.click();
        await settle();
        fixture.detectChanges();
        (element.querySelector('[data-new-folder]') as HTMLInputElement).click();
        buttonNamed(element, 'Extract here')!.click();
        await settle();

        expect(api.startExtract).toHaveBeenCalledWith({ archive: zip.path, target: 'Media/Holidays', newFolder: false, folders: [4], files: undefined, unlock: 'abc', password: null });
    });
});

describe('OperationsPanelComponent, extractions', () => {
    it('shows how far into the archive it is, then what it did and left out', async () => {
        await setUp();
        api.fileJobs.mockResolvedValue([job()]);
        const fixture = TestBed.createComponent(OperationsPanelComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        const element = fixture.nativeElement as HTMLElement;

        expect(element.textContent).toContain('Extracting “trip.zip” to “Media”');
        expect(element.textContent).toContain('1 KB of 4 KB read');

        api.fileJobs.mockResolvedValue([job({
            state: 'completed', done: 1, results: [{ path: zip.path, newPath: 'Media/trip', status: 'done', message: '2 files extracted (1 KB). Left out 1 entry: “../x”: the path goes up.' }],
        })]);
        api.cancelFileJob.mockResolvedValue(undefined);
        await TestBed.inject(FileTransfers).cancel(job());
        await settle();
        fixture.detectChanges();

        expect(element.textContent).toContain('“trip.zip” extracted to “Media”');
        expect(element.textContent).toContain('Left out 1 entry');
    });
});
