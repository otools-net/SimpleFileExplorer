import { TestBed } from '@angular/core/testing';
import { TAB_ID } from '../core/api.service';
import { DirectoryListing, FileJobView, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders, type } from '../../testing/fakes';
import { ChangeFeed } from '../library/change-feed.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import type { ViewerComponent } from '../library/viewer/viewer.component';
import { BatchRenameDialog, BatchRenameRule, planRenames } from './batch-rename.dialog';
import { FileTransfers } from './file-transfers';
import { ContextMenu, fromTouch } from '../menus/context-menu';
import { ItemMenu } from './item-menu';
import { NameDialog } from './name.dialog';

let api: ApiMock;
let store: ListingStore;

const a = item('Media/IMG_001.jpg');
const b = item('Media/IMG_002.jpg');
const notes = item('Media/notes.txt');
const holidays = item('Media/Holidays', { kind: 'directory' });

function listing(items: LibraryItem[], overrides: Partial<DirectoryListing> = {}): DirectoryListing {
    return { path: 'Media', name: 'Media', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: 0, fileCount: items.length, ...overrides };
}

async function setUp(items: LibraryItem[] = [holidays, a, b, notes], config = fakeConfig()): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing(items));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

const rule = (overrides: Partial<BatchRenameRule>): BatchRenameRule =>
    ({ mode: 'replace', find: '', replace: '', matchCase: false, pattern: '', start: 1, ...overrides });

afterEach(() => vi.restoreAllMocks());

describe('planRenames', () => {
    it('finds and replaces in the names, keeping extensions', () => {
        const plans = planRenames([a, b], rule({ find: 'img_', replace: 'Beach ' }), []);

        expect(plans.map(p => p.name)).toEqual(['Beach 001.jpg', 'Beach 002.jpg']);
        expect(plans.every(p => p.problem === null)).toBe(true);
        expect(planRenames([a], rule({ find: 'img_', replace: 'x', matchCase: true }), [])[0].name).toBe('IMG_001.jpg');
    });

    it('numbers names with as many digits as #', () => {
        expect(planRenames([a, b, holidays], rule({ mode: 'number', pattern: 'Trip ##', start: 9 }), []).map(p => p.name))
            .toEqual(['Trip 09.jpg', 'Trip 10.jpg', 'Trip 11']);
        expect(planRenames([a], rule({ mode: 'number', pattern: 'Trip', start: 1 }), [])[0].name).toBe('Trip 1.jpg');
    });

    it('says what keeps a name from being given', () => {
        expect(planRenames([a, b], rule({ find: 'IMG_00', replace: '' }), []).map(p => p.problem)).toEqual([null, null]);
        expect(planRenames([a, b], rule({ find: 'IMG_00', replace: 'x' }), ['x1.jpg'])[0].problem).toBe('Already taken in this folder');
        expect(planRenames([a, b], rule({ find: 'IMG_001', replace: '' }), [])[0].problem).toBe('No name left');
        expect(planRenames([a, b], rule({ find: 'IMG_00', replace: 'a/' }), [])[0].problem).toContain('/');
        const alike = planRenames([item('Media/x.jpg'), item('Media/X.jpg')], rule({ find: 'x', replace: 'y' }), []);
        expect(alike.map(p => p.problem)).toEqual(['Two items would have this name', 'Two items would have this name']);
        expect(planRenames([a, b], rule({ mode: 'number', pattern: 'IMG_00#', start: 2 }), [])[0].problem).toContain('two steps');
    });
});

