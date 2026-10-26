import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { DirectoryListing, LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders, type } from '../../testing/fakes';
import { setArchives } from '../formats/formats';
import { Dialogs } from '../library/dialogs.service';
import { LibraryPageComponent } from '../library/library-page.component';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { ContextMenu, ContextMenuComponent } from '../menus/context-menu';
import { ItemMenu } from './item-menu';
import { QuickLinksService } from '../library/quick-links/quick-links.service';
import { NameDialog } from './name.dialog';

let api: ApiMock;
let store: ListingStore;

const folder = item('Media/Holidays', { kind: 'directory' });
const photo = item('Media/beach.jpg');

function listing(items: LibraryItem[], overrides: Partial<DirectoryListing> = {}): DirectoryListing {
    return { path: 'Media', name: 'Media', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: 1, fileCount: items.length - 1, ...overrides };
}

async function setUp(rights?: string[]): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, rights ? limitedConfig(rights) : fakeConfig());
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing([folder, photo]));
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('NameDialog', () => {
    beforeEach(() => setUp());

    function create() {
        const fixture = TestBed.createComponent(NameDialog);
        fixture.detectChanges();
        const element: HTMLElement = fixture.nativeElement;
        return { dialog: fixture.componentInstance, element, input: () => element.querySelector('input')! };
    }

    it('makes a folder in the folder listed and adds it there without loading again', async () => {
        const { dialog, element, input } = create();
        api.createFolder.mockResolvedValue(item('Media/Trips', { kind: 'directory' }));
        dialog.newFolder();
        await settle();

        expect(input().value).toBe('New folder');
        expect([input().selectionStart, input().selectionEnd]).toEqual([0, 'New folder'.length]);
        expect(element.textContent).toContain('In “Media”');
        type(input(), 'Trips');
        buttonNamed(element, 'Create')!.click();
        await settle();

        expect(api.createFolder).toHaveBeenCalledWith('Media', 'Trips');
        expect(store.items().map(i => i.name)).toEqual(['Holidays', 'Trips', 'beach.jpg']);
        expect(store.listing()).toMatchObject({ total: 3, directoryCount: 2 });
        expect(api.browse).toHaveBeenCalledTimes(1);
        expect(element.querySelector('dialog')!.open).toBe(false);
    });

    it('selects a file\'s name up to its extension, and renames it in place', async () => {
        const { dialog, element, input } = create();
        store.select(photo, true);
        api.renameItem.mockResolvedValue(item('Media/sea.jpg'));
        dialog.rename(photo);
        await settle();

        expect([input().selectionStart, input().selectionEnd]).toEqual([0, 'beach'.length]);
        expect(buttonNamed(element, 'Rename')!.disabled).toBe(true);
        type(input(), 'sea.jpg');
        await settle();
        buttonNamed(element, 'Rename')!.click();
        await settle();

        expect(api.renameItem).toHaveBeenCalledWith('Media/beach.jpg', 'sea.jpg');
        expect(store.items().map(i => i.path)).toEqual(['Media/Holidays', 'Media/sea.jpg']);
        expect([...store.selection()]).toEqual(['Media/sea.jpg']);
        expect(store.revealed()).toBe('Media/sea.jpg');
    });

    it('reloads the quick links that followed a renamed folder', async () => {
        const { dialog, element, input } = create();
        const quickLinks = TestBed.inject(QuickLinksService);
        quickLinks.links.set([{ path: 'Media/Holidays/2024', name: '2024', kind: 'directory', extension: null, exists: true }]);
        api.renameItem.mockResolvedValue(item('Media/Trips', { kind: 'directory' }));
        api.quickLinks.mockResolvedValue([{ path: 'Media/Trips/2024', name: '2024', kind: 'directory', extension: null, exists: true }]);
        dialog.rename(folder);
        type(input(), 'Trips');
        await settle();
        buttonNamed(element, 'Rename')!.click();
        await settle();

        expect(quickLinks.links().map(link => link.path)).toEqual(['Media/Trips/2024']);
    });

    it('selects a whole folder name, dots and all', async () => {
        const { dialog, input } = create();
        dialog.rename(item('Media/v1.2', { kind: 'directory' }));
        await settle();

        expect([input().selectionStart, input().selectionEnd]).toEqual([0, 4]);
    });

    it('shows why the server refused, and stays open', async () => {
        const { dialog, element, input } = create();
        api.createFolder.mockResolvedValueOnce({ type: 'file-exists', detail: 'There is already a file or folder named “Holidays” here.' });
        dialog.newFolder();
        type(input(), 'Holidays');
        await settle();
        buttonNamed(element, 'Create')!.click();
        await settle();

        expect(element.querySelector('.form-error')!.textContent).toContain('already a file or folder named “Holidays”');
        expect(element.querySelector('dialog')!.open).toBe(true);
        expect(store.items()).toHaveLength(2);

        api.createFolder.mockRejectedValueOnce(new ApiError(400, 'A name can\'t contain “/” or “\\”: it is one name, not a path.'));
        type(input(), 'a/b');
        await settle();
        expect(element.querySelector('.form-error')).toBeNull();
        buttonNamed(element, 'Create')!.click();
        await settle();

        expect(element.querySelector('.form-error')!.textContent).toContain('one name, not a path');
    });

    it('says that a name starting with a dot is a hidden item', async () => {
        const { dialog, element, input } = create();
        dialog.newFolder();
        type(input(), '.config');
        await settle();

        expect(element.textContent).toContain('hidden item');
    });
});

