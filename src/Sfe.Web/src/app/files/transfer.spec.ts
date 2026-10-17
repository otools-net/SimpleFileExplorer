import { TestBed } from '@angular/core/testing';
import { DirectoryListing, FileJobView, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { DRAG_ITEMS } from '../library/quick-links/quick-links.service';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { FileTransfers } from './file-transfers';
import { ItemMenu } from './item-menu';
import { OperationsPanelComponent } from './operations-panel.component';
import { TransferDialog } from './transfer.dialog';

let api: ApiMock;
let store: ListingStore;

const folder = item('Media/Holidays', { kind: 'directory' });
const photo = item('Media/beach.jpg', { size: 2048 });
const clip = item('Media/clip.mp4', { size: 1024 });

function listing(path: string, items: LibraryItem[], access: DirectoryListing['access'] = null): DirectoryListing {
    const folders = items.filter(i => i.kind === 'directory').length;
    return { path, name: path.split('/').pop()!, parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: folders, fileCount: items.length - folders, access };
}

function job(overrides: Partial<FileJobView> = {}): FileJobView {
    return {
        id: 1, kind: 'move', state: 'running', target: 'Media/Holidays', total: 1, done: 0, bytesTotal: 0, bytesDone: 0, current: null, conflict: null,
        results: [], started: '2026-10-17T10:00:00Z', finished: null, ...overrides,
    };
}

async function setUp(config = fakeConfig()): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing('Media', [folder, photo, clip]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('TransferDialog', () => {
    function create() {
        const fixture = TestBed.createComponent(TransferDialog);
        fixture.detectChanges();
        return { dialog: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
    }

    it('picks a folder by clicking through the library, then moves there', async () => {
        await setUp();
        const { element } = create();
        const transfers = TestBed.inject(FileTransfers);
        api.startFileJob.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([job()]);
        api.checkMove.mockResolvedValue({ gains: [] });

        expect(transfers.pick('move', [photo, folder])).toBe(true);
        await settle();
        expect(api.browse.mock.lastCall![0]).toMatchObject({ path: 'Media', sort: 'name' });
        expect(element.textContent).toContain('Move 1 folder and 1 file into:');
        expect(buttonNamed(element, 'Move here')!.disabled).toBe(true);
        const holidays = [...element.querySelectorAll<HTMLButtonElement>('.transfer-folder')].find(b => b.textContent!.includes('Holidays'))!;
        expect(holidays.disabled).toBe(true);

        api.browse.mockResolvedValue(listing('', [item('Media', { kind: 'directory' }), item('Other', { kind: 'directory' })]));
        buttonNamed(element.querySelector('.transfer-crumbs')!, 'Root')!.click();
        await settle();
        api.browse.mockResolvedValue(listing('Other', []));
        [...element.querySelectorAll<HTMLButtonElement>('.transfer-folder')].find(b => b.textContent!.includes('Other'))!.click();
        await settle();
        expect(element.textContent).toContain('No folders in here.');
        buttonNamed(element, 'Move here')!.click();
        await settle();

        expect(api.checkMove).toHaveBeenCalledWith([photo.path, folder.path], 'Other');
        expect(api.startFileJob).toHaveBeenCalledWith({ kind: 'move', paths: [photo.path, folder.path], target: 'Other', keepAccess: false });
        expect(element.querySelector('dialog')!.open).toBe(false);
        expect(transfers.jobs().map(j => j.id)).toEqual([1]);
    });

    it('says who would gain access, and moves keeping it when asked', async () => {
        await setUp();
        const { element } = create();
        api.startFileJob.mockResolvedValue(job());
        api.fileJobs.mockResolvedValue([]);
        api.checkMove.mockResolvedValue({ gains: [{ folder: 'Media/Holidays', who: 'Visitors', isGroup: true, before: 'hidden', after: 'read' }] });

        await TestBed.inject(FileTransfers).transfer('move', [folder.path], '');
        await settle();

        expect(element.textContent).toContain('Move “Holidays” into “Root”?');
        expect(element.textContent).toContain('The group Visitors (who don\'t see it now) would see “Holidays”.');
        expect(api.startFileJob).not.toHaveBeenCalled();
        (element.querySelector('.transfer-gains + label input') as HTMLInputElement).click();
        buttonNamed(element, 'Move')!.click();
        await settle();
        expect(api.startFileJob).toHaveBeenCalledWith({ kind: 'move', paths: [folder.path], target: '', keepAccess: true });
    });

    it('refuses folders where the session may not copy, and asks about the tags of a copy', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.viewFiles, Rights.copyFiles]));
        const { element } = create();
        api.startFileJob.mockResolvedValue(job({ kind: 'copy' }));
        api.fileJobs.mockResolvedValue([]);
        api.browse.mockResolvedValue(listing('Media', [folder], { rights: [Rights.browse], pathOnly: false }));

        TestBed.inject(FileTransfers).pick('copy', [photo]);
        await settle();
        expect(element.textContent).toContain('You may not copy files into this folder');
        expect(buttonNamed(element, 'Copy here')!.disabled).toBe(true);

        expect(TestBed.inject(FileTransfers).keep('copy', [photo])).toBe(true);
        TestBed.inject(FileTransfers).paste('Other');
        await settle();
        expect(element.textContent).toContain('Copy “beach.jpg” into “Other”?');
        (element.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
        buttonNamed(element, 'Copy')!.click();
        await settle();
        expect(api.startFileJob).toHaveBeenCalledWith({ kind: 'copy', paths: [photo.path], target: 'Other', keepTags: false });
        expect(TestBed.inject(FileTransfers).clipboard(), 'A copy can be pasted again').not.toBeNull();
    });
});

