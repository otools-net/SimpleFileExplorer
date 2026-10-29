import { ComponentFixture, TestBed } from '@angular/core/testing';
import { item, loadConfig, mockApi, settle, tag, testProviders } from '../../../testing/fakes';
import { EditorView } from '@codemirror/view';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { leaveViewer } from './leave-viewer';
import { ContextMenu, ContextMenuComponent } from '../../menus/context-menu';
import { ViewerComponent } from './viewer.component';

/** A screen box (jsdom lays nothing out). */
function box(left: number, top: number, width: number, height: number): DOMRect {
    return { left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON: () => ({}) } as DOMRect;
}

/** A pointer event at a point of the screen (jsdom has no PointerEvent). */
function pointer(type: string, clientX: number, clientY: number): Event {
    const event = new MouseEvent(type, { bubbles: true, cancelable: true, clientX, clientY, button: 0 });
    Object.defineProperty(event, 'pointerId', { value: 1 });
    return event;
}

describe('ViewerComponent › Actual size', () => {
    let fixture: ComponentFixture<ViewerComponent>;
    let element: HTMLElement;

    const photo = item('Media/photo.jpg', { kind: 'image', size: 1000 });
    const other = item('Media/other.jpg', { kind: 'image', size: 1000 });
    const dialog = () => element.querySelector('dialog')!;
    const picture = () => element.querySelector<HTMLImageElement>('.viewer-stage > img')!;
    const button = (label: string) => element.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
    const transform = () => picture().style.transform;
    /** An entry of the viewer's ⋮ menu (opened for it). */
    async function entry(id: string): Promise<HTMLElement | null> {
        if (!document.querySelector('.item-menu')) {
            button('More actions')?.click();
            await settle();
        }
        return document.querySelector<HTMLElement>(`.item-menu [data-entry="${id}"]`);
    }

    async function press(type: string, x: number, y: number): Promise<void> {
        picture().dispatchEvent(pointer(type, x, y));
        await settle();
    }

    async function click(x: number, y: number): Promise<void> {
        await press('pointerdown', x, y);
        await press('pointerup', x, y);
    }

    async function key(name: string): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
        dialog().dispatchEvent(event);
        await settle();
        return event;
    }

    /** Opens `shown` with its picture of `natural` pixels drawn in `drawn` (a 1000 × 600 stage, 50 px below the top). */
    async function view(shown = photo, natural = { width: 4000, height: 3000 }, drawn = box(100, 50, 800, 600)): Promise<void> {
        fixture.componentInstance.open(shown);
        await settle();
        element.querySelector<HTMLElement>('.viewer-stage')!.getBoundingClientRect = () => box(0, 50, 1000, 600);
        const image = picture();
        Object.defineProperty(image, 'naturalWidth', { value: natural.width });
        Object.defineProperty(image, 'naturalHeight', { value: natural.height });
        image.getBoundingClientRect = () => drawn;
        image.dispatchEvent(new Event('load'));
        await settle();
    }

    beforeEach(async () => {
        const api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        TestBed.inject(ListingStore).items.set([photo, other]);
        // The app's menu, as the root component has it (the ⋮ menu and a right click on the stage open it).
        TestBed.createComponent(ContextMenuComponent);
        fixture = TestBed.createComponent(ViewerComponent);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => TestBed.resetTestingModule());

    it('shows a picture drawn smaller than it is at its actual size where it is clicked', async () => {
        await view();
        expect(picture().classList).toContain('zoomable');
        expect(await entry('actualSize')).not.toBeNull();

        // The centre of the picture stays under the pointer.
        await click(500, 350);

        expect(picture().classList).toContain('actual-size');
        expect(picture().style.width).toBe('4000px');
        expect(picture().style.height).toBe('3000px');
        expect(transform()).toBe('translate(-1500px, -1200px)');
        expect(await entry('fit')).not.toBeNull();
        expect(await entry('actualSize')).toBeNull();
    });

    it('follows two fingers: larger around the point between them, no larger than its actual size, fitted again when let go near it', async () => {
        await view();
        type Pinch = { first: boolean; last: boolean; movement: [number, number]; origin: [number, number]; memo?: { width: number; height: number } };
        const viewer = fixture.componentInstance as unknown as { onPinch(state: Pinch): { width: number; height: number } | undefined };
        const pinch = async (scale: number, step: 'first' | 'move' | 'last', memo?: Pinch['memo']) => {
            const next = viewer.onPinch({ first: step === 'first', last: step === 'last', movement: [scale, 0], origin: [500, 350], memo });
            await settle();
            return next;
        };

        const memo = await pinch(1, 'first');
        expect(memo).toEqual({ width: 800, height: 600 });
        expect(picture().classList).not.toContain('actual-size');
        await pinch(2, 'move', memo);
        expect(picture().style.width).toBe('1600px');
        expect(picture().style.height).toBe('1200px');
        // The point between the fingers stays where it is.
        expect(transform()).toBe('translate(-300px, -300px)');
        await pinch(10, 'move', memo);
        expect(picture().style.width).toBe('4000px');
        await pinch(1.03, 'last', memo);
        expect(picture().classList).not.toContain('actual-size');
        expect(picture().style.width).toBe('');
    });

    it('never leaves a gap at the edges when zooming near a corner', async () => {
        await view();

        await click(100, 50);

        expect(transform()).toBe('translate(0px, 0px)');
    });

    it('moves the picture by dragging it, within its edges, then fits it back on a click', async () => {
        await view();
        await click(500, 350);

        await press('pointerdown', 500, 350);
        await press('pointermove', 400, 300);
        expect(picture().classList).toContain('dragging');
        expect(transform()).toBe('translate(-1600px, -1250px)');
        await press('pointermove', 5000, 5000);
        await press('pointerup', 5000, 5000);
        expect(transform()).toBe('translate(0px, 0px)');
        expect(picture().classList).not.toContain('dragging');
        expect(picture().classList).toContain('actual-size');

        await click(300, 300);
        expect(picture().classList).not.toContain('actual-size');
        expect(picture().style.transform).toBe('');
        expect(picture().style.width).toBe('');
    });

    it('centres a side that fits the screen', async () => {
        await view(photo, { width: 2000, height: 400 }, box(0, 170, 1000, 200));

        await click(500, 270);

        expect(transform()).toBe('translate(-500px, 100px)');
    });

    it('leaves a picture already shown at its size alone', async () => {
        await view(photo, { width: 800, height: 600 });

        expect(picture().classList).not.toContain('zoomable');
        expect(await entry('actualSize')).toBeNull();
        await click(500, 350);
        expect(picture().classList).not.toContain('actual-size');
        expect((await key('z')).defaultPrevented).toBe(false);
    });

    it('zooms with Z, and Esc fits the picture back before closing', async () => {
        await view();

        await key('z');
        expect(picture().classList).toContain('actual-size');
        expect(transform()).toBe('translate(-1500px, -1200px)');
        await key('Z');
        expect(picture().classList).not.toContain('actual-size');

        await key('z');
        expect((await key('Escape')).defaultPrevented).toBe(true);
        expect(picture().classList).not.toContain('actual-size');
        expect(dialog().open).toBe(true);
        expect((await key('Escape')).defaultPrevented).toBe(false);
    });

    it('scrolls the zoomed picture with the wheel instead of moving to the next file', async () => {
        await view();
        await click(500, 350);

        element.querySelector('.viewer-stage')!.dispatchEvent(new WheelEvent('wheel', { deltaY: 100, bubbles: true, cancelable: true }));
        await settle();

        expect(transform()).toBe('translate(-1500px, -1300px)');
        expect(element.querySelector('.viewer-title strong')!.textContent).toBe('photo.jpg');
    });

    it('shows the next file fitted to the screen', async () => {
        await view();
        await click(500, 350);

        await key('ArrowRight');

        expect(element.querySelector('.viewer-title strong')!.textContent).toBe('other.jpg');
        expect(picture().classList).not.toContain('actual-size');
        expect(picture().classList).not.toContain('zoomable');
    });
});

