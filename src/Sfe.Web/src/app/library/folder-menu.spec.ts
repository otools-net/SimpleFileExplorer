import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { DirectoryListing, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ApiMock, fakeConfig, item, limitedConfig, loadConfig, mockApi, testProviders } from '../../testing/fakes';
import { FileTransfers } from '../files/file-transfers';
import { UploadQueue } from '../files/uploads';
import { ContextMenu } from '../menus/context-menu';
import { Dialogs } from './dialogs.service';
import { FolderMenu } from './folder-menu';
import { ListingStore } from './listing.store';
import { SidebarService } from './sidebar/sidebar.service';

let api: ApiMock;
let store: ListingStore;
let menu: FolderMenu;

const folder = item('Media/Holidays', { kind: 'directory' });
const photo = item('Media/beach.jpg');

function listing(items: LibraryItem[]): DirectoryListing {
    return { path: 'Media', name: 'Media', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: 1, fileCount: items.length - 1 };
}

async function setUp(rights?: string[], q = ''): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, rights ? limitedConfig(rights) : fakeConfig());
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing([folder, photo]));
    api.search.mockResolvedValue({ items: [photo], hasMore: false, cursor: null } as never);
    store.route.set({ path: 'Media', q, sort: '', desc: false });
    await store.load();
    const dialogs = TestBed.inject(Dialogs);
    dialogs.properties.set({ open: vi.fn() } as never);
    dialogs.folderExclusions.set({ open: vi.fn() } as never);
    dialogs.name.set({ newFolder: vi.fn(), newFile: vi.fn() } as never);
    dialogs.scopeTags.set({ open: vi.fn() } as never);
    menu = TestBed.inject(FolderMenu);
}

afterEach(() => vi.restoreAllMocks());

describe('FolderMenu', () => {
    const ids = () => menu.entries().map(entry => entry.id);
    const entry = (id: string) => menu.entries().find(e => e.id === id)!;

    it('offers what may be done in the folder listed, by rights', async () => {
        await setUp();
        expect(ids()).toEqual(['newFolder', 'newFile', 'upload', 'uploadFolder', 'selectAll', 'refresh', 'tags', 'exclusions', 'quickLink', 'newTab', 'properties']);

        entry('newFolder').run!();
        expect(TestBed.inject(Dialogs).name()!.newFolder).toHaveBeenCalledWith('Media');
        entry('properties').run!();
        expect(TestBed.inject(Dialogs).properties()!.open).toHaveBeenCalledWith(expect.objectContaining({ path: 'Media', name: 'Media', kind: 'directory' }));
        expect(entry('newTab')).toMatchObject({ newTab: true, href: expect.stringMatching(/\?path=Media$/) });
        expect(menu.target()).toMatchObject({ kind: 'background', name: 'Media' });
    });

    it('offers little to those who only browse, and Paste once something was kept', async () => {
        await setUp([Rights.browse, Rights.viewFiles]);
        expect(ids()).toEqual(['selectAll', 'refresh', 'newTab', 'properties']);

        TestBed.resetTestingModule();
        await setUp();
        TestBed.inject(FileTransfers).keep('copy', [photo]);
        const paste = vi.spyOn(TestBed.inject(FileTransfers), 'paste').mockReturnValue(true);
        expect(entry('paste')).toMatchObject({ label: 'Paste', shortcut: 'Ctrl+V' });
        entry('paste').run!();
        expect(paste).toHaveBeenCalledWith('Media');
    });

    it('offers the search\'s own entries while searching', async () => {
        await setUp(undefined, 'beach');
        expect(ids()).toEqual(['selectAll', 'tags', 'refresh', 'newTab', 'copyLink']);
        expect(entry('tags').label).toBe('Tag the results…');
        entry('tags').run!();
        expect(TestBed.inject(Dialogs).scopeTags()!.open).toHaveBeenCalledWith({ path: 'Media', recursive: false, query: 'beach' });
        expect(entry('newTab').href).toMatch(/\?path=Media&q=beach$/);
        expect(menu.target().name).toBe('Search “beach”');
    });

    it('opens a folder link, in a new tab too, pastes into it and shows its properties', async () => {
        await setUp();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const target = menu.linkTarget('Media/Trips');
        const entries = () => target.entries();
        const link = (id: string) => entries().find(e => e.id === id)!;

        expect(target).toMatchObject({ kind: 'folderLink', key: 'folderLink:Media/Trips', name: 'Trips' });
        expect(entries().map(e => e.id)).toEqual(['open', 'newTab', 'quickLink', 'properties']);
        link('open').run!();
        expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { path: 'Media/Trips', q: null, sort: null, desc: null } });
        expect(link('newTab').href).toMatch(/\?path=Media%2FTrips$/);
        link('properties').run!();
        expect(TestBed.inject(Dialogs).properties()!.open).toHaveBeenCalledWith(expect.objectContaining({ path: 'Media/Trips', name: 'Trips' }));

        TestBed.inject(FileTransfers).keep('move', [photo]);
        const paste = vi.spyOn(TestBed.inject(FileTransfers), 'paste').mockReturnValue(true);
        expect(link('paste').label).toBe('Paste into Trips');
        link('paste').run!();
        expect(paste).toHaveBeenCalledWith('Media/Trips');
        expect(menu.linkTarget('').name).toBe(TestBed.inject(ConfigService).config()!.rootName);
    });

    it('uploads the files picked into the folder listed', async () => {
        await setUp();
        const add = vi.spyOn(TestBed.inject(UploadQueue), 'add').mockResolvedValue();
        let picker: HTMLInputElement | undefined;
        vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(function (this: HTMLInputElement) { picker = this; });

        entry('uploadFolder').run!();
        expect(picker!.type).toBe('file');
        expect(picker!.webkitdirectory).toBe(true);
        picker!.dispatchEvent(new Event('change'));

        expect(add).toHaveBeenCalledWith([], 'Media');
    });

    it('is the page\'s menu: a right click on nothing with a menu of its own', async () => {
        await setUp();
        const contextMenu = TestBed.inject(ContextMenu);
        contextMenu.page.set(menu.target());
        const event = new MouseEvent('contextmenu', { cancelable: true, clientX: 5, clientY: 6 });
        Object.defineProperty(event, 'target', { value: document.body });

        contextMenu.fromPage(event);

        expect(event.defaultPrevented).toBe(true);
        expect(contextMenu.open()?.target.kind).toBe('background');
        // Not inside a dialog (the viewer, sheets): the browser's menu there, for now.
        const dialog = document.body.appendChild(document.createElement('dialog'));
        const inDialog = new MouseEvent('contextmenu', { cancelable: true });
        Object.defineProperty(inDialog, 'target', { value: dialog });
        contextMenu.fromPage(inDialog);
        expect(inDialog.defaultPrevented).toBe(false);
        dialog.remove();
    });
});
