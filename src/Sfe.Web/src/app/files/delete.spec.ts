import { TestBed } from '@angular/core/testing';
import { DirectoryListing, LibraryItem, TrashPage } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { DeleteDialog } from './delete.dialog';
import { FileActions } from './file-actions';
import { ItemMenu } from './item-menu';
import { TrashSectionComponent } from './trash-section.component';

let api: ApiMock;
let store: ListingStore;

const folder = item('Media/Holidays', { kind: 'directory' });
const photo = item('Media/beach.jpg', { size: 2048 });
const clip = item('Media/clip.mp4', { size: 1024 });

function listing(items: LibraryItem[]): DirectoryListing {
    const folders = items.filter(i => i.kind === 'directory').length;
    return { path: 'Media', name: 'Media', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: folders, fileCount: items.length - folders };
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
}

afterEach(() => vi.restoreAllMocks());

describe('DeleteDialog', () => {
    function create() {
        const fixture = TestBed.createComponent(DeleteDialog);
        fixture.detectChanges();
        return { dialog: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
    }

    it('asks, moves to the trash, takes the items out of the listing and offers to undo', async () => {
        await setUp();
        const { dialog, element } = create();
        const toasts = TestBed.inject(ToastService);
        api.deleteItems.mockResolvedValue({ permanent: false, items: [{ path: folder.path, status: 'done', trashId: 4 }, { path: photo.path, status: 'done', trashId: 5 }] });
        api.restoreFromTrash.mockImplementation(id => Promise.resolve(id === 4 ? folder : photo));
        dialog.open([folder, photo], false);
        await settle();

        expect(element.textContent).toContain('Move 1 folder and 1 file (2 KB, and what the folders hold) to the trash?');
        buttonNamed(element, 'Delete')!.click();
        await settle();

        expect(api.deleteItems).toHaveBeenCalledWith([folder.path, photo.path], false);
        expect(store.items()).toEqual([clip]);
        expect(store.listing()).toMatchObject({ total: 1, directoryCount: 0, fileCount: 1 });
        const toast = toasts.toasts().at(-1)!;
        expect(toast.message).toBe('2 items moved to the trash.');

        api.browse.mockResolvedValue(listing([folder, photo, clip]));
        toasts.act(toast);
        await settle();
        expect(api.restoreFromTrash.mock.calls.map(call => call[0])).toEqual([4, 5]);
        expect(store.items()).toHaveLength(3);
    });

    it('says a permanent delete can\'t be undone, and offers no undo', async () => {
        await setUp();
        const { dialog, element } = create();
        api.deleteItems.mockResolvedValue({ permanent: true, items: [{ path: clip.path, status: 'done' }] });
        dialog.open([clip], true);
        await settle();

        expect(element.textContent).toContain('can\'t be undone');
        buttonNamed(element, 'Delete permanently')!.click();
        await settle();

        expect(api.deleteItems).toHaveBeenCalledWith([clip.path], true);
        expect(TestBed.inject(ToastService).toasts().at(-1)!.action).toBeUndefined();
    });

    it('offers a permanent delete for what can\'t go to the trash, and says what failed', async () => {
        await setUp();
        const { dialog, element } = create();
        const toasts = TestBed.inject(ToastService);
        api.deleteItems.mockResolvedValueOnce({
            permanent: false,
            items: [{ path: folder.path, status: 'trashUnavailable' }, { path: photo.path, status: 'failed', message: '“beach.jpg” could not be deleted: it is in use.' }],
        });
        dialog.open([folder, photo], false);
        await settle();
        buttonNamed(element, 'Delete')!.click();
        await settle();

        expect(toasts.toasts().at(-1)!.message).toContain('it is in use');
        expect(element.querySelector('dialog')!.open).toBe(true);
        expect(element.textContent).toContain('can\'t go to the trash');

        api.deleteItems.mockResolvedValueOnce({ permanent: true, items: [{ path: folder.path, status: 'done' }] });
        buttonNamed(element, 'Delete permanently')!.click();
        await settle();

        expect(api.deleteItems).toHaveBeenLastCalledWith([folder.path], true);
        expect(store.items().map(i => i.path)).toEqual([photo.path, clip.path]);
        expect(element.querySelector('dialog')!.open).toBe(false);
    });
});

describe('FileActions', () => {
    it('deletes to the trash with the right, for good only with that one, and always for good when the space says so', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.deleteFiles]));
        const open = vi.fn();
        TestBed.inject(Dialogs).delete.set({ open } as never);
        const actions = TestBed.inject(FileActions);

        expect(actions.delete([photo], true)).toBe(true);
        expect(open).toHaveBeenLastCalledWith([photo], false);
        expect(TestBed.inject(ItemMenu).entries(photo).find(e => e.id === 'delete')!.label).toBe('Delete…');

        await loadConfig(api, limitedConfig([Rights.browse, Rights.deleteFiles], { files: { deletePermanently: true, trashRetentionDays: 30 } }));
        expect(actions.canDelete(photo)).toBe(false);
        expect(actions.delete([photo])).toBe(false);

        await loadConfig(api, limitedConfig([Rights.browse, Rights.deletePermanently], { files: { deletePermanently: true, trashRetentionDays: 30 } }));
        expect(actions.delete([photo])).toBe(true);
        expect(open).toHaveBeenLastCalledWith([photo], true);
    });

    it('acts on the whole selection from the menu of one of its items', async () => {
        await setUp();
        const open = vi.fn();
        TestBed.inject(Dialogs).delete.set({ open } as never);
        store.select(photo, true);
        store.select(clip, true);

        TestBed.inject(ItemMenu).entries(photo).find(e => e.id === 'delete')!.run();

        expect(open).toHaveBeenCalledWith([photo, clip], false);
    });
});