describe('ViewerComponent › Documents', () => {
    let fixture: ComponentFixture<ViewerComponent>;
    let element: HTMLElement;
    let store: ListingStore;

    const photo = item('Media/photo.jpg', { kind: 'image' });
    const notes = item('Media/notes.txt', { kind: 'file' });
    const archive = item('Media/backup.zip', { kind: 'file' });
    const clip = item('Media/clip.mp4', { kind: 'video' });
    const dialog = () => element.querySelector('dialog')!;
    const title = () => element.querySelector('.viewer-title strong')!.textContent;
    const meta = () => element.querySelector('.viewer-title span')!.textContent ?? '';
    const content = () => element.querySelector('.cm-content');

    async function key(name: string, target: Element = dialog()): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
        target.dispatchEvent(event);
        await settle();
        return event;
    }

    async function open(shown: typeof photo, options?: { asText?: boolean }): Promise<void> {
        fixture.componentInstance.open(shown, options);
        await settle();
        await vi.waitFor(() => expect(shown.kind === 'file' ? content() : true).toBeTruthy());
    }

    beforeEach(async () => {
        const api = mockApi();
        api.textFile.mockImplementation(async (path: string) => ({ text: `Text of ${path}\nline 2`, encoding: 'utf-8', lineEnding: 'lf', version: 'v', size: 20, modified: '2026-10-18T10:00:00Z',
            access: { overwrite: true, saveCopy: true, download: true, permanent: false } }));
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        store = TestBed.inject(ListingStore);
        store.items.set([photo, notes, archive, clip]);
        // The app's menu, as the root component has it (the ⋮ menu and a right click on the stage open it).
        TestBed.createComponent(ContextMenuComponent);
        fixture = TestBed.createComponent(ViewerComponent);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => {
        TestBed.resetTestingModule();
        vi.restoreAllMocks();
    });

    it('steps from a picture to a text file and on, past files without a viewer', async () => {
        await open(photo);
        expect(meta()).toContain('1 / 3');

        await key('ArrowRight');
        await vi.waitFor(() => expect(content()?.textContent).toContain('Text of Media/notes.txt'));
        expect(title()).toBe('notes.txt');
        expect(meta()).toContain('2 / 3');
        expect(element.querySelector('.viewer-stage')!.classList).toContain('document');

        await key('ArrowRight');
        expect(title()).toBe('clip.mp4');
        await key('ArrowLeft');
        await key('ArrowLeft');
        expect(title()).toBe('photo.jpg');
    });

    it('leaves the keys, the wheel and swipes in the text to the text', async () => {
        await open(notes);

        await key('ArrowRight', content()!);    // the text's cursor moves (CodeMirror takes it)
        const wheel = new WheelEvent('wheel', { deltaY: 200, bubbles: true, cancelable: true });
        content()!.dispatchEvent(wheel);
        await settle();

        expect(wheel.defaultPrevented).toBe(false);
        expect(title()).toBe('notes.txt');
    });

    it('takes Esc in the text as leaving it, and the next Esc closes', async () => {
        await open(notes);

        const first = await key('Escape', content()!);

        expect(first.defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(dialog());
        expect(dialog().open).toBe(true);
        expect((await key('Escape')).defaultPrevented).toBe(false);
    });

    it('keeps the file shown when the listing is refreshed in place', async () => {
        await open(notes);
        const tagged = { ...notes, tags: [tag('todo')] };

        store.items.set([item('Media/new.txt', { kind: 'file' }), photo, tagged, clip]);
        await settle();

        expect(title()).toBe('notes.txt');
        expect(meta()).toContain('3 / 4');
        expect(element.querySelector('.viewer-tags')!.textContent).toContain('todo');
        expect(content()!.textContent).toContain('Text of Media/notes.txt');
    });

    it('opens any file as text when asked, and nothing otherwise', async () => {
        fixture.componentInstance.open(archive);
        await settle();
        expect(dialog().open).toBe(false);

        await open(archive, { asText: true });

        expect(title()).toBe('backup.zip');
        expect(content()!.textContent).toContain('Text of Media/backup.zip');
        expect(meta()).not.toContain(' / ');
        await key('ArrowRight');
        expect(title()).toBe('clip.mp4');
    });

    async function edit(): Promise<void> {
        await key('e', content()!);
        const view = EditorView.findFromDOM(element.querySelector<HTMLElement>('.cm-editor')!)!;
        view.dispatch({ changes: { from: 0, insert: 'draft ' } });
        await settle();
    }

    const leaveListing = () => TestBed.runInInjectionContext(() => leaveViewer(null, null!, null!, null!));

    it('asks before a file with unsaved changes is left, and stays on it when told to', async () => {
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        await open(notes);
        await edit();
        const viewer = fixture.componentInstance;

        await key('ArrowRight');
        expect(title()).toBe('notes.txt');
        expect(viewer.close()).toBe(false);
        const cancel = new Event('cancel', { cancelable: true });
        dialog().dispatchEvent(cancel);
        expect(cancel.defaultPrevented).toBe(true);
        viewer.open(photo);
        await settle();
        expect(title()).toBe('notes.txt');
        expect(TestBed.inject(Dialogs).closeAll()).toBe(false);
        expect(leaveListing()).toBe(false);
        expect(dialog().open).toBe(true);
        expect(confirm).toHaveBeenCalledTimes(6);

        // Agreed once: the questions after it (the route's, after the viewer's) pass.
        confirm.mockReturnValue(true);
        await key('ArrowRight');
        await vi.waitFor(() => expect(title()).toBe('clip.mp4'));
        expect(viewer.close()).toBe(true);
        expect(confirm).toHaveBeenCalledTimes(7);
    });

    it('leaves a file without unsaved changes without asking', async () => {
        const confirm = vi.spyOn(window, 'confirm');
        await open(notes);
        await key('e', content()!);

        await key('ArrowRight');

        expect(title()).toBe('clip.mp4');
        expect(confirm).not.toHaveBeenCalled();
        expect(leaveListing()).toBe(true);
    });

    it('keeps only tags, a menu and close in its bar; the rest is in the menu, which Esc closes first', async () => {
        await open(clip);
        const bar = [...element.querySelectorAll('.viewer-actions > :is(button, a)')].map(control => control.getAttribute('aria-label'));
        expect(bar).toEqual(['Tags', 'More actions', 'Close']);

        element.querySelector<HTMLButtonElement>('button[aria-label="More actions"]')!.click();
        await settle();
        // In the dialog: the rest of the page is inert while it is open.
        expect(dialog().querySelector('.item-menu')).not.toBeNull();
        const entries = [...element.querySelectorAll<HTMLElement>('.item-menu .item-menu-entry')];
        expect(entries.map(entry => entry.dataset['entry'])).toEqual(expect.arrayContaining(['popOut', 'newTab', 'download']));
        expect(element.querySelector<HTMLAnchorElement>('.item-menu [data-entry="newTab"]')!.target).toBe('_blank');

        const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        element.querySelector('.item-menu')!.dispatchEvent(escape);
        await settle();
        expect(escape.defaultPrevented).toBe(true);
        expect(document.querySelector('.item-menu')).toBeNull();
        expect(dialog().open).toBe(true);
    });

    it('has the same menu on its ⋮ and on a right click on the stage, without what would take the file away', async () => {
        await open(photo);
        const menu = TestBed.inject(ContextMenu);

        element.querySelector('.viewer-stage')!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 50, clientY: 60 }));
        await settle();
        const target = menu.open()!.target;
        expect(target).toMatchObject({ kind: 'file', key: 'viewer:Media/photo.jpg', item: photo });
        const ids = target.entries().map(entry => entry.id);
        expect(ids).toEqual(expect.arrayContaining(['newTab', 'copy', 'copyTo', 'properties']));
        expect(ids.filter(id => ['rename', 'delete', 'cut', 'moveTo', 'open'].includes(id))).toEqual([]);
        expect(element.querySelector('button[aria-label="More actions"]')!.getAttribute('aria-expanded')).toBe('true');

        // The ⋮ button closes it (a toggle), then opens the same menu.
        element.querySelector<HTMLButtonElement>('button[aria-label="More actions"]')!.click();
        expect(menu.open()).toBeNull();
        element.querySelector<HTMLButtonElement>('button[aria-label="More actions"]')!.click();
        expect(menu.open()!.target.key).toBe('viewer:Media/photo.jpg');

        // Another file: the menu was for the one before.
        await key('ArrowRight');
        expect(menu.open()).toBeNull();
        // A document (here a text) keeps the browser's menu on its stage; its ⋮ still has the menu.
        await vi.waitFor(() => expect(content()).toBeTruthy());
        const onText = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        element.querySelector('.viewer-stage')!.dispatchEvent(onText);
        expect(onText.defaultPrevented).toBe(false);
        expect(menu.open()).toBeNull();
        // Elsewhere in the viewer (its bar): no menu, not even the browser's.
        const onBar = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        element.querySelector('.viewer-bar')!.dispatchEvent(onBar);
        expect(onBar.defaultPrevented).toBe(true);
        expect(menu.open()).toBeNull();
    });

    it('opens a file ready to be changed when asked (a new file)', async () => {
        fixture.componentInstance.open(notes, { edit: true });
        await settle();
        await vi.waitFor(() => expect(content()?.getAttribute('aria-readonly')).not.toBe('true'));
        expect(element.querySelector('[data-save]')).not.toBeNull();
    });
});
