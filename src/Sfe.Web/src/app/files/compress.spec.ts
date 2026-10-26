import { TestBed } from '@angular/core/testing';
import { DirectoryListing, FileJobView, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { CompressDialog, defaultName } from './compress.dialog';
import { FileTransfers } from './file-transfers';
import { ItemMenu } from './item-menu';
import { OperationsPanelComponent } from './operations-panel.component';

let api: ApiMock;
let store: ListingStore;

const folder = item('Media/Holidays', { kind: 'directory' });
const photo = item('Media/beach.jpg', { size: 2048 });
const notes = item('Media/notes.txt', { size: 100 });

function listing(path: string, items: LibraryItem[]): DirectoryListing {
    const folders = items.filter(i => i.kind === 'directory').length;
    return { path, name: path.split('/').pop()!, parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: folders, fileCount: items.length - folders, access: null };
}

function job(overrides: Partial<FileJobView> = {}): FileJobView {
    return {
        id: 1, kind: 'compress', state: 'running', target: 'Media', total: 1, done: 0, bytesTotal: 4096, bytesDone: 1024, current: null, conflict: null,
        results: [], started: '2026-10-26T10:00:00Z', finished: null, ...overrides,
    };
}

async function setUp(config = fakeConfig()): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing('Media', [folder, photo, notes]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('Compressing', () => {
    it('names the zip after its only item, else after the folder', () => {
        expect(defaultName([folder], 'Media', 'Library')).toBe('Holidays');
        expect(defaultName([photo], 'Media', 'Library')).toBe('beach');
        expect(defaultName([photo, notes], 'Media', 'Library')).toBe('Media');
        expect(defaultName([photo, notes], '', 'Library')).toBe('Library');
    });

    it('offers Compress… on items and the selection, by rights', async () => {
        await setUp();
        const open = vi.fn();
        TestBed.inject(Dialogs).compress.set({ open } as never);
        const menu = TestBed.inject(ItemMenu);
        store.select(photo, true);
        store.select(notes, true);

        menu.entries(photo).find(e => e.id === 'compress')!.run();
        expect(open).toHaveBeenCalledWith([photo, notes]);

        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewFiles, Rights.download]));
        expect(menu.entries(photo).map(e => e.id)).not.toContain('compress');
    });

    it('says the ratio when it is done, and shows the zip in its folder', async () => {
        await setUp();
        const toasts = TestBed.inject(ToastService);
        api.startCompress.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job({
            state: 'completed', done: 1, results: [{ path: 'Media', newPath: 'Media/trip.zip', status: 'done', message: '3 files: 4 KB → 1 KB (25 %).' }],
        })]);

        await TestBed.inject(FileTransfers).compress({ paths: [photo.path], target: 'Media', name: 'trip', level: 6, skipCompressed: true });
        await settle();

        const done = toasts.toasts().find(t => t.message.includes('made in'))!;
        expect(done.message).toBe('“trip.zip” made in “Media”. 3 files: 4 KB → 1 KB (25 %).');
        const reveal = vi.spyOn(store, 'reveal').mockImplementation(() => undefined);
        toasts.act(done, done.actions[0]);
        expect(reveal).toHaveBeenCalledWith('Media/trip.zip');
    });
});

describe('CompressDialog', () => {
    function create() {
        const fixture = TestBed.createComponent(CompressDialog);
        fixture.detectChanges();
        return { fixture, element: fixture.nativeElement as HTMLElement };
    }

    function type(element: HTMLElement, selector: string, value: string): void {
        const input = element.querySelector<HTMLInputElement>(selector)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
    }

    it('makes the zip in the folder listed, at the level chosen, with a password typed twice', async () => {
        await setUp();
        const { fixture, element } = create();
        api.startCompress.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job()]);

        TestBed.inject(Dialogs).compress()!.open([photo, notes]);
        fixture.detectChanges();
        expect(element.textContent).toContain('Put 2 items into a zip in “Media”.');
        expect(element.querySelector<HTMLInputElement>('[data-zip-name]')!.value).toBe('Media');
        expect([...element.querySelectorAll('option')].map(o => o.textContent!.split(':')[0])).toEqual(['Store', 'Fastest', 'Normal', 'Maximum']);
        expect(element.querySelector<HTMLSelectElement>('[data-level]')!.value).toBe('6');

        type(element, '[data-zip-name]', 'holiday');
        const select = element.querySelector<HTMLSelectElement>('[data-level]')!;
        select.value = '9';
        select.dispatchEvent(new Event('change'));
        type(element, '[data-zip-password]', 'pässwort');
        fixture.detectChanges();
        expect(element.querySelector('[data-password-notes]')!.textContent).toContain('File names stay visible');
        expect(element.querySelector('[data-password-notes]')!.textContent).toContain('may not work in some programs');
        expect(element.querySelector('[data-compress-problem]')!.textContent).toContain('The two passwords differ.');
        expect(buttonNamed(element, 'Compress')!.disabled).toBe(true);

        type(element, '[data-zip-confirm]', 'pässwort');
        fixture.detectChanges();
        buttonNamed(element, 'Compress')!.click();
        await settle();

        expect(api.startCompress).toHaveBeenCalledWith({ paths: [photo.path, notes.path], target: 'Media', name: 'holiday', level: 9, skipCompressed: true, password: 'pässwort' });
    });

    it('needs a name the system takes', async () => {
        await setUp();
        const { fixture, element } = create();
        TestBed.inject(Dialogs).compress()!.open([folder]);
        fixture.detectChanges();
        expect(element.querySelector<HTMLInputElement>('[data-zip-name]')!.value).toBe('Holidays');

        type(element, '[data-zip-name]', 'a:b');
        fixture.detectChanges();
        expect(buttonNamed(element, 'Compress')!.disabled).toBe(true);
        type(element, '[data-zip-name]', ' ');
        fixture.detectChanges();
        expect(element.textContent).toContain('Give the zip a name.');
    });
});

describe('OperationsPanelComponent, compressions', () => {
    it('shows what is compressed, then the zip made and its ratio', async () => {
        await setUp();
        api.fileJobs.mockResolvedValue([job()]);
        const fixture = TestBed.createComponent(OperationsPanelComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        const element = fixture.nativeElement as HTMLElement;

        expect(element.textContent).toContain('Compressing into “Media”');
        expect(element.textContent).toContain('1 KB of 4 KB compressed');

        api.fileJobs.mockResolvedValue([job({ state: 'completed', done: 1, results: [{ path: 'Media', newPath: 'Media/trip.zip', status: 'done', message: '3 files: 4 KB → 1 KB (25 %).' }] })]);
        api.cancelFileJob.mockResolvedValue(undefined);
        await TestBed.inject(FileTransfers).cancel(job());
        await settle();
        fixture.detectChanges();

        expect(element.textContent).toContain('“trip.zip” made in “Media”');
        expect(element.textContent).toContain('4 KB → 1 KB (25 %)');
    });
});
