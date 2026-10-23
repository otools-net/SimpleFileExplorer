import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ApiError } from '../../core/api.service';
import { ClientConfig, DirectoryListing, FolderCover, LibraryItem } from '../../core/models';
import { TrickplayMarksService } from '../../trickplay/trickplay-marks.service';
import { FileTagsDialog } from '../../tags/file-tags.dialog';
import { FilesTagsDialog } from '../../tags/files-tags.dialog';
import type { ScopeTagsDialog } from '../../tags/scope-tags.dialog';
import { ApiMock, deferred, fakeConfig, intersect, item, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../../testing/fakes';
import { ContextMenu } from '../../menus/context-menu';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { SelectionBarComponent } from '../selection-bar.component';
import { SidebarService } from '../sidebar/sidebar.service';
import { ViewerComponent } from '../viewer/viewer.component';
import { CopyPathService } from '../../core/copy-path.service';
import { ItemMenu } from '../../files/item-menu';
import { CoversService } from './covers.service';
import { FileRowComponent, FileTileComponent } from './file-tile.component';
import { FolderItemComponent } from './folder-item.component';
import { ItemActions } from './item-actions.service';
import { PreviewsService } from './previews.service';

let api: ApiMock;
let store: ListingStore;

function listing(items: LibraryItem[], hasMore = false): DirectoryListing {
    return { path: '', name: 'Root', parentPath: null, items, offset: 0, limit: 100, total: items.length + (hasMore ? 1 : 0), directoryCount: 0, fileCount: items.length };
}

async function setUp(config = fakeConfig()): Promise<void> {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
}

async function list(items: LibraryItem[], hasMore = false): Promise<void> {
    api.browse.mockResolvedValue(listing(items, hasMore));
    await store.load();
}

afterEach(() => vi.restoreAllMocks());

describe('SelectionBarComponent', () => {
    const a = item('a.jpg');
    const b = item('b.mp4');
    let fileTags: { open: ReturnType<typeof vi.fn> };
    let filesTags: { open: ReturnType<typeof vi.fn> };

    beforeEach(async () => {
        await setUp();
        fileTags = { open: vi.fn() };
        filesTags = { open: vi.fn() };
        TestBed.inject(Dialogs).fileTags.set(fileTags as unknown as FileTagsDialog);
        TestBed.inject(Dialogs).filesTags.set(filesTags as unknown as FilesTagsDialog);
    });

    async function bar() {
        const fixture = TestBed.createComponent(SelectionBarComponent);
        await fixture.whenStable();
        return fixture;
    }

    const button = (element: HTMLElement, label: string) => [...element.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === label)!;

    it('is hidden without a selection', async () => {
        await list([a, b]);
        const fixture = await bar();

        expect(fixture.nativeElement.hidden).toBe(true);
    });

    it('counts the selection, folders and files, and disables "Select all" once everything is', async () => {
        await list([item('Folder', { kind: 'directory' }), a, b]);
        const fixture = await bar();
        store.select(a, true);
        await settle();

        const element: HTMLElement = fixture.nativeElement;
        expect(element.hidden).toBe(false);
        expect(element.querySelector('.selection-count')!.textContent).toBe('1 file selected');
        expect(button(element, 'Select all').title).toBe('Select every file and folder listed (Ctrl+A)');

        button(element, 'Select all').click();
        await settle();
        expect(element.querySelector('.selection-count')!.textContent).toBe('1 folder and 2 files selected');
        expect(button(element, 'Select all').disabled).toBe(true);
        expect(button(element, 'Tags').title).toContain('folders selected are left out');
    });

    it('says "Select all" only takes the items loaded so far', async () => {
        await list([a, b], true);
        const fixture = await bar();
        store.select(a, true);
        await settle();

        expect(button(fixture.nativeElement, 'Select all').title).toContain('the 2 items loaded so far');
    });

    it('offers Rename for one item or several together, and Tags only with files selected', async () => {
        const folder = item('Folder', { kind: 'directory' });
        await list([folder, a, b]);
        const rename = vi.fn();
        const batch = vi.fn();
        TestBed.inject(Dialogs).name.set({ rename } as never);
        TestBed.inject(Dialogs).batchRename.set({ open: batch } as never);
        const fixture = await bar();
        const element: HTMLElement = fixture.nativeElement;
        store.select(folder, true);
        await settle();

        expect(button(element, 'Tags')).toBeUndefined();
        button(element, 'Rename').click();
        expect(rename).toHaveBeenCalledWith(folder);

        store.select(a, true);
        await settle();
        button(element, 'Rename').click();
        expect(batch).toHaveBeenCalledWith([folder, a]);
        expect(button(element, 'Tags')).toBeDefined();
    });

    it('tags one file in its own dialog and several in the shared one', async () => {
        await list([a, b]);
        const fixture = await bar();
        store.select(a, true);
        await settle();

        button(fixture.nativeElement, 'Tags').click();
        expect(fileTags.open).toHaveBeenCalledWith(a);

        store.select(b, true);
        await settle();
        button(fixture.nativeElement, 'Tags').click();
        expect(filesTags.open).toHaveBeenCalledWith([a, b]);
    });

    it('clears the selection', async () => {
        await list([a, b]);
        const fixture = await bar();
        store.selectAll();
        await settle();

        fixture.nativeElement.querySelector('[aria-label="Clear the selection"]').click();
        await settle();

        expect(store.selection().size).toBe(0);
        expect(fixture.nativeElement.hidden).toBe(true);
    });
});

describe('ItemActions', () => {
    const photo = item('photo.jpg', { tags: [summary(tag('Sun'))] });
    const doc = item('report.zip');
    let viewer: { open: ReturnType<typeof vi.fn> };
    const click = (init: MouseEventInit = {}) => new MouseEvent('click', { button: 0, cancelable: true, ...init });

    beforeEach(async () => {
        await setUp();
        viewer = { open: vi.fn() };
        TestBed.inject(Dialogs).viewer.set(viewer as unknown as ViewerComponent);
        api.fileViewed.mockResolvedValue(undefined);
        await list([photo, doc]);
    });

    it('opens media in the viewer on a plain click', () => {
        const event = click();

        TestBed.inject(ItemActions).openFile(event, photo);

        expect(event.defaultPrevented).toBe(true);
        expect(viewer.open).toHaveBeenCalledWith(photo);
        expect(api.fileViewed).not.toHaveBeenCalled();
    });

    it('opens text files in the viewer too', () => {
        const event = click();
        const notes = item('notes.txt');

        TestBed.inject(ItemActions).openFile(event, notes);

        expect(event.defaultPrevented).toBe(true);
        expect(viewer.open).toHaveBeenCalledWith(notes);
    });

    it('lets the browser open other files, and modified clicks, recording the view', () => {
        const actions = TestBed.inject(ItemActions);
        const plain = click();
        const newTab = click({ ctrlKey: true });

        actions.openFile(plain, doc);
        actions.openFile(newTab, photo);

        expect(plain.defaultPrevented).toBe(false);
        expect(newTab.defaultPrevented).toBe(false);
        expect(viewer.open).not.toHaveBeenCalled();
        expect(api.fileViewed).toHaveBeenCalledWith(photo.path);
    });

    it('selects instead of opening while files are selected', () => {
        const actions = TestBed.inject(ItemActions);
        store.select(doc, true);

        const event = click();
        actions.openFile(event, photo);
        expect(event.defaultPrevented).toBe(true);
        expect(store.isSelected(photo.path)).toBe(true);

        actions.openFile(click(), photo);
        expect(store.isSelected(photo.path)).toBe(false);
        expect(viewer.open).not.toHaveBeenCalled();
    });

    it('still opens in a new tab while files are selected', () => {
        store.select(doc, true);
        const event = click({ metaKey: true });

        TestBed.inject(ItemActions).openFile(event, photo);

        expect(event.defaultPrevented).toBe(false);
        expect(store.isSelected(photo.path)).toBe(false);
    });

    it('searches for a tag clicked on a file', () => {
        const navigate = vi.spyOn(store, 'navigate').mockImplementation(() => { });

        TestBed.inject(ItemActions).searchTag(summary(tag('Big "Sky"')));

        expect(navigate).toHaveBeenCalledWith({ q: 't:"Big Sky"' });
    });
});

describe('PreviewsService', () => {
    it('tries an image itself, then its thumbnail; a video only has its thumbnail', async () => {
        await setUp(fakeConfig({ largeImageSize: 1000 }));
        const previews = TestBed.inject(PreviewsService);

        expect(previews.sources(item('a b.jpg', { size: 10 }))).toEqual(['api/files/raw/a%20b.jpg', 'api/thumbnails/a%20b.jpg']);
        expect(previews.sources(item('big.jpg', { size: 5000 }))).toEqual(['api/thumbnails/big.jpg']);
        expect(previews.sources(item('v.mp4'))).toEqual(['api/thumbnails/v.mp4']);
        expect(previews.sources(item('a.txt'))).toEqual([]);
    });

    it('tries a large image itself when asked for the original', async () => {
        await setUp(fakeConfig({ largeImageSize: 1000 }));
        const previews = TestBed.inject(PreviewsService);

        expect(previews.sources(item('big.jpg', { size: 5000 }), true)).toEqual(['api/files/raw/big.jpg', 'api/thumbnails/big.jpg']);
        expect(previews.sources(item('v.mp4'), true)).toEqual(['api/thumbnails/v.mp4']);
    });

    it('shows images as they are without thumbnails, and videos without a preview', async () => {
        await setUp(fakeConfig({ largeImageSize: 1000, features: { thumbnails: false, trickplay: false } }));
        const previews = TestBed.inject(PreviewsService);

        expect(previews.sources(item('big.jpg', { size: 5000 }))).toEqual(['api/files/raw/big.jpg']);
        expect(previews.sources(item('v.mp4'))).toEqual([]);
    });

    it('guesses the usual shape from the known ones, not swayed by panoramas', async () => {
        await setUp();
        const previews = TestBed.inject(PreviewsService);
        const items = [item('a.jpg'), item('b.jpg'), item('c.jpg'), item('unknown.jpg')];
        previews.learn('a.jpg', 1.5);
        previews.learn('b.jpg', 1.5);
        previews.learn('c.jpg', 40);

        previews.scheduleGuess(() => items);
        await new Promise(resolve => requestAnimationFrame(resolve));

        expect(previews.knownRatio('a.jpg')).toBe(1.5);
        expect(previews.knownRatio('unknown.jpg')).toBeNull();
        // 40 counts as 4: (1.5 × 1.5 × 4)^(1/3).
        expect(previews.ratioGuess()).toBeCloseTo(Math.cbrt(9), 3);
    });

    it('has no guess before any preview loaded', async () => {
        await setUp();
        const previews = TestBed.inject(PreviewsService);

        previews.scheduleGuess(() => [item('a.jpg')]);
        await new Promise(resolve => requestAnimationFrame(resolve));

        expect(previews.ratioGuess()).toBeNull();
    });
});

describe('FileTileComponent', () => {
    async function tileSource(view: 'grid' | 'single', singleViewOriginals: boolean): Promise<string | null> {
        await setUp(fakeConfig({ largeImageSize: 1000, singleViewOriginals }));
        store.setView(view);
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('big.jpg', { size: 5000 }));
        await fixture.whenStable();
        return (fixture.nativeElement as HTMLElement).querySelector('img')!.getAttribute('src');
    }

    it('shows a large image through its thumbnail in the one-per-row view by default', async () => {
        expect(await tileSource('single', false)).toBe('api/thumbnails/big.jpg');
    });

    it('shows the image itself in the one-per-row view when singleViewOriginals is on', async () => {
        expect(await tileSource('single', true)).toBe('api/files/raw/big.jpg');
    });

    it('keeps thumbnails in the grid view when singleViewOriginals is on', async () => {
        expect(await tileSource('grid', true)).toBe('api/thumbnails/big.jpg');
    });

    async function tileMeta(showResolution: boolean, overrides: Partial<LibraryItem>): Promise<string> {
        await setUp(fakeConfig({ showResolution }));
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('photo.jpg', { size: 2048, ...overrides }));
        await fixture.whenStable();
        return (fixture.nativeElement as HTMLElement).querySelector('.meta')!.textContent!;
    }

    it('shows the resolution before the size', async () => {
        expect(await tileMeta(true, { width: 1920, height: 1080 })).toMatch(/^1920×1080 · 2 KB · /);
    });

    it('leaves the resolution out when turned off', async () => {
        expect(await tileMeta(false, { width: 1920, height: 1080 })).toMatch(/^2 KB · /);
    });

    it('leaves the resolution out when the index does not know it', async () => {
        expect(await tileMeta(true, {})).toMatch(/^2 KB · /);
    });

    async function videoTile(overrides: Partial<LibraryItem>, config = fakeConfig()): Promise<HTMLElement> {
        await setUp(config);
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('clip.mp4', { size: 2048, ...overrides }));
        await fixture.whenStable();
        return fixture.nativeElement as HTMLElement;
    }

    it('shows the duration of a video at the bottom left of its preview, not in its details', async () => {
        const tile = await videoTile({ width: 1280, height: 720, duration: 3725.6 });
        const start = tile.querySelector('.preview .preview-marks.start')!;
        expect([...start.children].map(child => child.className)).toEqual(['badge duration', 'trickplay-mark']);
        expect(start.querySelector('.duration')!.textContent).toBe('1:02:05');
        expect(tile.querySelector('.meta')!.textContent).toMatch(/^1280×720 · 2 KB · /);
    });

    it('keeps the duration of a video in its details when its preview cannot be shown', async () => {
        const tile = await videoTile({ duration: 65 }, fakeConfig({ features: { thumbnails: false, trickplay: false } }));
        expect(tile.querySelector('.duration')).toBeNull();
        expect(tile.querySelector('.meta')!.textContent).toMatch(/^1:05 · 2 KB · /);
    });

    it('shows the duration of an audio file on its placeholder, without a trickplay mark', async () => {
        await setUp();
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('song.mp3', { kind: 'audio', size: 2048, duration: 245.3 }));
        await fixture.whenStable();
        const tile = fixture.nativeElement as HTMLElement;
        const start = tile.querySelector('.preview .preview-marks.start')!;
        expect([...start.children].map(child => child.className)).toEqual(['badge duration']);
        expect(start.textContent).toBe('4:05');
        expect(tile.querySelector('.meta')!.textContent).toMatch(/^2 KB · /);
    });

    it('shows no duration group on an audio file whose duration is unknown', async () => {
        await setUp();
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('song.mp3', { kind: 'audio' }));
        await fixture.whenStable();
        expect((fixture.nativeElement as HTMLElement).querySelector('.preview-marks.start')).toBeNull();
    });

    it('leaves the duration out when turned off', async () => {
        const tile = await videoTile({ duration: 65 }, fakeConfig({ showDuration: false }));
        expect(tile.querySelector('.duration')).toBeNull();
        expect(tile.querySelector('.meta')!.textContent).toMatch(/^2 KB · /);
    });

    it('leaves the duration out when the index does not know it', async () => {
        const tile = await videoTile({});
        expect(tile.querySelector('.duration')).toBeNull();
        expect(tile.querySelector('.meta')!.textContent).toMatch(/^2 KB · /);
    });

    async function tileMark(resolutionMark: ClientConfig['resolutionMark'], path: string, overrides: Partial<LibraryItem>): Promise<HTMLElement | null> {
        await setUp(fakeConfig({ resolutionMark }));
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item(path, overrides));
        await fixture.whenStable();
        return (fixture.nativeElement as HTMLElement).querySelector('.preview .resolution-mark');
    }

    const thresholds = { image: 1500, video: 720 };

    it('marks a photo reaching its threshold on its shorter side as high resolution', async () => {
        const mark = (await tileMark(thresholds, 'photo.jpg', { width: 1500, height: 2000 }))!;
        expect(mark.textContent).toBe('HR');
        expect(mark.classList.contains('high')).toBe(true);
        expect(mark.title).toBe('High resolution: 1500×2000 (at least 1500 pixels on the shorter side)');
    });

    it('marks a photo under its threshold as low resolution', async () => {
        const mark = (await tileMark(thresholds, 'photo.jpg', { width: 1000, height: 1000 }))!;
        expect(mark.textContent).toBe('LR');
        expect(mark.classList.contains('high')).toBe(false);
        expect(mark.title).toBe('Low resolution: 1000×1000 (under 1500 pixels on the shorter side)');
    });

    it('marks a video of the same resolution by the threshold of videos', async () => {
        const mark = (await tileMark(thresholds, 'clip.mp4', { width: 1000, height: 1000 }))!;
        expect(mark.textContent).toBe('HR');
        expect(mark.title).toBe('High resolution: 1000×1000 (at least 720 pixels on the shorter side)');
    });

    it('marks a video under its threshold as low resolution', async () => {
        expect((await tileMark(thresholds, 'clip.mp4', { width: 854, height: 480 }))!.textContent).toBe('LR');
    });

    it('shows no mark when the marks are off', async () => {
        expect(await tileMark(null, 'photo.jpg', { width: 4000, height: 3000 })).toBeNull();
    });

    it('shows no mark when the resolution is unknown', async () => {
        expect(await tileMark(thresholds, 'photo.jpg', {})).toBeNull();
    });

    it('puts the mark next to the media badge', async () => {
        await setUp(fakeConfig({ resolutionMark: { image: 1500, video: 1080 } }));
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('clip.mp4', { width: 3840, height: 2160 }));
        await fixture.whenStable();
        const marks = (fixture.nativeElement as HTMLElement).querySelector('.preview-marks')!;
        expect([...marks.children].map(child => child.className)).toEqual(['resolution-mark high', 'badge']);
    });
});

