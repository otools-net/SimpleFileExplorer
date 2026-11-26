import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { DirectoryListing, LibraryItem, ReadOnlyReason } from '../core/models';
import { FileRowComponent, FileTileComponent } from './items/file-tile.component';
import { FolderItemComponent } from './items/folder-item.component';
import { ListingStore } from './listing.store';
import { LocationComponent } from './location/location.component';
import { itemLock, readOnlyText } from './read-only';
import { entryOf, greyedIds } from '../../testing/menus';
import { FileTransfers } from '../files/file-transfers';
import { ItemMenu } from '../files/item-menu';
import { FolderMenu } from './folder-menu';
import { SidebarService } from './sidebar/sidebar.service';

/* A folder the session can't change, and the items that differ from their folder (item-access.md). */

let api: ApiMock;
let store: ListingStore;

beforeEach(async () => {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
});

afterEach(() => vi.restoreAllMocks());

function page(items: LibraryItem[], extra: Partial<DirectoryListing> = {}): DirectoryListing {
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
        ...extra,
    };
}

async function show(listing: DirectoryListing, q = ''): Promise<void> {
    store.route.set({ path: listing.path, q, sort: '', desc: false });
    api.browse.mockResolvedValue(listing);
    api.search.mockResolvedValue({ query: q, path: '', items: [], limit: 100, hasMore: false, next: null });
    await store.load();
}

async function chip(): Promise<HTMLElement | null> {
    const fixture = TestBed.createComponent(LocationComponent);
    await settle();
    return (fixture.nativeElement as HTMLElement).querySelector('.summary .read-only-chip');
}

describe('Read-only folders', () => {
    it('show a Read only chip in the location bar, the reason in its hint', async () => {
        await show(page([], { readOnly: 'rule' }));

        const mark = await chip();

        expect(mark!.textContent!.trim()).toBe('Read only');
        expect(mark!.title).toBe('Read only: a folder rule lets you open the files here, not change them.');
    });

    it('stay quiet when the folder can be changed, and in searches', async () => {
        await show(page([]));
        expect(await chip()).toBeNull();

        TestBed.resetTestingModule();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        store = TestBed.inject(ListingStore);
        await show(page([], { readOnly: 'space' }), 'cat');
        expect(await chip()).toBeNull();
    });

    it('have one sentence for every reason', () => {
        const reasons: ReadOnlyReason[] = [
            'server',
            'space',
            'visitor',
            'rights',
            'rule',
            'link',
            'diskReadOnly',
            'diskRefused',
            'fileReadOnly',
        ];
        const texts = reasons.map(readOnlyText);

        expect(new Set(texts).size).toBe(reasons.length);
        expect(texts.every((text) => text.startsWith('Read only: ') && text.endsWith('.'))).toBe(true);
        expect(readOnlyText('diskReadOnly')).toContain('mounted read-only');
    });
});

describe('Locks on items', () => {
    const marked = item('Media/photo.jpg', { readOnly: true });
    const plain = item('Media/clip.mp4');
    const ruled = item('Media/Old', { kind: 'directory' });
    const access = {
        rights: ['files.view'],
        pathOnly: false,
        children: { old: { rights: [], pathOnly: false, readOnly: true } },
    };

    it('mark files the disk marks read-only and entries a folder rule makes read-only', async () => {
        await show(page([marked, plain, ruled], { access }));
        const make = async (type: Type<{ item: unknown }>, value: LibraryItem) => {
            const fixture = TestBed.createComponent(type);
            fixture.componentRef.setInput('item', value);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };

        const tile = await make(FileTileComponent, marked);
        const row = await make(FileRowComponent, marked);
        const folder = await make(FolderItemComponent, ruled);
        const quiet = await make(FileTileComponent, plain);

        expect(tile.querySelector('.lock-mark')!.getAttribute('title')).toBe(
            "Marked read-only on the disk: it can't be saved over.",
        );
        expect(row.querySelector('.lock-mark')).not.toBeNull();
        expect(folder.querySelector('.lock-mark')!.getAttribute('title')).toContain('folder rule');
        expect(quiet.querySelector('.lock-mark')).toBeNull();
    });

    it('are left out when the whole folder is read-only: its chip says it', () => {
        expect(itemLock(page([marked], { readOnly: 'diskRefused' }), marked)).toBeNull();
        expect(itemLock(page([marked]), marked)).not.toBeNull();
        expect(
            itemLock(
                page([ruled], {
                    access: { ...access, children: { Old: { rights: [], pathOnly: true, readOnly: true } } },
                }),
                ruled,
            ),
        ).toBeNull();
    });
});

describe('Menus on a folder the disk refuses', () => {
    const photo = item('Media/photo.jpg');
    const sub = item('Media/Old', { kind: 'directory' });

    it('grey what writes in it, with the reason, and leave the rest', async () => {
        await show(page([photo, sub], { readOnly: 'diskReadOnly' }));
        TestBed.inject(FileTransfers).keep('copy', [photo]);

        const entries = TestBed.inject(ItemMenu).entries(photo);
        const greyed = greyedIds(entries);
        const background = greyedIds(TestBed.inject(FolderMenu).entries());

        expect(greyed).toEqual(expect.arrayContaining(['cut', 'rename', 'moveTo', 'delete']));
        expect(greyed).not.toContain('copy');
        expect(greyed).not.toContain('download');
        expect(entryOf(entries, 'rename').title).toBe(readOnlyText('diskReadOnly'));
        expect(background).toEqual(expect.arrayContaining(['newFolder', 'upload', 'paste']));
        expect(greyedIds(TestBed.inject(ItemMenu).entries(sub))).toContain('paste');
    });

    it('let a paste into a sub-folder through when only the folder itself is refused', async () => {
        await show(page([sub], { readOnly: 'diskRefused' }));
        TestBed.inject(FileTransfers).keep('copy', [photo]);

        expect(greyedIds(TestBed.inject(ItemMenu).entries(sub))).not.toContain('paste');
        expect(greyedIds(TestBed.inject(ItemMenu).entries(sub))).toContain('rename');
    });

    it('stay as the rights say when the folder takes changes, or the rights already refuse', async () => {
        await show(page([photo]));
        expect(greyedIds(TestBed.inject(ItemMenu).entries(photo))).toEqual([]);

        await show(page([photo], { readOnly: 'space' }));
        expect(greyedIds(TestBed.inject(ItemMenu).entries(photo)), 'the rights grey, not the disk').toEqual([]);
    });
});
