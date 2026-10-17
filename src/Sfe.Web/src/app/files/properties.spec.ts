import { TestBed } from '@angular/core/testing';
import { ItemProperties, LibraryItem, PropertyJob } from '../core/models';
import { Rights } from '../core/rights';
import { ApiMock, buttonNamed, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, tag, testProviders } from '../../testing/fakes';
import { ItemMenu } from './item-menu';
import { PropertiesDialog } from './properties.dialog';

let api: ApiMock;

const photo = item('Media/beach.jpg', { size: 2_500_000, width: 4000, height: 3000, tags: [tag('Sea')] });
const folder = item('Media/Holidays', { kind: 'directory' });

function properties(of: LibraryItem, overrides: Partial<ItemProperties> = {}): ItemProperties {
    return {
        item: of,
        details: {
            path: of.path, name: of.name, isFolder: of.kind === 'directory', size: of.size, created: '2024-01-02T03:04:05Z', modified: '2025-06-07T08:09:10Z',
            accessed: null, hidden: false, readOnly: true, isLink: false,
        },
        canChecksum: true,
        ...overrides,
    };
}

function job(overrides: Partial<PropertyJob> = {}): PropertyJob {
    return {
        id: 'c'.repeat(32), kind: 'count', path: 'Media/Holidays', algorithm: null, done: false, files: 0, folders: 0, bytes: 0, total: null,
        leftOut: 0, capped: false, checksum: null, error: null, ...overrides,
    };
}

async function open(of: LibraryItem, answer = properties(of), prepare: (api: ApiMock) => void = () => { }) {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, fakeConfig());
    api.properties.mockResolvedValue(answer);
    prepare(api);
    const fixture = TestBed.createComponent(PropertiesDialog);
    await fixture.whenStable();
    await fixture.componentInstance.open(of);
    await settle();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
}

afterEach(() => vi.restoreAllMocks());

describe('PropertiesDialog', () => {
    it('shows a file\'s size, dates, attributes, what the index knows and its tags', async () => {
        const { element } = await open(photo);

        const text = element.textContent!;
        expect(api.properties).toHaveBeenCalledWith('Media/beach.jpg');
        expect(text).toContain('JPG image');
        expect(text).toContain('2.4 MB (2,500,000 bytes)');
        expect(text).toContain('read-only');
        expect(text).toContain('4000×3000');
        expect(text).toContain('Sea');
        expect(text).toContain('Root / Media');
    });

    it('computes the checksum asked for', async () => {
        const { fixture, element } = await open(photo);
        api.startChecksum.mockResolvedValue(job({ kind: 'checksum', path: photo.path, algorithm: 'md5', total: 2_500_000 }));
        api.propertyJob.mockResolvedValue(job({ kind: 'checksum', path: photo.path, algorithm: 'md5', done: true, checksum: 'ab'.repeat(16) }));

        const select = element.querySelector<HTMLSelectElement>('#checksum-algorithm')!;
        select.value = 'md5';
        select.dispatchEvent(new Event('change'));
        buttonNamed(element, 'Compute')!.click();
        await settle(500);
        fixture.detectChanges();

        expect(api.startChecksum).toHaveBeenCalledWith('Media/beach.jpg', 'md5');
        expect(element.querySelector('.properties-checksum code')!.textContent).toBe('ab'.repeat(16));
    });

    it('offers no checksum to those who may not download the file', async () => {
        const { element } = await open(photo, properties(photo, { canChecksum: false }));

        expect(buttonNamed(element, 'Compute')).toBeUndefined();
    });

    it('shows a folder\'s totals from the index, then counts it on the disk', async () => {
        const { fixture, element } = await open(folder, properties(folder),
            mock => mock.folderTotals.mockResolvedValue({ indexed: true, files: 12, folders: 2, bytes: 3 * 1024 * 1024 }));
        expect(element.textContent).toContain('In the index: 12 files, 2 folders, 3 MB');

        api.countFolder.mockResolvedValue(job());
        api.propertyJob.mockResolvedValue(job({ done: true, files: 13, folders: 2, bytes: 4 * 1024 * 1024, leftOut: 1 }));
        buttonNamed(element, 'Count on disk')!.click();
        await settle(500);
        fixture.detectChanges();

        expect(api.countFolder).toHaveBeenCalledWith('Media/Holidays');
        expect(element.textContent).toContain('On the disk: 13 files, 2 folders, 4 MB.');
        expect(element.textContent).toContain('1 link or unreadable folder left out.');
    });

    it('stops what runs when it closes', async () => {
        const { fixture, element } = await open(photo);
        api.startChecksum.mockResolvedValue(job({ kind: 'checksum', path: photo.path, algorithm: 'sha256', total: 2_500_000 }));
        api.cancelPropertyJob.mockResolvedValue(undefined);
        buttonNamed(element, 'Compute')!.click();
        await settle();

        element.querySelector('dialog')!.dispatchEvent(new Event('close'));
        await settle();

        expect(api.cancelPropertyJob).toHaveBeenCalledWith('c'.repeat(32));
        expect(fixture).toBeTruthy();
    });
});

describe('Properties in the item menu', () => {
    it('opens the menu for everyone who browses, with Properties', async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewFiles]));
        const menu = TestBed.inject(ItemMenu);

        expect(menu.hasActions(photo)).toBe(true);
        expect(menu.entries(photo).map(entry => entry.id)).toEqual(['properties']);
        expect(menu.entries(photo)[0].shortcut).toBe('Alt+Enter');
    });
});