describe('FileRowComponent', () => {
    async function sizeCell(showResolution: boolean): Promise<HTMLElement> {
        await setUp(fakeConfig({ showResolution }));
        const fixture = TestBed.createComponent(FileRowComponent);
        fixture.componentRef.setInput('item', item('clip.mp4', { size: 2048, width: 1280, height: 720 }));
        await fixture.whenStable();
        return (fixture.nativeElement as HTMLElement).querySelector('.size')!;
    }

    it('puts the resolution under the size', async () => {
        const cell = await sizeCell(true);
        expect(cell.querySelector('.resolution')?.textContent).toBe('1280×720');
        expect(cell.textContent).toBe('2 KB1280×720');
    });

    it('leaves it out when turned off', async () => {
        expect((await sizeCell(false)).querySelector('.resolution')).toBeNull();
    });

    it('puts the duration of a video under its resolution', async () => {
        await setUp(fakeConfig());
        const fixture = TestBed.createComponent(FileRowComponent);
        fixture.componentRef.setInput('item', item('clip.mp4', { size: 2048, width: 1280, height: 720, duration: 65 }));
        await fixture.whenStable();
        const cell = (fixture.nativeElement as HTMLElement).querySelector('.size')!;
        expect(cell.querySelector('.duration')?.textContent).toBe('1:05');
        expect(cell.textContent).toBe('2 KB1280×7201:05');
    });

    it('puts the duration of an audio file under its size', async () => {
        await setUp(fakeConfig());
        const fixture = TestBed.createComponent(FileRowComponent);
        fixture.componentRef.setInput('item', item('song.mp3', { kind: 'audio', size: 2048, duration: 245.3 }));
        await fixture.whenStable();
        const cell = (fixture.nativeElement as HTMLElement).querySelector('.size')!;
        expect(cell.querySelector('.resolution')).toBeNull();
        expect(cell.textContent).toBe('2 KB4:05');
    });

    it('marks the thumbnail high or low resolution', async () => {
        await setUp(fakeConfig({ resolutionMark: { image: 1500, video: 1080 } }));
        const fixture = TestBed.createComponent(FileRowComponent);
        fixture.componentRef.setInput('item', item('clip.mp4', { width: 1280, height: 720 }));
        await fixture.whenStable();
        expect((fixture.nativeElement as HTMLElement).querySelector('.thumb .resolution-mark')?.textContent).toBe('LR');
    });
});

