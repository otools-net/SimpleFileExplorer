import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ViewerComponent } from '../library/viewer/viewer.component';
import { ApiMock, fakeCanvases, fakeConfig, item, limitedConfig, ALL_RIGHTS, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { PICTURE_LOADER } from './edits';
import { ImageEditorDialog } from './image-editor.dialog';

/** A decoded picture of the given size (jsdom decodes nothing). */
function picture(width: number, height: number): HTMLImageElement {
    const image = document.createElement('img');
    Object.defineProperty(image, 'naturalWidth', { value: width });
    Object.defineProperty(image, 'naturalHeight', { value: height });
    return image;
}

/** A pointer event at a point of the screen (jsdom has no PointerEvent). */
function pointer(type: string, clientX: number, clientY: number): Event {
    const event = new MouseEvent(type, { bubbles: true, cancelable: true, clientX, clientY, button: 0 });
    Object.defineProperty(event, 'pointerId', { value: 1 });
    return event;
}

describe('ImageEditorDialog', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<ImageEditorDialog>;
    let element: HTMLElement;
    let drawn: unknown[][];
    let loader: ReturnType<typeof vi.fn>;

    const photo = item('Media/photo.jpg', { kind: 'image' });
    const dialog = () => element.querySelector('dialog')!;
    const button = (label: string) => [...element.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === label || b.getAttribute('aria-label') === label)!;
    const field = (name: string) => element.querySelector<HTMLInputElement>(`input[data-field="${name}"]`)!;
    const frame = () => [field('x'), field('y'), field('width'), field('height')].map(input => Number(input.value));
    const subtitle = () => element.querySelector('.viewer-title span')!.textContent!.trim();
    const aspectButton = (id: string) => element.querySelector<HTMLButtonElement>(`.aspect-choice[data-aspect="${id}"]`)!;
    const pickAspect = async (id: string) => {
        aspectButton(id).click();
        await settle();
    };
    const setField = async (name: string, value: string) => {
        field(name).value = value;
        field(name).dispatchEvent(new Event('change'));
        await settle();
    };

    async function setUp(config = fakeConfig()): Promise<void> {
        api = mockApi();
        loader = vi.fn(() => Promise.resolve(picture(4000, 3000)));
        TestBed.configureTestingModule({ providers: testProviders(api, { provide: PICTURE_LOADER, useValue: loader }) });
        await loadConfig(api, config);
        drawn = fakeCanvases().drawn;
        fixture = TestBed.createComponent(ImageEditorDialog);
        element = fixture.nativeElement;
        await fixture.whenStable();
    }

    async function open(): Promise<void> {
        await fixture.componentInstance.open(photo);
        await settle();
    }

    afterEach(() => vi.restoreAllMocks());

    it('opens the original from this server, even with a data server, framed whole', async () => {
        await setUp(fakeConfig({ dataServer: 'http://nas/files' }));

        await open();

        expect(dialog().open).toBe(true);
        expect(loader).toHaveBeenCalledWith('api/files/raw/Media/photo.jpg');
        expect(element.querySelector('.editor-canvas canvas')).not.toBeNull();
        expect(subtitle()).toBe('4000 × 3000');
        expect(frame()).toEqual([0, 0, 4000, 3000]);
        expect(button('Save a copy').disabled).toBe(true);
        expect(element.querySelector('.editor-panel')!.textContent).toContain('in “Edited”');
    });

    it('frames by numbers, following the aspect ratio', async () => {
        await setUp();
        await open();

        await pickAspect('1:1');
        expect(frame()).toEqual([500, 0, 3000, 3000]);

        await setField('width', '1000');
        expect(frame()).toEqual([500, 0, 1000, 1000]);

        await setField('x', '9999');
        expect(frame()).toEqual([3000, 0, 1000, 1000]);
        expect(subtitle()).toBe('4000 × 3000 → 1000 × 1000');
        expect(button('Save a copy').disabled).toBe(false);
    });

    it('offers the usual ratios as buttons, one picked at a time', async () => {
        await setUp();
        await open();

        const ids = [...element.querySelectorAll<HTMLButtonElement>('.aspect-choice')].map(b => b.dataset['aspect']);
        expect(ids).toEqual(expect.arrayContaining(['free', 'original', '1:1', '16:9', '9:16', '4:3', '3:4', '3:2', '2:3', '4:5', '21:9']));
        expect(aspectButton('free').getAttribute('aria-checked')).toBe('true');

        await pickAspect('16:9');
        expect(frame()).toEqual([0, 375, 4000, 2250]);
        expect(aspectButton('16:9').getAttribute('aria-checked')).toBe('true');
        expect(aspectButton('free').getAttribute('aria-checked')).toBe('false');
        expect(aspectButton('9:16').querySelector('.aspect-shape')!.classList).toContain('portrait');

        await pickAspect('original');
        expect(frame()).toEqual([0, 0, 4000, 3000]);
    });

    it('keeps the frame centred and as large as possible when the ratio changes', async () => {
        await setUp();
        await open();
        await pickAspect('1:1');
        await setField('width', '1000');
        await setField('x', '0');

        await pickAspect('2:3');

        // Largest 2:3 frame (2000 × 3000), around the square's centre (500, 1500), moved inside the picture.
        expect(frame()).toEqual([0, 0, 2000, 3000]);
    });

    it('turns the ratio between landscape and portrait, also with X', async () => {
        await setUp();
        await open();
        const flip = () => element.querySelector<HTMLButtonElement>('.aspect-head button')!;
        expect(flip().disabled).toBe(true);

        await pickAspect('16:9');
        expect(flip().textContent!.trim()).toBe('Portrait');
        flip().click();
        await settle();
        expect(aspectButton('9:16').getAttribute('aria-checked')).toBe('true');
        expect(frame()).toEqual([1156, 0, 1688, 3000]);
        expect(flip().textContent!.trim()).toBe('Landscape');

        dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'x', bubbles: true }));
        await settle();
        expect(aspectButton('16:9').getAttribute('aria-checked')).toBe('true');

        await pickAspect('1:1');
        expect(flip().disabled).toBe(true);
    });

    it('drags a corner in pixels of the picture', async () => {
        await setUp();
        await open();
        const layer = element.querySelector<HTMLElement>('.crop-layer')!;
        vi.spyOn(layer, 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 400, height: 300 } as DOMRect);

        element.querySelector('[data-handle="se"]')!.dispatchEvent(pointer('pointerdown', 400, 300));
        layer.dispatchEvent(pointer('pointermove', 300, 250));
        layer.dispatchEvent(pointer('pointerup', 300, 250));
        await settle();
        expect(frame()).toEqual([0, 0, 3000, 2500]);

        // Outside the frame: a new one, drawn from there.
        layer.dispatchEvent(pointer('pointerdown', 350, 280));
        layer.dispatchEvent(pointer('pointermove', 380, 290));
        layer.dispatchEvent(pointer('pointerup', 380, 290));
        await settle();
        expect(frame()).toEqual([3500, 2800, 300, 100]);
    });

    it('applies a crop, lists it, and undoes it', async () => {
        await setUp();
        await open();

        await setField('width', '2000');
        button('Apply').click();
        await settle();

        expect(element.querySelector('.editor-history')!.textContent).toContain('Crop to 2000 × 3000');
        expect(frame()).toEqual([0, 0, 2000, 3000]);
        expect(subtitle()).toBe('4000 × 3000 → 2000 × 3000');
        expect(drawn.at(-1)!.slice(2)).toEqual([0, 0, 2000, 3000, 0, 0, 2000, 3000]);

        button('Undo').click();
        await settle();
        expect(element.querySelector('.editor-history')).toBeNull();
        expect(frame()).toEqual([0, 0, 4000, 3000]);
    });

    it('saves the cropped picture as a new file, in the format of the original', async () => {
        await setUp();
        api.saveEditedImage.mockResolvedValue({ path: 'Edited/Media/photo.jpg', width: 1000, height: 800, size: 2048 });
        await open();
        await setField('width', '1000');
        await setField('height', '800');

        button('Save a copy').click();
        await settle(10);

        expect(HTMLCanvasElement.prototype.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/jpeg', 0.92);
        expect(api.saveEditedImage).toHaveBeenCalledWith('Media/photo.jpg', expect.any(Blob));
        const sent = api.saveEditedImage.mock.calls[0][1];
        expect(await sent.text()).toBe('1000x800');
        expect(dialog().open).toBe(false);
        expect(TestBed.inject(ToastService).toasts().map(t => t.message)).toEqual(['Saved as “Edited/Media/photo.jpg” (1000 × 800, 2 KB).']);
    });

    it('saves over the original when the server allows it, from the version opened', async () => {
        await setUp();
        api.editedImageAccess.mockResolvedValue({ version: 'v1', overwrite: true, permanent: false });
        api.saveEditedImageOver.mockResolvedValue({ path: 'Media/photo.jpg', width: 1000, height: 3000, size: 2048, version: 'v2', modified: '2026-10-19T10:00:00Z', trashId: 7 });
        const store = TestBed.inject(ListingStore);
        const applied = vi.spyOn(store, 'applySaved');
        await open();
        await setField('width', '1000');

        expect(button('Save a copy').disabled).toBe(false);
        expect(element.querySelector('.editor-panel')!.textContent).toContain('Save writes over the original (the version before goes to the trash)');
        button('Save').click();
        await settle(10);

        expect(api.editedImageAccess).toHaveBeenCalledWith('Media/photo.jpg');
        expect(api.saveEditedImageOver).toHaveBeenCalledWith('Media/photo.jpg', expect.any(Blob), expect.stringMatching(/^[0-9a-f]{32}$/), 'v1');
        expect(api.saveEditedImage).not.toHaveBeenCalled();
        expect(applied).toHaveBeenCalledWith('Media/photo.jpg', { size: 2048, modified: '2026-10-19T10:00:00Z', width: 1000, height: 3000 });
        expect(dialog().open).toBe(false);
        expect(TestBed.inject(ToastService).toasts().map(t => t.message)).toEqual(['Saved (1000 × 3000, 2 KB). The version before is in the trash.']);
    });

    it('asks before saving over a picture changed meanwhile; Ctrl+Shift+S saves a copy', async () => {
        await setUp();
        api.editedImageAccess.mockResolvedValue({ version: 'v1', overwrite: true, permanent: false });
        api.saveEditedImageOver.mockRejectedValueOnce(new ApiError(412, 'Changed'))
            .mockResolvedValueOnce({ path: 'Media/photo.jpg', width: 1000, height: 3000, size: 2048, version: 'v3', modified: '2026-10-19T10:00:00Z', trashId: null });
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
        await open();
        await setField('width', '1000');

        dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
        await settle(10);

        expect(confirm).toHaveBeenCalledWith('The picture was changed elsewhere since the editor opened it. Save over it anyway?');
        expect(api.saveEditedImageOver.mock.calls.map(call => call[3])).toEqual(['v1', '*']);
        expect(dialog().open).toBe(false);

        await open();
        await setField('width', '1000');
        dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'S', ctrlKey: true, shiftKey: true, bubbles: true }));
        await settle(10);
        expect(api.saveEditedImage).toHaveBeenCalledTimes(1);
    });

    it('offers only a copy when the picture may not be saved over', async () => {
        await setUp();
        api.editedImageAccess.mockResolvedValue({ version: 'v1', overwrite: false, permanent: false });
        await open();
        await setField('width', '1000');

        expect(button('Save')).toBeUndefined();
        expect(button('Save a copy').classList).toContain('primary');
        expect(element.querySelector('.editor-panel')!.textContent).toContain('Saved as a new file in “Edited”');
    });

    it('stays open with the edits when the save fails', async () => {
        await setUp();
        api.saveEditedImage.mockRejectedValue(new ApiError(500, 'Disk full'));
        await open();
        await setField('width', '1000');

        button('Save a copy').click();
        await settle(10);

        expect(dialog().open).toBe(true);
        expect(TestBed.inject(ToastService).toasts()[0]).toMatchObject({ error: true, message: 'Could not save the picture: Disk full' });
        expect(element.querySelector('.editor-history')!.textContent).toContain('Crop to 1000 × 3000');
    });

    it('asks before closing with unsaved edits', async () => {
        await setUp();
        await open();
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        await setField('width', '1000');

        button('Close').click();
        expect(dialog().open).toBe(true);

        confirm.mockReturnValue(true);
        button('Close').click();
        expect(dialog().open).toBe(false);
        expect(confirm).toHaveBeenCalledTimes(2);
    });

    it('closes without asking when nothing changed', async () => {
        await setUp();
        await open();
        const confirm = vi.spyOn(window, 'confirm');

        button('Close').click();

        expect(dialog().open).toBe(false);
        expect(confirm).not.toHaveBeenCalled();
    });

    it('says when the browser cannot open the picture', async () => {
        await setUp();
        loader.mockRejectedValue(new DOMException('decode failed', 'EncodingError'));

        await open();

        expect(element.querySelector('.viewer-message')!.textContent).toContain('This browser cannot open this picture for editing.');
        expect(button('Save a copy').disabled).toBe(true);
    });

    it('applies with Enter and undoes with Ctrl+Z', async () => {
        await setUp();
        await open();
        await setField('height', '1500');

        dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
        await settle();
        expect(element.querySelectorAll('.editor-history li')).toHaveLength(1);

        dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true }));
        await settle();
        expect(element.querySelector('.editor-history')).toBeNull();
    });
});

