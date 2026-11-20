import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiMock, item, loadConfig, mockApi, testProviders } from '../../../testing/fakes';
import { LibraryItem } from '../../core/models';
import { ItemMenu } from '../../files/item-menu';
import { isPartial } from '../../files/partial-files';
import { Dialogs } from '../dialogs.service';
import { SidebarService } from '../sidebar/sidebar.service';
import { ViewerComponent } from '../viewer/viewer.component';
import { FileRowComponent, FileTileComponent } from './file-tile.component';
import { FolderItemComponent } from './folder-item.component';
import { ItemActions } from './item-actions.service';

/* Files and folders still being written (name.sfe-partial): marked, never opened, a short menu. */

let api: ApiMock;
const partialFile = item('Media/clip.mp4.sfe-partial', { size: 100 });
const partialFolder = item('Media/photos.SFE-PARTIAL', { kind: 'directory' });
const click = () => new MouseEvent('click', { button: 0, bubbles: true, cancelable: true });

beforeEach(async () => {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.fileViewed.mockResolvedValue(undefined);
});

afterEach(() => vi.restoreAllMocks());

async function make(type: Type<{ item: unknown }>, value: LibraryItem): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(type);
    fixture.componentRef.setInput('item', value);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
}

describe('Partial items', () => {
    it('knows them by their ending, in any case', () => {
        expect([partialFile, partialFolder, item('clip.mp4')].map(isPartial)).toEqual([true, true, false]);
    });

    it('marks tiles, rows and folders being written, with no link to open and no preview', async () => {
        const tile = await make(FileTileComponent, partialFile);
        const row = await make(FileRowComponent, partialFile);
        const folder = await make(FolderItemComponent, partialFolder);
        const done = await make(FileTileComponent, item('Media/clip.mp4'));

        for (const element of [tile, row, folder]) {
            expect(element.classList.contains('partial')).toBe(true);
            expect(element.querySelector('.partial-mark')!.textContent!.trim()).toBe('being written');
        }
        expect([...tile.querySelectorAll('.item-link')].map((link) => link.getAttribute('href'))).toEqual([null, null]);
        expect(tile.querySelector('.item-link')!.getAttribute('title')).toContain('being written');
        expect(folder.querySelector('.folder')!.getAttribute('title')).toContain('being written');
        expect(tile.querySelector('img')).toBeNull();
        expect(done.classList.contains('partial')).toBe(false);
        expect(done.querySelector('.partial-mark')).toBeNull();
        expect(done.querySelector('.item-link')!.getAttribute('href')).not.toBeNull();
    });

    it('opens nothing on a click on a file being written', () => {
        const viewer = { open: vi.fn() };
        TestBed.inject(Dialogs).viewer.set(viewer as unknown as ViewerComponent);
        const actions = TestBed.inject(ItemActions);
        const event = click();

        actions.openFile(event, partialFile);

        expect(event.defaultPrevented).toBe(true);
        expect(actions.view(partialFile)).toBe(false);
        expect(viewer.open).not.toHaveBeenCalled();
        expect(api.fileViewed).not.toHaveBeenCalled();
    });

    it('does not open a folder being written', async () => {
        const folder = await make(FolderItemComponent, partialFolder);
        const event = click();

        folder.querySelector('.folder')!.dispatchEvent(event);

        expect(event.defaultPrevented).toBe(true);
    });

    it('keeps only Delete, Copy full path and Properties in their menus', () => {
        TestBed.inject(Dialogs).viewer.set({ open: vi.fn() } as unknown as ViewerComponent);
        const menu = TestBed.inject(ItemMenu);
        const ids = (value: LibraryItem) => menu.entries(value).map((entry) => entry.id);
        const allowed = ['delete', 'copyPath', 'properties'];

        for (const value of [partialFile, partialFolder]) {
            expect(ids(value).every((id) => allowed.includes(id))).toBe(true);
            expect(ids(value)).toContain('properties');
        }
        expect(ids(item('Media/clip.mp4'))).toContain('open');
    });
});