describe('BatchRenameDialog', () => {
    it('renames the items one after the other, then undoes all of it', async () => {
        await setUp();
        const fixture = TestBed.createComponent(BatchRenameDialog);
        await fixture.whenStable();
        api.renameItem.mockImplementation(async (path: string, name: string) => item(`Media/${name}`));
        fixture.componentInstance.open([a, b]);
        fixture.detectChanges();
        const element = fixture.nativeElement as HTMLElement;

        type(element.querySelector<HTMLInputElement>('input[name="find"]')!, 'IMG_');
        element.querySelector<HTMLInputElement>('input[name="replace"]')!.value = 'Beach ';
        element.querySelector<HTMLInputElement>('input[name="replace"]')!.dispatchEvent(new Event('input'));
        fixture.detectChanges();
        expect(element.querySelector('.batch-rename-preview tbody')!.textContent).toContain('Beach 001.jpg');
        buttonNamed(element, 'Rename 2')!.click();
        await settle();

        expect(api.renameItem.mock.calls).toEqual([['Media/IMG_001.jpg', 'Beach 001.jpg'], ['Media/IMG_002.jpg', 'Beach 002.jpg']]);
        expect(store.items().map(i => i.name)).toEqual(['Holidays', 'Beach 001.jpg', 'Beach 002.jpg', 'notes.txt']);

        const toasts = TestBed.inject(ToastService);
        toasts.act(toasts.toasts().find(t => t.message === '2 items renamed.')!);
        await settle();
        expect(api.renameItem.mock.calls.slice(2)).toEqual([['Media/Beach 002.jpg', 'IMG_002.jpg'], ['Media/Beach 001.jpg', 'IMG_001.jpg']]);
        expect(store.items().map(i => i.name)).toEqual(['Holidays', 'IMG_001.jpg', 'IMG_002.jpg', 'notes.txt']);
    });

    it('opens from the menu and F2 only for several items that may be renamed', async () => {
        await setUp();
        const menu = TestBed.inject(ItemMenu);
        const open = vi.fn();
        TestBed.inject(Dialogs).batchRename.set({ open } as never);

        expect(menu.renameSelection()).toBe(false);
        store.select(a, true);
        store.select(b, true);
        expect(menu.renameSelection()).toBe(true);
        expect(open).toHaveBeenCalledWith([a, b]);
        menu.entries(a).find(e => e.id === 'rename')!.run();
        expect(open).toHaveBeenCalledTimes(2);
    });
});

