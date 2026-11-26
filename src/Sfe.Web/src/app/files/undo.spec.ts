import { TestBed } from '@angular/core/testing';
import { App } from '../app';
import { ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ConnectionPlace } from '../core/connection.models';
import { DirectoryListing, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { Toast, ToastService } from '../core/toast.service';
import { UNDO_LIMIT, UndoEntry, UndoHistory } from '../core/undo-history';
import {
    ApiMock,
    fakeConfig,
    item,
    limitedConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
    type,
} from '../../testing/fakes';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { MainMenuComponent } from '../library/topbar/main-menu.component';
import { FileJobView } from './file.models';
import { FileTransfers } from './file-transfers';
import { NameDialog } from './name.dialog';

let api: ApiMock;
let store: ListingStore;
let undos: UndoHistory;
let toasts: ToastService;

const a = item('Media/IMG_001.jpg', { modified: '2026-11-26T10:00:00Z' });
const plans = item('Media/Plans', { kind: 'directory', modified: '2026-11-26T10:00:00Z' });

function listing(items: LibraryItem[]): DirectoryListing {
    return {
        path: 'Media',
        name: 'Media',
        parentPath: '',
        items,
        offset: 0,
        limit: 100,
        total: items.length,
        directoryCount: 0,
        fileCount: items.length,
    };
}

async function setUp(config = fakeConfig()): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    undos = TestBed.inject(UndoHistory);
    toasts = TestBed.inject(ToastService);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing([a]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

function entry(done: string, run = vi.fn()): UndoEntry {
    return { done, hint: `Undo ${done}`, run };
}

const messages = () => toasts.toasts().map((toast) => toast.message);
const toastSaying = (message: string): Toast => toasts.toasts().find((toast) => toast.message === message)!;

function pressCtrlZ(target: Element = document.body, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
}

async function newFolder(created: LibraryItem): Promise<void> {
    const fixture = TestBed.createComponent(NameDialog);
    await fixture.whenStable();
    api.createFolder.mockResolvedValue(created);
    fixture.componentInstance.newFolder();
    const element = fixture.nativeElement as HTMLElement;
    type(element.querySelector<HTMLInputElement>('input')!, created.name);
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
}

function copyJob(overrides: Partial<FileJobView> = {}): FileJobView {
    return {
        id: 7,
        kind: 'copy',
        state: 'completed',
        target: 'Media/Plans',
        total: 1,
        done: 1,
        bytesTotal: 10,
        bytesDone: 10,
        current: null,
        conflict: null,
        results: [{ path: 'Media/IMG_001.jpg', newPath: 'Media/Plans/IMG_001.jpg', status: 'done' }],
        started: '2026-11-26T10:00:00Z',
        finished: '2026-11-26T10:00:01Z',
        ...overrides,
    };
}

describe('UndoHistory', () => {
    beforeEach(() => setUp());

    it('keeps the tab’s latest actions, the oldest forgotten past the limit', () => {
        for (let i = 1; i <= UNDO_LIMIT + 3; i++) undos.add(entry(`Action ${i}.`));

        expect(undos.latest()!.done).toBe(`Action ${UNDO_LIMIT + 3}.`);
        for (let i = 0; i < UNDO_LIMIT; i++) {
            const latest = undos.latest()!;
            undos.undoLatest();
            expect(undos.latest()).not.toBe(latest);
        }
        expect(undos.latest()).toBeNull();
        expect(undos.undoLatest()).toBe(false);
    });

    it('undoes an action once, from its toast or from Ctrl+Z', async () => {
        const run = vi.fn();
        const action = undos.add(entry('Renamed to “b.jpg”.', run));
        expect(action).toEqual(expect.objectContaining({ label: 'Undo', title: 'Undo Renamed to “b.jpg”.' }));

        action.run!();
        expect(undos.undoLatest()).toBe(false);
        action.run!();
        await settle();

        expect(run).toHaveBeenCalledTimes(1);
    });

    it('says what Ctrl+Z undoes, and undoes one action after the other, latest first', async () => {
        const order: string[] = [];
        let finishFirst!: () => void;
        undos.add(
            entry(
                'First.',
                vi.fn(() => void order.push('first')),
            ),
        );
        undos.add(
            entry(
                'Second.',
                vi.fn(
                    () =>
                        new Promise<void>((resolve) => {
                            order.push('second');
                            finishFirst = resolve;
                        }),
                ),
            ),
        );

        undos.undoLatest();
        undos.undoLatest();
        await settle();
        expect(order).toEqual(['second']);
        finishFirst();
        await settle();

        expect(order).toEqual(['second', 'first']);
        expect(messages()).toEqual(['Undoing: Second.', 'Undoing: First.']);
    });

    it('says why an undo failed', async () => {
        undos.add(entry('Moved.', vi.fn().mockRejectedValue(new Error('The server is not reachable.'))));

        undos.undoLatest();
        await settle();

        expect(messages()).toContain('Could not undo it: The server is not reachable.');
    });

    it('undoes an action on a place’s files only while that place is listed, keeping it meanwhile', async () => {
        const run = vi.fn();
        undos.add({ ...entry('Renamed to “b.jpg”.', run), place: undos.here() });
        const config = TestBed.inject(ConfigService);
        config.place.set({ id: 3, name: 'Dropbox' } as ConnectionPlace);

        expect(undos.blocked()).toBe('It was done in the library: open the library to undo it.');
        expect(undos.undoLatest()).toBe(true);
        await settle();
        expect(run).not.toHaveBeenCalled();
        expect(messages()).toEqual(['It was done in the library: open the library to undo it.']);

        config.place.set(null);
        undos.undoLatest();
        await settle();
        expect(run).toHaveBeenCalledTimes(1);
    });
});

describe('Ctrl+Z', () => {
    beforeEach(async () => {
        await setUp();
        TestBed.createComponent(App);
        await settle();
    });

    it('undoes the latest action, anywhere in the page', async () => {
        const run = vi.fn();
        undos.add(entry('Moved.', run));

        const event = pressCtrlZ();
        await settle();

        expect(event.defaultPrevented).toBe(true);
        expect(run).toHaveBeenCalledTimes(1);
    });

    it('is left to text fields, dialogs and other keys; does nothing with nothing to undo', async () => {
        const run = vi.fn();
        undos.add(entry('Moved.', run));
        const input = document.body.appendChild(document.createElement('input'));
        const dialog = document.body.appendChild(document.createElement('dialog'));
        const inDialog = dialog.appendChild(document.createElement('button'));

        pressCtrlZ(input);
        pressCtrlZ(inDialog);
        pressCtrlZ(document.body, { shiftKey: true });
        pressCtrlZ(document.body, { key: 'y' });
        await settle();
        expect(run).not.toHaveBeenCalled();

        const meta = pressCtrlZ(document.body, { ctrlKey: false, metaKey: true });
        await settle();
        expect(meta.defaultPrevented).toBe(true);
        expect(run).toHaveBeenCalledTimes(1);
        expect(pressCtrlZ().defaultPrevented).toBe(false);
        input.remove();
        dialog.remove();
    });
});

describe('Undo of a new folder', () => {
    it('moves the folder made to the trash', async () => {
        await setUp();
        await newFolder(plans);
        expect(undos.latest()!.done).toBe('Folder “Plans” created.');
        api.properties.mockResolvedValue({ item: plans, details: {} as never, canChecksum: false });
        api.deleteItems.mockResolvedValue({ permanent: false, items: [{ path: plans.path, status: 'done' }] });

        undos.undoLatest();
        await settle();

        expect(api.deleteItems).toHaveBeenCalledWith(['Media/Plans'], false);
        expect(store.items().map((i) => i.name)).not.toContain('Plans');
        expect(messages()).toContain('1 item moved to the trash.');
    });

    it('leaves it when something changed in it since, or it is gone', async () => {
        await setUp();
        await newFolder(plans);
        const changed = { ...plans, modified: '2026-11-26T10:05:00Z' };
        api.properties.mockResolvedValueOnce({ item: changed, details: {} as never, canChecksum: false });

        undos.undoLatest();
        await settle();
        expect(api.deleteItems).not.toHaveBeenCalled();
        expect(messages()).toContain('Could not move “Plans” to the trash: it changed since it was made.');

        await newFolder(plans);
        api.properties.mockRejectedValueOnce(new ApiError(404, 'Not found.'));
        undos.undoLatest();
        await settle();
        expect(api.deleteItems).not.toHaveBeenCalled();
        expect(messages()).toContain("Could not move “Plans” to the trash: it isn't there any more.");
    });

    it('is not offered without the right to delete, nor where deletes are for good', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.viewFiles, Rights.createFolders]));
        await newFolder(plans);
        expect(undos.latest()).toBeNull();

        TestBed.resetTestingModule();
        await setUp(fakeConfig({ files: { deletePermanently: true, trashRetentionDays: 30 } }));
        await newFolder(plans);
        expect(undos.latest()).toBeNull();
    });
});

