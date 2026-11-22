import { TestBed } from '@angular/core/testing';
import { ConfigService } from '../core/config.service';
import { PlaceProperties } from '../core/connection.models';
import { ItemProperties, LibraryItem } from '../core/models';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    item,
    loadConfig,
    mockApi,
    settle,
    testProviders,
} from '../../testing/fakes';
import { enterPlace } from '../library/connection-pages';
import { ItemMenu } from './item-menu';
import { PropertiesDialog } from './properties.dialog';

const photo = item('Trips/beach.jpg', { size: 2048, modified: '2025-06-07T08:09:10Z' });
const folder = item('Trips', { kind: 'directory', modified: null, size: null });

let api: ApiMock;
let config: ConfigService;

function remote(of: LibraryItem, overrides: Partial<PlaceProperties> = {}): PlaceProperties {
    return {
        item: of,
        kind: 'dropbox',
        account: 'someone@example.org',
        contentHash: 'ab'.repeat(32),
        hashName: 'Dropbox content hash',
        downloadable: true,
        ...overrides,
    };
}

function local(of: LibraryItem): ItemProperties {
    return {
        item: of,
        details: {
            path: of.path,
            name: of.name,
            isFolder: of.kind === 'directory',
            size: of.size,
            created: null,
            modified: '2025-06-07T08:09:10Z',
            accessed: null,
            hidden: false,
            readOnly: false,
            isLink: false,
        },
        canChecksum: true,
    };
}

async function setUp(onPlace: boolean): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    const kinds = [{ kind: 'dropbox', name: 'Dropbox', icon: 'cloud', signIn: [], capabilities: {} }];
    await loadConfig(api, fakeConfig({ connections: { levels: ['account'], kinds } }));
    config = TestBed.inject(ConfigService);
    if (onPlace) enterPlace(config, 7);
}

async function open(of: LibraryItem) {
    const fixture = TestBed.createComponent(PropertiesDialog);
    await fixture.whenStable();
    await fixture.componentInstance.open(of);
    await settle();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
}

afterEach(() => {
    if (config) enterPlace(config, null);
    vi.restoreAllMocks();
});

describe("Properties of a connection's items", () => {
    it("asks the connection's route and shows what the provider tells", async () => {
        await setUp(true);
        api.connectionGet.mockResolvedValue(remote(photo));

        const element = await open(photo);

        expect(api.connectionGet).toHaveBeenCalledWith(7, 'properties', { path: 'Trips/beach.jpg' });
        expect(api.properties).not.toHaveBeenCalled();
        const text = element.textContent!;
        expect(text).toContain('JPG image');
        expect(text).toContain('Dropbox');
        expect(text).toContain('someone@example.org');
        expect(text).toContain('/Trips/beach.jpg');
        expect(text).toContain('Dropbox content hash');
        expect(element.querySelector('.properties-hash code')!.textContent).toBe('ab'.repeat(32));
        expect(buttonNamed(element, 'Copy')).toBeDefined();
    });

    it('offers no counts, totals, checksums or full path on a connection', async () => {
        await setUp(true);
        api.connectionGet.mockResolvedValue(remote(folder, { contentHash: null, hashName: null }));

        const element = await open(folder);

        expect(element.textContent).toContain('Folder');
        expect(element.textContent).not.toContain('Modified');
        expect(buttonNamed(element, 'Count on disk')).toBeUndefined();
        expect(buttonNamed(element, 'Compute')).toBeUndefined();
        expect(buttonNamed(element, 'Copy full path')).toBeUndefined();
        expect(api.folderTotals).not.toHaveBeenCalled();
    });

    it('tells when an online document has no content to download', async () => {
        await setUp(true);
        api.connectionGet.mockResolvedValue(remote(photo, { downloadable: false }));

        const element = await open(photo);

        expect(element.textContent).toContain('None to download: this is an online document.');
    });

    it("asks the library's route outside a connection", async () => {
        await setUp(false);
        api.properties.mockResolvedValue(local(photo));

        await open(photo);

        expect(api.properties).toHaveBeenCalledWith('Trips/beach.jpg');
        expect(api.connectionGet).not.toHaveBeenCalled();
    });

    it('is offered in the item menu on a connection', async () => {
        await setUp(true);
        const menu = TestBed.inject(ItemMenu);

        expect(menu.entries(photo).map((entry) => entry.id)).toContain('properties');
    });
});