describe('FolderItemComponent', () => {
    beforeEach(() => setUp());

    it('is selected by its box, and by a click while something is selected (instead of opening)', async () => {
        const films = item('Films', { kind: 'directory' });
        const doc = item('notes.txt');
        await list([films, doc]);
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
        const fixture = TestBed.createComponent(FolderItemComponent);
        fixture.componentRef.setInput('item', films);
        await fixture.whenStable();
        const element: HTMLElement = fixture.nativeElement;

        element.querySelector<HTMLInputElement>('.select-box input')!.click();
        await settle();
        expect(store.isSelected('Films')).toBe(true);
        expect(element.classList.contains('selected')).toBe(true);

        element.querySelector<HTMLElement>('a.folder')!.click();
        await settle();
        expect(store.isSelected('Films')).toBe(false);
        expect(navigate).not.toHaveBeenCalled();

        store.clearSelection();
        element.querySelector<HTMLElement>('a.folder')!.click();
        await settle();
        expect(navigate).toHaveBeenCalled();
    });

    it('opens the tags of the files of the folder', async () => {
        const open = vi.fn();
        TestBed.inject(Dialogs).scopeTags.set({ open } as unknown as ScopeTagsDialog);
        const fixture = TestBed.createComponent(FolderItemComponent);
        fixture.componentRef.setInput('item', item('Media/Films', { kind: 'directory' }));
        await fixture.whenStable();

        (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.folder-tags-button')!.click();

        expect(open).toHaveBeenCalledWith({ path: 'Media/Films', recursive: false, query: null });
    });
});

describe('Items not indexed yet', () => {
    beforeEach(async () => await setUp());

    it('fades the tiles, rows and folders the index lacks, and says why in their hint', async () => {
        const make = async (type: Type<{ item: unknown }>, value: LibraryItem) => {
            const fixture = TestBed.createComponent(type);
            fixture.componentRef.setInput('item', value);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };
        const tile = await make(FileTileComponent, item('a.jpg', { indexed: false }));
        const row = await make(FileRowComponent, item('a.jpg', { indexed: false }));
        const folder = await make(FolderItemComponent, item('Films', { kind: 'directory', indexed: false }));
        const indexed = await make(FileTileComponent, item('b.jpg', { indexed: true }));
        const searched = await make(FileTileComponent, item('c.jpg'));

        expect([tile, row, folder].map(e => e.classList.contains('unindexed'))).toEqual([true, true, true]);
        expect(tile.querySelector('.item-link')!.getAttribute('title')).toContain('not indexed yet');
        expect(folder.querySelector('.folder')!.getAttribute('title')).toContain('not indexed yet');
        expect([indexed, searched].map(e => e.classList.contains('unindexed'))).toEqual([false, false]);
        expect(indexed.querySelector('.item-link')!.getAttribute('title')).toBe('b.jpg');
    });

    it('counts the indexed items of a folder over its card, or at the end of its line, when the index knows it', async () => {
        const make = async (value: LibraryItem, cards: boolean) => {
            const fixture = TestBed.createComponent(FolderItemComponent);
            fixture.componentRef.setInput('item', value);
            fixture.componentRef.setInput('cards', cards);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };
        const card = await make(item('Films', { kind: 'directory', indexed: true, indexedItems: 1234 }), true);
        const line = await make(item('Films', { kind: 'directory', indexed: true, indexedItems: 0 }), false);
        const unknown = await make(item('Films', { kind: 'directory', indexed: false }), true);

        // Over the pictures, outside the link but letting clicks through to it: the link holds its hint.
        const badge = card.querySelector('.folder-cover-marks .folder-count')!;
        expect(badge.textContent!.trim()).toBe((1234).toLocaleString());
        expect(card.querySelector('.folder')!.getAttribute('title')).toContain('indexed items directly in this folder');
        expect(line.querySelector('.folder > .folder-count')!.textContent!.trim()).toBe('0');
        expect(unknown.querySelector('.folder-count')).toBeNull();
    });
});

describe('Context menus of listed items', () => {
    beforeEach(() => setUp());

    it('opens the item\'s menu on the card, and the folder\'s menu on a search result\'s folder link', async () => {
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('Media/Trips/a.jpg', { directory: 'Media/Trips' }));
        await fixture.whenStable();
        const element: HTMLElement = fixture.nativeElement;
        const menu = TestBed.inject(ContextMenu);
        const rightClick = (on: Element) => on.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));

        rightClick(element.querySelector('.item-name')!);
        expect(menu.open()?.target).toMatchObject({ kind: 'file', key: 'item:Media/Trips/a.jpg' });
        rightClick(element.querySelector('.item-folder')!);
        expect(menu.open()?.target).toMatchObject({ kind: 'folderLink', name: 'Trips' });
        expect(menu.open()?.target.entries().map(entry => entry.id)).toEqual(['open', 'newTab', 'quickLink']);
    });

    it('opens a tag\'s menu on its chip, not the file\'s', async () => {
        const fixture = TestBed.createComponent(FileTileComponent);
        fixture.componentRef.setInput('item', item('Media/a.jpg', { tags: [tag('Beach')] }));
        await fixture.whenStable();
        const chip = (fixture.nativeElement as HTMLElement).querySelector('sfe-tag-chip')!;

        chip.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));

        expect(TestBed.inject(ContextMenu).open()?.target).toMatchObject({ kind: 'tag', name: 'Beach' });
    });
});