describe('Undo of a copy', () => {
    async function copied(job: FileJobView, start = true): Promise<void> {
        const transfers = TestBed.inject(FileTransfers);
        api.fileJobs.mockResolvedValueOnce([job]).mockResolvedValue([]);
        if (start) {
            api.startFileJob.mockResolvedValueOnce({ ...job, state: 'running', results: [] });
            await transfers.start({ kind: 'copy', paths: [a.path], target: 'Media/Plans' });
        } else {
            api.fileJobs.mockReset();
            api.fileJobs.mockResolvedValueOnce([{ ...job, state: 'running' }]).mockResolvedValueOnce([job]);
            api.fileJobs.mockResolvedValue([]);
            transfers.resume();
            await settle(800);
        }
        await settle(800);
    }

    it('moves the copies to the trash, unless they changed since', async () => {
        await setUp();
        await copied(copyJob());
        const toast = toastSaying('1 item copied to “Plans”.');
        expect(toast.actions.map((action) => action.label)).toEqual(['Undo', 'Open']);
        api.properties.mockResolvedValue({
            item: item('Media/Plans/IMG_001.jpg', { modified: '2026-11-26T10:00:00Z' }),
            details: {} as never,
            canChecksum: false,
        });
        api.deleteItems.mockResolvedValue({
            permanent: false,
            items: [{ path: 'Media/Plans/IMG_001.jpg', status: 'done' }],
        });

        toasts.act(toast, toast.actions[0]);
        await settle();

        expect(api.properties).toHaveBeenCalledWith('Media/Plans/IMG_001.jpg');
        expect(api.deleteItems).toHaveBeenCalledWith(['Media/Plans/IMG_001.jpg'], false);
    });

    it('is not offered for an operation another tab started', async () => {
        await setUp();
        await copied(copyJob(), false);

        expect(toastSaying('1 item copied to “Plans”.').actions.map((action) => action.label)).toEqual(['Open']);
        expect(undos.latest()).toBeNull();
    });
});