describe('ViewerComponent › Edit', () => {
    let api: ApiMock;

    async function viewer(rights: string[], editor = true) {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, limitedConfig(rights, editor ? {} : { editor: null }));
        TestBed.inject(ListingStore).items.set([item('Media/photo.jpg', { kind: 'image' }), item('Media/clip.mp4', { kind: 'video' })]);
        const editorDialog = { open: vi.fn(() => Promise.resolve()) };
        TestBed.inject(Dialogs).imageEditor.set(editorDialog as unknown as ImageEditorDialog);
        const fixture = TestBed.createComponent(ViewerComponent);
        await fixture.whenStable();
        return { fixture, element: fixture.nativeElement as HTMLElement, editorDialog };
    }

    /** Crop, in the viewer's ⋮ menu (opened for it). */
    const editButton = async (element: HTMLElement) => {
        const more = element.querySelector<HTMLButtonElement>('button[aria-label="More actions"]');
        if (more && !element.querySelector('.viewer-menu')) {
            more.click();
            await settle();
        }
        return element.querySelector<HTMLButtonElement>('[data-entry="crop"]');
    };

    afterEach(() => TestBed.resetTestingModule());

    it('offers to edit pictures to those with the right, also with E', async () => {
        const { fixture, element, editorDialog } = await viewer(ALL_RIGHTS);
        fixture.componentInstance.open(item('Media/photo.jpg', { kind: 'image' }));
        await settle();

        expect(await editButton(element)).not.toBeNull();
        element.querySelector('dialog')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'e', bubbles: true }));
        expect(editorDialog.open).toHaveBeenCalledWith(expect.objectContaining({ path: 'Media/photo.jpg' }));

        fixture.componentInstance.open(item('Media/clip.mp4', { kind: 'video' }));
        await settle();
        expect(await editButton(element)).toBeNull();
    });

    it('hides it without the right, or from a server without the editor', async () => {
        const without = await viewer(ALL_RIGHTS.filter(r => r !== 'files.edit'));
        without.fixture.componentInstance.open(item('Media/photo.jpg', { kind: 'image' }));
        await settle();
        expect(await editButton(without.element)).toBeNull();

        TestBed.resetTestingModule();
        const older = await viewer(ALL_RIGHTS, false);
        older.fixture.componentInstance.open(item('Media/photo.jpg', { kind: 'image' }));
        await settle();
        expect(await editButton(older.element)).toBeNull();
    });
});