describe('ListingStore.applyRemoved', () => {
    it('takes deleted items out, and the next page starts where the server now has it', async () => {
        await setUp();
        api.browse.mockResolvedValueOnce({ ...listing([folder, photo]), total: 4, limit: 2 });
        await store.load();
        store.select(photo, true);

        store.applyRemoved([photo.path]);
        api.browse.mockResolvedValueOnce({ ...listing([clip, item('Media/z.txt')]), offset: 1, total: 3, limit: 2 });
        await store.loadMore();

        expect(store.selection().size).toBe(0);
        expect(api.browse.mock.lastCall![0]).toMatchObject({ offset: 1 });
        expect(store.items().map(i => i.name)).toEqual(['Holidays', 'clip.mp4', 'z.txt']);
    });
});

describe('TrashSectionComponent', () => {
    const page = (items: TrashPage['items'], next: number | null = null): TrashPage => ({ items, hasMore: next !== null, next, retentionDays: 30 });
    const entry = (id: number, path: string, isFolder = false) => ({
        id, path, name: path.split('/').pop()!, isFolder, size: 2048, files: isFolder ? 3 : null, deletedAt: '2026-10-17T10:00:00Z', deletedBy: 'Sam', deletedById: 2,
    });

    async function section() {
        const fixture = TestBed.createComponent(TrashSectionComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    it('lists the trash, restores and deletes for good', async () => {
        await setUp();
        api.trash.mockResolvedValueOnce(page([entry(9, 'Media/beach.jpg'), entry(8, 'Old', true)], 8));
        api.trash.mockResolvedValueOnce(page([entry(7, 'notes.txt')]));
        api.restoreFromTrash.mockResolvedValue(item('Media/beach (2).jpg'));
        api.purgeFromTrash.mockResolvedValue(undefined);
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const element = await section();

        expect(element.textContent).toContain('30 days after it was deleted');
        expect(element.querySelector('[data-id="8"]')!.textContent).toContain('3 files · 2 KB');
        expect(element.querySelector('[data-id="9"]')!.textContent).toContain('by Sam');
        buttonNamed(element, 'Load more')!.click();
        await settle();
        expect(element.querySelectorAll('[data-id]')).toHaveLength(3);

        buttonNamed(element.querySelector('[data-id="9"]')!, 'Restore')!.click();
        await settle();
        expect(api.restoreFromTrash).toHaveBeenCalledWith(9);
        expect(TestBed.inject(ToastService).toasts().at(-1)!.message).toBe('“beach.jpg” is back, as “Media/beach (2).jpg”.');
        expect(element.querySelector('[data-id="9"]')).toBeNull();

        buttonNamed(element.querySelector('[data-id="8"]')!, 'Delete')!.click();
        await settle();
        expect(api.purgeFromTrash).toHaveBeenCalledWith(8);
        expect(element.querySelector('[data-id="8"]')).toBeNull();
    });

    it('offers neither delete nor empty without the right to delete for good', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.deleteFiles]));
        api.trash.mockResolvedValue(page([entry(1, 'a.txt')]));
        const element = await section();

        expect(buttonNamed(element, 'Restore')).toBeDefined();
        expect(buttonNamed(element, 'Delete')).toBeUndefined();
        expect(buttonNamed(element, 'Empty the trash')).toBeUndefined();
    });
});