describe('Copied paths', () => {
    beforeEach(async () => {
        await setUp(fakeConfig({ rootPath: '/m/' }));
        Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true });
        document.execCommand = vi.fn(() => true);
    });

    it('keeps only the tags and ⋮ buttons on cards and rows: the path is copied from the item menu', async () => {
        const make = async (type: Type<{ item: unknown }>, value: LibraryItem) => {
            const fixture = TestBed.createComponent(type);
            fixture.componentRef.setInput('item', value);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };
        const buttons = (element: HTMLElement) => [...element.querySelectorAll('button')].map(button => button.className.split(' ').find(c => c.endsWith('-button') && c !== 'icon-button'));

        expect(buttons(await make(FileTileComponent, item('a.jpg')))).toEqual(['tag-button', 'item-menu-button']);
        expect(buttons(await make(FileRowComponent, item('a.jpg')))).toEqual(['tag-button', 'item-menu-button']);
        expect(buttons(await make(FolderItemComponent, item('Films', { kind: 'directory' })))).toEqual(['folder-tags-button', 'item-menu-button']);
    });

    it('outlines the tiles, rows and folders whose path was copied, and only those', async () => {
        const make = async (type: Type<{ item: unknown }>, value: LibraryItem) => {
            const fixture = TestBed.createComponent(type);
            fixture.componentRef.setInput('item', value);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };
        const tile = await make(FileTileComponent, item('a.jpg'));
        const row = await make(FileRowComponent, item('a.jpg'));
        const other = await make(FileTileComponent, item('b.jpg'));
        const folder = await make(FolderItemComponent, item('Films', { kind: 'directory' }));
        expect(tile.classList.contains('copied')).toBe(false);

        const menu = TestBed.inject(ItemMenu);
        const copy = (value: LibraryItem) => menu.entries(value).find(entry => entry.id === 'copyPath')!.run();
        copy(item('a.jpg'));
        await settle();
        copy(item('Films', { kind: 'directory' }));
        await settle();

        expect(tile.classList.contains('copied')).toBe(true);
        expect(row.classList.contains('copied')).toBe(true);
        expect(folder.classList.contains('copied')).toBe(true);
        expect(other.classList.contains('copied')).toBe(false);
        expect(TestBed.inject(CopyPathService).copied().size).toBe(2);
    });
});

