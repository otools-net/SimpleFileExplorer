import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { entryOf, greyedIds, usableIds } from '../../testing/menus';
import { DirectoryListing, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { fakeConfig, item, limitedConfig, loadConfig, mockApi, testProviders } from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { ItemCommands } from './item-commands';
import { ItemMenu } from './item-menu';

const folder = item('Media/Holidays', { kind: 'directory' });
const zip = item('Media/trip.zip', { size: 4096 });
const photo = item('Media/beach.jpg', { size: 2048 });
const archives = { archives: { maxCompressedTarMegabytes: 2048 } };

function listing(items: LibraryItem[]): DirectoryListing {
    return {
        path: 'Media',
        name: 'Media',
        parentPath: '',
        items,
        offset: 0,
        limit: 100,
        total: items.length,
        directoryCount: 1,
        fileCount: items.length - 1,
    };
}

async function setUp(config = fakeConfig(archives)): Promise<ItemMenu> {
    const api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    const store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing([folder, zip, photo]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
    const dialogs = TestBed.inject(Dialogs);
    dialogs.extract.set({ open: vi.fn() } as never);
    dialogs.compress.set({ open: vi.fn() } as never);
    return TestBed.inject(ItemMenu);
}

afterEach(() => vi.restoreAllMocks());

describe('ItemCommands', () => {
    it('lays the menu out in groups, as a desktop file browser: open, clipboard, organise, save, delete', async () => {
        const menu = await setUp();
        const groups = [...new Set(menu.entries(photo).map((entry) => entry.group))];
        expect(groups).toEqual(['open', 'clipboard', 'organise', 'archive', 'save', 'delete', 'properties']);
        expect(menu.entries(photo).map((entry) => entry.id)).toEqual([
            'newTab',
            'cut',
            'copy',
            'rename',
            'moveTo',
            'copyTo',
            'tags',
            'quickLink',
            'compress',
            'download',
            'delete',
            'properties',
        ]);
    });

    it('gathers the extractions and Compress… under Archive, flat when only one applies', async () => {
        const menu = await setUp();
        const archive = menu.entries(zip).find((entry) => entry.id === 'archive')!;
        expect(archive.label).toBe('Archive');
        expect(archive.submenu!().map((entry) => entry.id)).toEqual(['extractBeside', 'extract', 'compress']);
        // A picture can only be compressed: the entry stands alone, without a submenu.
        expect(entryOf(menu.entries(photo), 'compress').label).toBe('Compress…');
        expect(menu.entries(photo).some((entry) => entry.id === 'archive')).toBe(false);
    });

    it('greys out what the rights forbid, saying why, and hides what does not apply', async () => {
        const menu = await setUp(limitedConfig([Rights.browse, Rights.viewFiles], archives));
        const entries = menu.entries(photo);

        expect(usableIds(entries)).toEqual(['newTab', 'properties']);
        expect(greyedIds(entries)).toEqual([
            'cut',
            'copy',
            'rename',
            'moveTo',
            'copyTo',
            'tags',
            'compress',
            'download',
            'delete',
        ]);
        expect(entryOf(entries, 'delete').title).toBe('You may not delete “beach.jpg”.');
        expect(entryOf(entries, 'cut').title).toBe('You may not move “beach.jpg”.');
        // Extracting is for archives: not even greyed out on a picture.
        expect(entries.some((entry) => entry.id === 'archive' || entry.id === 'extract')).toBe(false);
    });

    it('acts on the whole selection, and gives the selection bar the same entries', async () => {
        await setUp();
        const store = TestBed.inject(ListingStore);
        store.select(photo, true);
        store.select(zip, true);
        const commands = TestBed.inject(ItemCommands);
        const scope = { item: photo, targets: store.selectedItems() };

        expect(commands.entry('open', scope)).toBeNull();
        expect(commands.entry('rename', scope)!.title).toBe(
            'Rename the selected items together: find and replace, or numbered names (F2).',
        );
        expect(commands.entry('quickLink', scope)!.label).toBe('Add to quick links');
        expect(commands.entry('download', scope)!.label).toBe('Download as zip');
        expect(commands.entry('extractEach', scope)).toBeNull();
    });

    it("opens a library folder's rules in the administration, for those who manage them", async () => {
        const menu = await setUp();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const entry = entryOf(menu.entries(folder), 'folderAccess');
        expect(entry).toMatchObject({ label: 'Folder access…', group: 'organise' });
        expect(menu.entries(photo).some((candidate) => candidate.id === 'folderAccess')).toBe(false);

        entry.run!();
        expect(navigate).toHaveBeenCalledWith(['/admin'], {
            queryParams: { section: 'access', page: 'folders', folder: 'Media/Holidays' },
            state: { sfeFromLibrary: true },
        });

        TestBed.inject(ConfigService).place.set({ id: 3 } as never);
        expect(menu.entries(folder).some((candidate) => candidate.id === 'folderAccess')).toBe(false);
    });

    it('leaves Folder access… out without the right to manage the space', async () => {
        const menu = await setUp(limitedConfig([Rights.browse, Rights.viewFiles], archives));
        expect(menu.entries(folder).some((entry) => entry.id === 'folderAccess')).toBe(false);
    });

    it("opens a library file's previous versions, next to its properties", async () => {
        const menu = await setUp();
        const open = vi.fn();
        TestBed.inject(Dialogs).versions.set({ open } as never);
        const ids = menu.entries(photo).map((entry) => entry.id);
        expect(ids.slice(-2)).toEqual(['versions', 'properties']);
        const entry = entryOf(menu.entries(photo), 'versions');
        expect(entry).toMatchObject({ label: 'Previous versions…', group: 'properties' });
        expect(menu.entries(folder).some((candidate) => candidate.id === 'versions')).toBe(false);

        entry.run!();
        expect(open).toHaveBeenCalledWith(photo);

        TestBed.inject(ConfigService).place.set({ id: 3 } as never);
        expect(menu.entries(photo).some((candidate) => candidate.id === 'versions')).toBe(false);
    });

    it('leaves Previous versions… out for those who may not open files', async () => {
        const menu = await setUp(limitedConfig([Rights.browse], archives));
        TestBed.inject(Dialogs).versions.set({ open: vi.fn() } as never);
        expect(menu.entries(photo).some((entry) => entry.id === 'versions')).toBe(false);
    });
});