describe('Undo of a rename and a move', () => {
    it('gives a renamed item its name back', async () => {
        await setUp();
        const fixture = TestBed.createComponent(NameDialog);
        await fixture.whenStable();
        api.renameItem.mockResolvedValueOnce(item('Media/Sunset.jpg')).mockResolvedValueOnce(a);
        fixture.componentInstance.rename(a);
        const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input')!;
        type(input, 'Sunset.jpg');
        (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
        await settle();
        expect(store.items().map(i => i.name)).toContain('Sunset.jpg');

        const toasts = TestBed.inject(ToastService);
        toasts.act(toasts.toasts().find(t => t.message === 'Renamed to “Sunset.jpg”.')!);
        await settle();

        expect(api.renameItem).toHaveBeenLastCalledWith('Media/Sunset.jpg', 'IMG_001.jpg');
        expect(store.items().map(i => i.name)).toContain('IMG_001.jpg');
    });

    it('moves moved items back into the folders they came from', async () => {
        await setUp();
        const transfers = TestBed.inject(FileTransfers);
        const job: FileJobView = {
            id: 4, kind: 'move', state: 'completed', target: 'Media/Holidays', total: 2, done: 2, bytesTotal: 0, bytesDone: 0, current: null, conflict: null,
            results: [
                { path: 'Media/IMG_001.jpg', newPath: 'Media/Holidays/IMG_001.jpg', status: 'done' },
                { path: 'Media/IMG_002.jpg', newPath: 'Media/Holidays/IMG_002.jpg', status: 'done' },
            ],
            started: '2026-10-17T10:00:00Z', finished: '2026-10-17T10:00:01Z',
        };
        api.startFileJob.mockResolvedValueOnce({ ...job, state: 'running', results: [] });
        api.fileJobs.mockResolvedValueOnce([job]);
        api.checkMove.mockResolvedValue({ gains: [] });
        await transfers.start({ kind: 'move', paths: [a.path, b.path], target: 'Media/Holidays' });
        await settle();
        api.startFileJob.mockResolvedValue({ ...job, id: 5, state: 'running', results: [] });
        api.fileJobs.mockResolvedValue([]);

        const toasts = TestBed.inject(ToastService);
        const moved = toasts.toasts().find(t => t.message === '2 items moved to “Holidays”.')!;
        toasts.act(moved, moved.actions.find(action => action.label === 'Undo'));
        await settle();

        expect(api.startFileJob).toHaveBeenLastCalledWith({ kind: 'move', paths: ['Media/Holidays/IMG_001.jpg', 'Media/Holidays/IMG_002.jpg'], target: 'Media' });
    });
});

describe('New file', () => {
    it('creates an empty file, listed at once', async () => {
        await setUp();
        const fixture = TestBed.createComponent(NameDialog);
        await fixture.whenStable();
        api.createFile.mockResolvedValue(item('Media/todo.txt', { size: 0 }));
        fixture.componentInstance.newFile();
        const element = fixture.nativeElement as HTMLElement;
        type(element.querySelector<HTMLInputElement>('input')!, 'todo.txt');
        element.querySelector('form')!.dispatchEvent(new Event('submit'));
        await settle();

        expect(api.createFile).toHaveBeenCalledWith('Media', 'todo.txt');
        expect(store.items().map(i => i.name)).toContain('todo.txt');
    });

    it('opens a new text file in the viewer, ready to be written; not a picture', async () => {
        await setUp();
        const fixture = TestBed.createComponent(NameDialog);
        await fixture.whenStable();
        const open = vi.fn();
        TestBed.inject(Dialogs).viewer.set({ open } as unknown as ViewerComponent);
        const element = fixture.nativeElement as HTMLElement;
        const create = async (name: string, created: LibraryItem) => {
            api.createFile.mockResolvedValue(created);
            fixture.componentInstance.newFile();
            type(element.querySelector<HTMLInputElement>('input')!, name);
            element.querySelector('form')!.dispatchEvent(new Event('submit'));
            await settle();
        };

        await create('todo.txt', item('Media/todo.txt', { kind: 'file', size: 0 }));
        expect(open).toHaveBeenCalledWith(expect.objectContaining({ path: 'Media/todo.txt' }), { edit: true });

        await create('empty.jpg', item('Media/empty.jpg', { kind: 'image', size: 0 }));
        expect(open).toHaveBeenCalledTimes(1);
        TestBed.inject(Dialogs).viewer.set(null);
    });
});

describe('ChangeFeed', () => {
    it('refreshes the folder listed when another tab or person changed it, in place', async () => {
        await setUp();
        const feed = TestBed.inject(ChangeFeed);
        store.select(a, true);
        store.select(b, true);
        api.browse.mockResolvedValue(listing([holidays, a, notes, item('Media/new.jpg')]));

        feed.changed({ path: 'Media', origin: TAB_ID });
        feed.changed({ path: 'Elsewhere', origin: null });
        await settle(450);
        expect(api.browse).toHaveBeenCalledTimes(1);

        feed.changed({ path: 'Media', origin: 'another tab' });
        feed.changed({ path: 'Media', origin: null });
        await settle(450);

        expect(api.browse).toHaveBeenCalledTimes(2);
        expect(store.items().map(i => i.name)).toEqual(['Holidays', 'IMG_001.jpg', 'notes.txt', 'new.jpg']);
        expect([...store.selection()]).toEqual([a.path]);
        expect(store.status()).toBe('ready');
    });

    describe('connection', () => {
        /** A stand-in for the browser's EventSource: the test opens, drops and closes it. */
        class FakeSource extends EventTarget {
            static readonly CONNECTING = 0;
            static readonly OPEN = 1;
            static readonly CLOSED = 2;
            static all: FakeSource[] = [];
            readyState = FakeSource.CONNECTING;
            constructor(readonly url: string) {
                super();
                FakeSource.all.push(this);
            }
            close(): void {
                this.readyState = FakeSource.CLOSED;
            }
            emit(type: 'open' | 'error' | 'ping' | 'trash', state = this.readyState): void {
                this.readyState = state;
                this.dispatchEvent(new Event(type));
            }
        }
        let feed: ChangeFeed;
        const last = () => FakeSource.all[FakeSource.all.length - 1];

        beforeEach(async () => {
            FakeSource.all = [];
            vi.stubGlobal('EventSource', FakeSource);
            await setUp();
            vi.useFakeTimers();
            feed = TestBed.inject(ChangeFeed);
            feed.start();
            last().emit('open', FakeSource.OPEN);
        });

        afterEach(() => {
            feed.stop();
            vi.useRealTimers();
            vi.unstubAllGlobals();
        });

        it('tells the server lost once a drop lasts, and reads the folder again once it is back', () => {
            // The feed ends every 10 minutes and comes back after 5 s: not shown.
            last().emit('error', FakeSource.CONNECTING);
            vi.advanceTimersByTime(5_000);
            last().emit('open', FakeSource.OPEN);
            vi.advanceTimersByTime(20_000);
            expect(feed.serverLost()).toBe(false);

            last().emit('error', FakeSource.CONNECTING);
            vi.advanceTimersByTime(10_000);
            expect(feed.serverLost()).toBe(true);

            const browses = api.browse.mock.calls.length;
            last().emit('open', FakeSource.OPEN);
            expect(feed.serverLost()).toBe(false);
            expect(api.browse.mock.calls.length).toBe(browses + 1);
        });

        it('gives the connection up while the page is kept for Back, and opens it again when it shows', () => {
            window.dispatchEvent(new Event('pagehide'));
            expect(FakeSource.all[0].readyState).toBe(FakeSource.CLOSED);
            vi.advanceTimersByTime(120_000);
            expect(feed.serverLost()).toBe(false);
            expect(FakeSource.all).toHaveLength(1);

            window.dispatchEvent(new Event('pageshow'));
            expect(FakeSource.all).toHaveLength(2);
            // Shown again without having been left: nothing more.
            window.dispatchEvent(new Event('pageshow'));
            expect(FakeSource.all).toHaveLength(2);
        });

        it('opens the feed again itself when the browser gives up (an error status)', () => {
            last().emit('error', FakeSource.CLOSED);
            vi.advanceTimersByTime(5_000);
            expect(FakeSource.all).toHaveLength(2);
            vi.advanceTimersByTime(5_000);
            expect(feed.serverLost()).toBe(true);

            last().emit('open', FakeSource.OPEN);
            expect(feed.serverLost()).toBe(false);
        });

        it('takes a minute without a word (pings come every 25 s) for a lost server, and opens the feed again', () => {
            for (let i = 0; i < 4; i++) {
                vi.advanceTimersByTime(25_000);
                last().emit('ping');
            }
            expect(feed.serverLost()).toBe(false);
            expect(FakeSource.all).toHaveLength(1);

            vi.advanceTimersByTime(60_000);
            expect(feed.serverLost()).toBe(true);
            expect(FakeSource.all).toHaveLength(2);
            expect(FakeSource.all[0].readyState).toBe(FakeSource.CLOSED);
            last().emit('open', FakeSource.OPEN);
            expect(feed.serverLost()).toBe(false);
        });

        it('counts the trash events, which also tell the server answers', () => {
            vi.advanceTimersByTime(50_000);
            last().emit('trash');
            last().emit('trash');
            vi.advanceTimersByTime(50_000);

            expect(feed.trashChanges()).toBe(2);
            expect(feed.serverLost()).toBe(false);
        });
    });
});

describe('Phones', () => {
    it('selects on a long press instead of opening the menu', async () => {
        await setUp(undefined, limitedConfig([Rights.browse]));
        const menu = TestBed.inject(ItemMenu);
        const press = Object.assign(new MouseEvent('contextmenu', { cancelable: true }), { pointerType: 'touch' });

        TestBed.inject(ContextMenu).fromEvent(press, menu.target(a));

        expect(fromTouch(press)).toBe(true);
        expect(press.defaultPrevented).toBe(true);
        expect(store.isSelected(a.path)).toBe(true);
        expect(TestBed.inject(ContextMenu).open()).toBeNull();
    });
});