describe('CoversService', () => {
    let covers: CoversService;
    const card = () => document.createElement('div');

    beforeEach(async () => {
        await setUp();
        await list([]);
        covers = TestBed.inject(CoversService);
        TestBed.tick();
    });

    it('asks once for the folders that came into view together, and keeps their covers', async () => {
        const [a, b, c] = [card(), card(), card()];
        api.folderCovers.mockResolvedValue([{ path: 'A', items: [item('A/1.jpg')] }] as FolderCover[]);
        covers.watch(a, 'A');
        covers.watch(b, 'B');
        covers.watch(c, 'C');

        intersect(a, b);
        await settle();

        expect(api.folderCovers).toHaveBeenCalledTimes(1);
        expect(api.folderCovers.mock.lastCall![0]).toEqual(['A', 'B']);
        expect(covers.covers().get('A')).toEqual([item('A/1.jpg')]);
        // A folder without pictures is known too: it isn't asked for again.
        expect(covers.covers().get('B')).toEqual([]);
        expect(covers.covers().has('C')).toBe(false);
    });

    it('does not ask again for a known folder, nor for an unwatched card', async () => {
        api.folderCovers.mockResolvedValue([]);
        const a = card();
        covers.watch(a, 'A');
        intersect(a);
        await settle();

        const again = card();
        covers.watch(again, 'A');
        const gone = card();
        covers.watch(gone, 'B');
        covers.unwatch(gone);
        intersect(again, gone);
        await settle();

        expect(api.folderCovers).toHaveBeenCalledTimes(1);
    });

    it('asks in parts of 100 folders', async () => {
        api.folderCovers.mockResolvedValue([]);
        const cards = Array.from({ length: 150 }, (_, i) => {
            const element = card();
            covers.watch(element, `F${i}`);
            return element;
        });

        intersect(...cards);
        await settle();
        await settle();

        expect(api.folderCovers.mock.calls.map(([paths]) => paths.length)).toEqual([100, 50]);
    });

    it('forgets the covers, and drops late answers, when the listing changes', async () => {
        const late = deferred<FolderCover[]>();
        api.folderCovers.mockReturnValue(late.promise);
        const a = card();
        covers.watch(a, 'A');
        intersect(a);
        await settle();

        await list([]);
        TestBed.tick();
        late.resolve([{ path: 'A', items: [item('A/1.jpg')] }]);
        await settle();

        expect(covers.covers().size).toBe(0);
    });

    it('leaves the folder icons when the covers cannot be loaded', async () => {
        api.folderCovers.mockRejectedValue(new ApiError(500, 'Boom'));
        const a = card();
        covers.watch(a, 'A');
        intersect(a);
        await settle();

        expect(covers.covers().size).toBe(0);
    });
});

