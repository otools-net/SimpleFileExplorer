import { TestBed } from '@angular/core/testing';
import { DirectoryListing, FolderExclusions, LibraryItem } from '../../core/models';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { ItemMenu } from '../../files/item-menu';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FolderItemComponent } from '../items/folder-item.component';
import { exclusionChip, exclusionTitle } from '../items/exclusions';
import { ListingStore } from '../listing.store';
import { LocationComponent } from '../location/location.component';
import { SidebarService } from '../sidebar/sidebar.service';
import { ExclusionsSectionComponent } from './exclusions-section.component';
import { FolderExclusionsDialog } from './folder-exclusions.dialog';
import { ScanService } from './scan.service';

let api: ApiMock;
let store: ListingStore;

const none: FolderExclusions = { scan: null, trickplay: null, thumbnails: null };

async function setUp(config = fakeConfig()): Promise<void> {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
}

async function show(path: string, items: LibraryItem[], exclusions: FolderExclusions | null = null): Promise<void> {
    const page: DirectoryListing = { path, name: path.split('/').pop() ?? '', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: items.length, fileCount: 0, exclusions };
    store.route.set({ path, q: '', sort: '', desc: false });
    api.browse.mockResolvedValue(page);
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('Exclusion wording', () => {
    it('says briefly what a folder is kept out of', () => {
        expect(exclusionChip(null)).toBeNull();
        expect(exclusionChip(none)).toBeNull();
        expect(exclusionChip({ ...none, scan: 'A' })).toBe('not scanned');
        expect(exclusionChip({ ...none, thumbnails: 'A' })).toBe('no thumbnails');
        expect(exclusionChip({ ...none, trickplay: 'A' })).toBe('no trickplay');
        expect(exclusionChip({ ...none, trickplay: 'A', thumbnails: 'A' })).toBe('no previews');
        expect(exclusionChip({ scan: 'A', trickplay: null, thumbnails: 'A' })).toBe('not scanned · no thumbnails');
        expect(exclusionChip({ scan: 'A', trickplay: 'A', thumbnails: 'A' })).toBe('skipped');
    });

    it('names the folder above it is excluded through', () => {
        expect(exclusionTitle({ scan: 'Media', trickplay: null, thumbnails: 'Media' }, 'Media'))
            .toBe('Kept out of scans (what the index has of it stays as it was) and thumbnails by an administrator.');
        expect(exclusionTitle({ ...none, trickplay: 'Media' }, 'Media/Old')).toBe('Kept out of trickplay by an administrator, through “Media”.');
    });
});

describe('Excluded folders in the listing', () => {
    beforeEach(async () => await setUp());

    it('show a small chip on the card and on the line, with the full wording in its hint', async () => {
        const make = async (value: LibraryItem, cards: boolean) => {
            const fixture = TestBed.createComponent(FolderItemComponent);
            fixture.componentRef.setInput('item', value);
            fixture.componentRef.setInput('cards', cards);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };
        const card = await make(item('Old', { excluded: { ...none, scan: 'Old' } }), true);
        const line = await make(item('Raw', { excluded: { ...none, trickplay: 'Raw', thumbnails: 'Raw' } }), false);
        const plain = await make(item('Films'), false);

        expect(card.querySelector('.folder-caption .exclusion-chip')!.textContent!.trim()).toBe('not scanned');
        expect(card.querySelector('.folder')!.getAttribute('title')).toContain('Kept out of scans');
        expect(line.querySelector('.folder > .exclusion-chip')!.textContent!.trim()).toBe('no previews');
        expect(plain.querySelector('.exclusion-chip')).toBeNull();
        expect(plain.querySelector('.folder')!.getAttribute('title')).toBe('Films');
    });

    it('say it in the location bar, which opens the dialog for those who may change it', async () => {
        const dialog = TestBed.createComponent(FolderExclusionsDialog).componentInstance;
        const open = vi.spyOn(dialog, 'open').mockImplementation(() => { });
        await show('Media/Old', [], { ...none, scan: 'Media' });
        const fixture = TestBed.createComponent(LocationComponent);
        await settle();
        const chip = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.summary button.exclusion-chip')!;

        expect(chip.textContent!.trim()).toBe('not scanned');
        expect(chip.title).toContain('through “Media”');
        chip.click();
        expect(open).toHaveBeenCalledWith('Media/Old', { ...none, scan: 'Media' });
    });

    it('only as text without the right', async () => {
        TestBed.resetTestingModule();
        await setUp(limitedConfig([Rights.browse]));
        await show('Media', [], { ...none, thumbnails: 'Media' });
        const fixture = TestBed.createComponent(LocationComponent);
        await settle();
        const element = fixture.nativeElement as HTMLElement;

        expect(element.querySelector('.summary span.exclusion-chip')!.textContent!.trim()).toBe('no thumbnails');
        expect(element.querySelector('.summary button.exclusion-chip')).toBeNull();
    });
});

describe('Scans and previews… in the menu', () => {
    it('is offered on folders to those with the right, while browsing', async () => {
        await setUp();
        const dialog = TestBed.createComponent(FolderExclusionsDialog).componentInstance;
        const open = vi.spyOn(dialog, 'open').mockImplementation(() => { });
        const folder = item('Media', { excluded: { ...none, scan: 'Media' } });
        await show('', [folder, item('a.jpg')]);
        const menu = TestBed.inject(ItemMenu);

        const entry = menu.entries(folder).find(e => e.id === 'exclusions')!;
        expect(menu.entries(item('a.jpg')).some(e => e.id === 'exclusions')).toBe(false);
        entry.run();
        expect(open).toHaveBeenCalledWith('Media', { ...none, scan: 'Media' });
    });

    it('is not offered without the right', async () => {
        await setUp(limitedConfig([Rights.browse]));
        TestBed.createComponent(FolderExclusionsDialog);
        const folder = item('Media');
        await show('', [folder]);

        expect(TestBed.inject(ItemMenu).entries(folder).some(e => e.id === 'exclusions')).toBe(false);
    });
});

describe('FolderExclusionsDialog', () => {
    beforeEach(async () => await setUp());

    it('saves only what changed, then refreshes the listing', async () => {
        api.setFolderExclusions.mockResolvedValue({ scan: 'Media/Old', trickplay: 'Media/Old', thumbnails: null });
        const refresh = vi.spyOn(store, 'refresh').mockResolvedValue();
        const fixture = TestBed.createComponent(FolderExclusionsDialog);
        const element: HTMLElement = fixture.nativeElement;
        fixture.componentInstance.open('Media/Old', { ...none, scan: 'Media/Old' });
        await settle();
        const box = (kind: string) => element.querySelector<HTMLInputElement>(`input[data-kind=${kind}]`)!;

        expect(box('scan').checked).toBe(true);
        expect(buttonNamed(element, 'Save')!.disabled).toBe(true);
        box('trickplay').click();
        await settle();
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.setFolderExclusions).toHaveBeenCalledWith('Media/Old', { trickplay: true });
        expect(refresh).toHaveBeenCalled();
    });

    it('shows what comes from a folder above as checked and fixed', async () => {
        const fixture = TestBed.createComponent(FolderExclusionsDialog);
        const element: HTMLElement = fixture.nativeElement;
        fixture.componentInstance.open('Media/Old', { ...none, thumbnails: 'Media' });
        await settle();
        const box = element.querySelector<HTMLInputElement>('input[data-kind=thumbnails]')!;

        expect(box.checked).toBe(true);
        expect(box.disabled).toBe(true);
        expect(element.textContent).toContain('Excluded through “Media”');
    });

    it('shows why the server refused', async () => {
        api.setFolderExclusions.mockRejectedValue(new Error('This list is set by the server\'s configuration'));
        const fixture = TestBed.createComponent(FolderExclusionsDialog);
        const element: HTMLElement = fixture.nativeElement;
        fixture.componentInstance.open('Media', none);
        await settle();
        element.querySelector<HTMLInputElement>('input[data-kind=scan]')!.click();
        await settle();
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(element.querySelector('.form-error')!.textContent).toContain('set by the server');
    });
});