describe('FileTransfers', () => {
    it('updates the listing and says what was done when a job ends', async () => {
        await setUp();
        const toasts = TestBed.inject(ToastService);
        const transfers = TestBed.inject(FileTransfers);
        api.startFileJob.mockResolvedValue(job({ total: 2 }));
        api.fileJobs.mockResolvedValue([job({
            state: 'completed', total: 2, done: 2, finished: '2026-10-17T10:00:01Z', results: [
                { path: photo.path, newPath: 'Media/Holidays/beach.jpg', status: 'done' },
                { path: clip.path, newPath: null, status: 'failed', message: '“clip.mp4” could not be moved: it is in use.' },
            ],
        })]);

        await transfers.start({ kind: 'move', paths: [photo.path, clip.path], target: 'Media/Holidays' });
        await settle();

        expect(store.items().map(i => i.name)).toEqual(['Holidays', 'clip.mp4']);
        const messages = toasts.toasts().map(t => t.message);
        expect(messages).toContain('1 item moved to “Holidays”.');
        expect(messages).toContain('“clip.mp4” could not be moved: it is in use.');
        const navigate = vi.spyOn(store, 'navigate').mockImplementation(() => undefined as never);
        toasts.act(toasts.toasts().find(t => t.action)!);
        expect(navigate).toHaveBeenCalledWith({ path: 'Media/Holidays' });
    });

    it('cuts once and pastes into the folder listed; keeps nothing it may not move', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.moveFiles]));
        const transfers = TestBed.inject(FileTransfers);
        vi.spyOn(transfers, 'transfer').mockResolvedValue();

        expect(transfers.keep('copy', [photo])).toBe(false);
        expect(transfers.keep('move', [photo, clip])).toBe(true);
        expect(transfers.paste('Media/Holidays')).toBe(true);

        expect(transfers.transfer).toHaveBeenCalledWith('move', [photo.path, clip.path], 'Media/Holidays');
        expect(transfers.clipboard()).toBeNull();
        expect(transfers.paste('Media')).toBe(false);
    });
});