describe('TrickplayMarksService', () => {
    const video = item('v.mp4');
    const other = item('w.mkv');

    it('asks nothing while trickplay is off', async () => {
        await setUp();
        await list([video]);
        TestBed.inject(TrickplayMarksService);
        TestBed.tick();
        await settle();

        expect(api.trickplayStates).not.toHaveBeenCalled();
        expect(await TestBed.inject(TrickplayMarksService).load('v.mp4', () => true)).toBeNull();
    });

    it('asks for the states of the listed videos only, and shows them', async () => {
        await setUp(fakeConfig({ features: { thumbnails: true, trickplay: true } }));
        api.trickplayStates.mockResolvedValue({ running: true, files: { 'v.mp4': 'running' } });
        await list([item('a.jpg'), video, other]);
        const marks = TestBed.inject(TrickplayMarksService);
        TestBed.tick();
        await settle();

        expect(api.trickplayStates).toHaveBeenCalledWith(['v.mp4', 'w.mkv']);
        expect([...marks.states()]).toEqual([['v.mp4', 'running']]);
    });

    it('keeps the marks through a failed request', async () => {
        await setUp(fakeConfig({ features: { thumbnails: true, trickplay: true } }));
        api.trickplayStates.mockResolvedValueOnce({ running: true, files: { 'v.mp4': 'pending' } }).mockRejectedValue(new ApiError(0, 'offline'));
        await list([video]);
        const marks = TestBed.inject(TrickplayMarksService);
        TestBed.tick();
        await settle();

        await list([video, other]);
        TestBed.tick();
        await settle();

        expect(api.trickplayStates).toHaveBeenCalledTimes(2);
        expect([...marks.states()]).toEqual([['v.mp4', 'pending']]);
    });
});