describe('Scanning with excluded folders', () => {
    beforeEach(async () => await setUp());

    it('does not scan a folder kept out of scans', async () => {
        const ask = vi.spyOn(window, 'confirm');
        const show_ = vi.spyOn(TestBed.inject(ToastService), 'show');
        await show('Media/Old', [], { ...none, scan: 'Media' });

        await TestBed.inject(ScanService).start();

        expect(ask).not.toHaveBeenCalled();
        expect(api.startScan).not.toHaveBeenCalled();
        expect(show_).toHaveBeenCalledWith('“Media/Old” is inside “Media”, which is excluded from scans.');
    });

    it('names the excluded folders listed before scanning', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValue(false);
        await show('', [item('Media', { excluded: { ...none, scan: 'Media' } }), item('Films')]);

        await TestBed.inject(ScanService).start();

        expect(ask.mock.lastCall![0]).toContain('Excluded from scans: “Media”. It is skipped');
    });
});

describe('ExclusionsSectionComponent', () => {
    const rows = (element: HTMLElement) => [...element.querySelectorAll<HTMLTableRowElement>('tbody tr')].map(row => row.dataset['path']);
    const box = (element: HTMLElement, path: string, kind: string) =>
        element.querySelector<HTMLInputElement>(`tr[data-path="${path}"] input[data-kind=${kind}]`)!;

    async function create(): Promise<HTMLElement> {
        const fixture = TestBed.createComponent(ExclusionsSectionComponent);
        await settle();
        return fixture.nativeElement as HTMLElement;
    }

    beforeEach(async () => {
        await setUp();
        api.folderExclusions.mockResolvedValue([
            { path: 'Gone', scan: false, trickplay: true, thumbnails: false, exists: false },
            { path: 'Media', scan: true, trickplay: false, thumbnails: false, exists: true },
        ]);
    });

    it('lists the excluded folders, a box per list, and those gone from the disk', async () => {
        const element = await create();

        expect(rows(element)).toEqual(['Gone', 'Media']);
        expect([box(element, 'Media', 'scan').checked, box(element, 'Media', 'trickplay').checked]).toEqual([true, false]);
        expect(element.querySelector('tr[data-path="Gone"]')!.textContent).toContain('folder not found');
        expect(box(element, 'Gone', 'scan').disabled).toBe(true);
        expect(box(element, 'Gone', 'trickplay').disabled).toBe(false);
        expect(element.querySelector('tr[data-path="Media"] a')!.getAttribute('href')).toContain('path=Media');
    });

    it('saves a box at once, and puts it back when refused', async () => {
        api.setFolderExclusions.mockResolvedValueOnce({ scan: 'Media', trickplay: 'Media', thumbnails: null });
        const element = await create();

        box(element, 'Media', 'trickplay').click();
        await settle();
        expect(api.setFolderExclusions).toHaveBeenCalledWith('Media', { trickplay: true });
        expect(box(element, 'Media', 'trickplay').checked).toBe(true);

        const error = vi.spyOn(TestBed.inject(ToastService), 'error');
        api.setFolderExclusions.mockRejectedValueOnce(new Error('locked'));
        box(element, 'Media', 'thumbnails').click();
        await settle();
        expect(box(element, 'Media', 'thumbnails').checked).toBe(false);
        expect(error).toHaveBeenCalled();
    });

    it('removes a folder from every list it is on', async () => {
        api.setFolderExclusions.mockResolvedValue(none);
        const element = await create();

        buttonNamed(element.querySelector('tr[data-path="Gone"]')!, 'Remove')!.click();
        await settle();

        expect(api.setFolderExclusions).toHaveBeenCalledWith('Gone', { trickplay: false });
        expect(rows(element)).toEqual(['Media']);
    });

    it('adds a folder picked in the library', async () => {
        api.browse.mockImplementation(async ({ path }: { path: string }) => ({
            path, name: path, parentPath: null, offset: 0, limit: 200, total: 2, directoryCount: 1, fileCount: 1,
            items: path ? [] : [item('Photos'), item('a.jpg')],
        }));
        api.setFolderExclusions.mockResolvedValue({ ...none, thumbnails: 'Photos' });
        const element = await create();

        buttonNamed(element, 'Add folder…')!.click();
        await settle();
        expect([...element.querySelectorAll('.folder-picker li button')].map(b => b.textContent!.trim())).toEqual(['Photos']);
        element.querySelector<HTMLButtonElement>('.folder-picker li button')!.click();
        await settle();
        element.querySelector<HTMLInputElement>('.exclusions-kinds input[data-kind=scan]')!.click();
        element.querySelector<HTMLInputElement>('.exclusions-kinds input[data-kind=thumbnails]')!.click();
        await settle();
        buttonNamed(element, 'Add')!.click();
        await settle();

        expect(api.setFolderExclusions).toHaveBeenCalledWith('Photos', { thumbnails: true });
        expect(api.folderExclusions).toHaveBeenCalledTimes(2);
        expect(element.querySelector('.exclusions-form')).toBeNull();
    });
});