describe('ItemMenu, moves and copies', () => {
    it('offers cut, copy, paste into a folder, move to and copy to by rights', async () => {
        await setUp();
        const menu = TestBed.inject(ItemMenu);
        expect(menu.entries(photo).map(e => e.id)).toEqual(['rename', 'delete', 'cut', 'copy', 'moveTo', 'copyTo', 'download', 'tags']);

        TestBed.inject(FileTransfers).keep('move', [photo]);
        const paste = menu.entries(folder).find(e => e.id === 'paste')!;
        expect(paste.label).toBe('Paste into Holidays');
        const transfer = vi.spyOn(TestBed.inject(FileTransfers), 'transfer').mockResolvedValue();
        paste.run();
        expect(transfer).toHaveBeenCalledWith('move', [photo.path], folder.path);

        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewFiles]));
        expect(menu.hasActions(photo)).toBe(false);
    });

    it('opens the picker for the whole selection', async () => {
        await setUp();
        const pick = vi.fn();
        TestBed.inject(Dialogs).transfer.set({ pick } as never);
        store.select(photo, true);
        store.select(clip, true);

        TestBed.inject(ItemMenu).entries(photo).find(e => e.id === 'copyTo')!.run();

        expect(pick).toHaveBeenCalledWith('copy', [photo, clip]);
    });
});

describe('OperationsPanelComponent', () => {
    async function panel() {
        const fixture = TestBed.createComponent(OperationsPanelComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        return { fixture, element: fixture.nativeElement as HTMLElement };
    }

    it('shows the progress, asks about a name clash and closes when done', async () => {
        await setUp();
        const waiting = job({
            kind: 'copy', state: 'waiting', total: 3, done: 1, bytesTotal: 4096, bytesDone: 1024,
            conflict: { source: { name: 'beach.jpg', isFolder: false, size: 2048, modified: null }, existing: { name: 'beach.jpg', isFolder: false, size: 1024, modified: null }, canReplace: false },
        });
        api.fileJobs.mockResolvedValue([waiting]);
        api.answerFileJob.mockResolvedValue(undefined);
        const { fixture, element } = await panel();

        expect(element.textContent).toContain('Copying 3 items to “Holidays”');
        expect(element.textContent).toContain('“beach.jpg” is already there.');
        expect(buttonNamed(element, 'Replace')).toBeUndefined();
        (element.querySelector('.operation-conflict input') as HTMLInputElement).click();
        api.fileJobs.mockResolvedValue([job({ ...waiting, conflict: null, state: 'completed', done: 3, results: [{ path: 'b', newPath: null, status: 'skipped', message: '“b” was left out.' }] })]);
        buttonNamed(element, 'Skip')!.click();
        // The answer, then the poll it schedules.
        await settle();
        await settle();
        fixture.detectChanges();

        expect(api.answerFileJob).toHaveBeenCalledWith(1, 'skip', true);
        expect(element.textContent).toContain('0 items copied to “Holidays”');
        expect(element.textContent).toContain('“b” was left out.');
        (element.querySelector('[aria-label="Close"]') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(element.querySelector('[data-job]')).toBeNull();
    });

    it('moves items dropped onto a folder, and copies them with Ctrl', async () => {
        await setUp();
        await panel();
        const transfer = vi.spyOn(TestBed.inject(FileTransfers), 'transfer').mockResolvedValue();
        const target = document.createElement('div');
        target.setAttribute('data-drop', folder.path);
        document.body.append(target);

        const drop = (ctrlKey: boolean, paths: string[]) => {
            const event = new Event('drop', { bubbles: true, cancelable: true });
            Object.assign(event, { ctrlKey, dataTransfer: { types: [DRAG_ITEMS], getData: (type: string) => type === DRAG_ITEMS ? JSON.stringify(paths) : '' } });
            target.dispatchEvent(event);
            return event;
        };
        expect(drop(false, [photo.path]).defaultPrevented).toBe(true);
        drop(true, [clip.path, folder.path]);
        target.remove();

        expect(transfer.mock.calls).toEqual([['move', [photo.path], folder.path], ['copy', [clip.path], folder.path]]);
    });
});