describe('Undo in the ⋮ menu', () => {
    it('shows what it would undo, and undoes it', async () => {
        await setUp();
        const fixture = TestBed.createComponent(MainMenuComponent);
        const element = fixture.nativeElement as HTMLElement;
        await settle();
        expect(element.querySelector('.menu-undo')).toBeNull();

        const run = vi.fn();
        undos.add({ done: 'Renamed to “b.jpg”.', hint: 'Give it back its name “a.jpg”.', run });
        await settle();
        const button = element.querySelector<HTMLButtonElement>('.menu-undo')!;
        expect(button.querySelector('small')!.textContent).toBe('Renamed to “b.jpg”.');
        expect(button.title).toBe('Give it back its name “a.jpg”. Shortcut: Ctrl+Z.');

        button.click();
        await settle();
        expect(run).toHaveBeenCalledTimes(1);
        expect(element.querySelector('.menu-undo')).toBeNull();
    });

    it('is greyed with the reason while another place is listed', async () => {
        await setUp();
        const fixture = TestBed.createComponent(MainMenuComponent);
        const element = fixture.nativeElement as HTMLElement;
        undos.add({ ...entry('Renamed to “b.jpg”.'), place: { id: 3, name: 'Dropbox' } });
        await settle();

        const button = element.querySelector<HTMLButtonElement>('.menu-undo')!;
        expect(button.disabled).toBe(true);
        expect(button.querySelector('small')!.textContent).toBe('It was done in “Dropbox”: open it to undo it.');
    });
});