describe('ItemMenu', () => {
    function menuEvent(shiftKey = false): MouseEvent {
        return new MouseEvent('contextmenu', { cancelable: true, clientX: 30, clientY: 40, shiftKey });
    }

    function rightClick(event: MouseEvent, value: LibraryItem): void {
        TestBed.inject(ContextMenu).fromEvent(event, TestBed.inject(ItemMenu).target(value));
    }

    it('offers renaming only to those who may rename here, and Properties to everyone who browses', async () => {
        await setUp([Rights.browse, Rights.viewTags]);
        const menu = TestBed.inject(ItemMenu);
        const event = menuEvent();
        rightClick(event, photo);

        expect(menu.hasActions(photo)).toBe(true);
        expect(menu.entries(photo).map(e => e.id)).toEqual(['newTab', 'tags', 'properties']);
        expect(event.defaultPrevented).toBe(true);
        expect(TestBed.inject(ContextMenu).open()?.target.item).toBe(photo);
    });

    it('opens a file in the viewer or in a new tab, its viewer\'s page for formats that have one', async () => {
        await setUp([Rights.browse]);
        const menu = TestBed.inject(ItemMenu);
        const links = TestBed.inject(ConfigService).links;
        const open = vi.fn();
        TestBed.inject(Dialogs).viewer.set({ open } as never);
        const notes = item('Media/notes.txt', { kind: 'file' });
        const archive = item('Media/backup.zip', { kind: 'file' });

        expect(menu.entries(photo).map(e => e.id)).toEqual(['open', 'newTab', 'properties']);
        menu.entries(photo)[0].run();
        expect(open).toHaveBeenCalledWith(photo);
        const newTab = menu.entries(photo)[1];
        expect(newTab).toMatchObject({ href: links.file(photo.path), newTab: true });
        expect(menu.entries(notes).find(e => e.id === 'newTab')!.href).toBe(links.view(notes.path));
        // Nothing to show it in: only its own tab.
        expect(menu.entries(archive).map(e => e.id)).toEqual(['newTab', 'properties']);
    });

    it('lists a folder, or lists it in a new tab keeping the sort', async () => {
        await setUp([Rights.browse]);
        const menu = TestBed.inject(ItemMenu);
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        store.route.set({ path: 'Media', q: '', sort: 'size', desc: true });

        const [open, newTab] = menu.entries(folder);
        open.run();

        expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { path: 'Media/Holidays', q: null, sort: 'size', desc: '1' } });
        expect(newTab.id).toBe('newTab');
        expect(newTab.newTab).toBe(true);
        expect(newTab.href).toMatch(/\?path=Media%2FHolidays&sort=size&desc=1$/);
    });

    it('offers no opening for a selection of several items, and the containing folder in search results', async () => {
        await setUp([Rights.browse]);
        const menu = TestBed.inject(ItemMenu);
        store.select(photo, true);
        store.select(folder, true);
        expect(menu.entries(photo).map(e => e.id)).toEqual(['properties']);
        store.select(folder, false);
        expect(menu.entries(photo).map(e => e.id)).toEqual(['newTab', 'properties']);

        store.route.set({ path: '', q: 'beach', sort: '', desc: false });
        const reveal = vi.spyOn(store, 'reveal').mockImplementation(() => undefined);
        const found = item('Media/beach.jpg', { directory: 'Media' });
        const entry = menu.entries(found).find(e => e.id === 'openFolder')!;
        entry.run();
        expect(reveal).toHaveBeenCalledWith('Media/beach.jpg');
    });

    it('adds an item to the quick links, or takes it out when it is one', async () => {
        await setUp();
        const menu = TestBed.inject(ItemMenu);
        const quickLinks = TestBed.inject(QuickLinksService);
        const toggle = vi.spyOn(quickLinks, 'toggle').mockResolvedValue(true);
        const entry = () => menu.entries(folder).find(e => e.id === 'quickLink')!;

        expect(entry().label).toBe('Add to quick links');
        entry().run();
        expect(toggle).toHaveBeenCalledWith('Media/Holidays');
        quickLinks.links.set([{ path: 'Media/Holidays', name: 'Holidays', kind: 'directory', extension: null, exists: true }]);
        expect(entry().label).toBe('Remove from quick links');
    });

    it('opens files without a viewer as text, for those who may open files', async () => {
        await setUp([Rights.browse, Rights.viewFiles]);
        const menu = TestBed.inject(ItemMenu);
        const open = vi.fn();
        TestBed.inject(Dialogs).viewer.set({ open } as never);
        const archive = item('Media/backup.zip', { kind: 'file' });

        menu.entries(archive).find(e => e.id === 'openAsText')!.run();

        expect(open).toHaveBeenCalledWith(archive, { asText: true });
        expect(menu.entries(item('Media/notes.txt', { kind: 'file' })).map(e => e.id)).not.toContain('openAsText');
        expect(menu.entries(photo).map(e => e.id)).not.toContain('openAsText');
        TestBed.resetTestingModule();
        await setUp([Rights.browse]);
        TestBed.inject(Dialogs).viewer.set({ open } as never);
        expect(TestBed.inject(ItemMenu).entries(archive).map(e => e.id)).not.toContain('openAsText');
    });

    it('opens containers (comic books, books, office documents) as archives, for those who may open files', async () => {
        await setUp([Rights.browse, Rights.viewFiles]);
        setArchives({ maxCompressedTarMegabytes: 10 });
        try {
            const menu = TestBed.inject(ItemMenu);
            const open = vi.fn();
            TestBed.inject(Dialogs).viewer.set({ open } as never);
            const comic = item('Media/issue 1.cbz', { kind: 'file' });

            menu.entries(comic).find(e => e.id === 'openAsArchive')!.run();

            expect(open).toHaveBeenCalledWith(comic, { asArchive: true });
            expect(menu.entries(item('Media/backup.zip', { kind: 'file' })).map(e => e.id), 'an archive opens as one').not.toContain('openAsArchive');
            expect(menu.entries(photo).map(e => e.id)).not.toContain('openAsArchive');
        } finally {
            setArchives(null);
        }
    });

        it('edits text files in the viewer and crops pictures, for those who may', async () => {
        await setUp([Rights.browse, Rights.viewFiles, Rights.createFiles, Rights.editImages]);
        const menu = TestBed.inject(ItemMenu);
        const open = vi.fn();
        const crop = vi.fn();
        TestBed.inject(Dialogs).viewer.set({ open } as never);
        TestBed.inject(Dialogs).imageEditor.set({ open: crop } as never);
        const notes = item('Media/notes.txt', { kind: 'file' });

        menu.entries(notes).find(e => e.id === 'editText')!.run();
        menu.entries(photo).find(e => e.id === 'editPicture')!.run();

        expect(open).toHaveBeenCalledWith(notes, { edit: true });
        expect(crop).toHaveBeenCalledWith(photo);
        expect(menu.entries(photo).map(e => e.id)).not.toContain('editText');
        expect(menu.entries(notes).map(e => e.id)).not.toContain('editPicture');
        expect(menu.entries(item('Media/backup.zip', { kind: 'file' })).map(e => e.id)).not.toContain('editText');

        TestBed.resetTestingModule();
        await setUp([Rights.browse, Rights.viewFiles]);
        TestBed.inject(Dialogs).viewer.set({ open } as never);
        TestBed.inject(Dialogs).imageEditor.set({ open: crop } as never);
        expect(TestBed.inject(ItemMenu).entries(notes).map(e => e.id)).not.toContain('editText');
        expect(TestBed.inject(ItemMenu).entries(photo).map(e => e.id)).not.toContain('editPicture');
    });

    it('has no quick links entry without the right', async () => {
        await setUp([Rights.browse]);
        expect(TestBed.inject(ItemMenu).entries(photo).map(e => e.id)).toEqual(['newTab', 'properties']);
    });

    it('follows the folder rules of the folder listed and of each folder in it', async () => {
        await setUp();
        api.browse.mockResolvedValue(listing([folder, photo], {
            access: { rights: [Rights.browse, Rights.renameFiles], pathOnly: false, children: { Holidays: { rights: [Rights.browse], pathOnly: false } } },
        }));
        await store.load();
        const menu = TestBed.inject(ItemMenu);

        expect(menu.canRename(photo)).toBe(true);
        expect(menu.canRename(folder)).toBe(false);
    });

    it('opens at the pointer on a right click, and the browser\'s own menu with Shift', async () => {
        await setUp();
        const fixture = TestBed.createComponent(ContextMenuComponent);
        fixture.detectChanges();
        const menu = TestBed.inject(ItemMenu);
        const rename = vi.fn();
        TestBed.inject(Dialogs).name.set({ rename } as unknown as NameDialog);

        const shifted = menuEvent(true);
        rightClick(shifted, photo);
        expect(shifted.defaultPrevented).toBe(false);
        const event = menuEvent();
        rightClick(event, photo);
        await settle();
        fixture.detectChanges();
        const element: HTMLElement = fixture.nativeElement;

        expect(event.defaultPrevented).toBe(true);
        expect([...element.querySelectorAll('[data-entry]')].map(e => e.getAttribute('data-entry'))).toEqual(['newTab', 'rename', 'delete', 'cut', 'copy', 'moveTo', 'copyTo', 'download', 'tags', 'quickLink', 'properties']);
        expect(document.activeElement?.getAttribute('data-entry')).toBe('newTab');
        (element.querySelector('[data-entry="rename"]') as HTMLButtonElement).click();
        await settle();

        expect(rename).toHaveBeenCalledWith(photo);
        expect(TestBed.inject(ContextMenu).open()).toBeNull();
    });

    it('closes on Esc, on a click elsewhere and when the listing changes', async () => {
        await setUp();
        const fixture = TestBed.createComponent(ContextMenuComponent);
        fixture.detectChanges();
        const menu = TestBed.inject(ItemMenu);
        const element: HTMLElement = fixture.nativeElement;

        rightClick(menuEvent(), photo);
        await settle();
        // On phones, the standard sheet at the bottom over its backdrop (phone-sheets.css).
        expect(element.querySelector('.item-menu')!.classList).toContain('phone-sheet');
        expect(element.querySelector('.sheet-backdrop')).not.toBeNull();
        element.querySelector('.item-menu')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(TestBed.inject(ContextMenu).open()).toBeNull();

        rightClick(menuEvent(), photo);
        await settle();
        document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        expect(TestBed.inject(ContextMenu).open()).toBeNull();

        rightClick(menuEvent(), photo);
        await settle();
        await store.load();
        await settle();
        expect(TestBed.inject(ContextMenu).open()).toBeNull();
    });
});

describe('the listing page\'s keys', () => {
    async function page(rights?: string[]) {
        await setUp(rights);
        api.config.mockResolvedValue(rights ? limitedConfig(rights) : fakeConfig());
        const fixture = TestBed.createComponent(LibraryPageComponent);
        fixture.detectChanges();
        await settle();
        const dialogs = TestBed.inject(Dialogs);
        const newFolder = vi.spyOn(dialogs.name()!, 'newFolder').mockImplementation(() => { });
        const rename = vi.spyOn(dialogs.name()!, 'rename').mockImplementation(() => { });
        return { newFolder, rename };
    }

    function press(key: string, options: KeyboardEventInit = {}, target: EventTarget = document.body): KeyboardEvent {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options });
        target.dispatchEvent(event);
        return event;
    }

    it('F2 renames the only file selected; Shift+N makes a folder', async () => {
        const { newFolder, rename } = await page();
        store.route.set({ path: 'Media', q: '', sort: '', desc: false });
        await store.load();
        store.select(photo, true);

        press('F2');
        press('N', { shiftKey: true });

        expect(rename).toHaveBeenCalledWith(photo);
        expect(newFolder).toHaveBeenCalled();
    });

    it('moves through the items with the arrows once one has the focus, and selects with Space and Shift', async () => {
        await page();
        store.route.set({ path: 'Media', q: '', sort: '', desc: false });
        await store.load();
        await settle();
        const link = (path: string) => document.querySelector<HTMLElement>(`sfe-listing [data-path="${path}"] a`)!;

        const ignored = press('ArrowDown');
        expect(ignored.defaultPrevented).toBe(false);

        link('Media/Holidays').focus();
        press('ArrowRight', {}, document.activeElement!);
        expect(document.activeElement).toBe(link('Media/beach.jpg'));
        press(' ', {}, document.activeElement!);
        expect([...store.selection()]).toEqual(['Media/beach.jpg']);

        press('Home', { shiftKey: true }, document.activeElement!);
        expect(document.activeElement).toBe(link('Media/Holidays'));
        expect(store.selectedItems().map(i => i.path)).toEqual(['Media/Holidays', 'Media/beach.jpg']);
    });

    it('Delete deletes the selection, Shift+Delete for good', async () => {
        await page();
        store.route.set({ path: 'Media', q: '', sort: '', desc: false });
        await store.load();
        const open = vi.spyOn(TestBed.inject(Dialogs).delete()!, 'open').mockImplementation(() => { });
        store.select(folder, true);
        store.select(photo, true);

        const plain = press('Delete');
        press('Delete', { shiftKey: true });

        expect(plain.defaultPrevented).toBe(true);
        expect(open.mock.calls).toEqual([[[folder, photo], false], [[folder, photo], true]]);
    });

    it('does nothing without the rights', async () => {
        const { newFolder, rename } = await page([Rights.browse, Rights.viewFiles]);
        store.route.set({ path: 'Media', q: '', sort: '', desc: false });
        await store.load();
        store.select(photo, true);

        const f2 = press('F2');
        press('N', { shiftKey: true });

        expect(rename).not.toHaveBeenCalled();
        expect(newFolder).not.toHaveBeenCalled();
        expect(f2.defaultPrevented).toBe(false);
    });
});
