import { ComponentFixture, TestBed } from '@angular/core/testing';
import { item, loadConfig, mockApi, settle, tag, testProviders } from '../../../testing/fakes';
import { ListingStore } from '../listing.store';
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
        fixture = TestBed.createComponent(ViewerComponent);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => TestBed.resetTestingModule());

    it('shows a picture drawn smaller than it is at its actual size where it is clicked', async () => {
        await view();
        expect(picture().classList).toContain('zoomable');
        expect(button('Actual size')).not.toBeNull();

        // The centre of the picture stays under the pointer.
        await click(500, 350);

        expect(picture().classList).toContain('actual-size');
        expect(picture().style.width).toBe('4000px');
        expect(picture().style.height).toBe('3000px');
        expect(transform()).toBe('translate(-1500px, -1200px)');
        expect(button('Fit to the screen')).not.toBeNull();
        expect(button('Actual size')).toBeNull();
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
        expect(button('Actual size')).toBeNull();
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
        api.textFile.mockImplementation(async (path: string) => ({ text: `Text of ${path}\nline 2`, encoding: 'utf-8', lineEnding: 'lf', version: 'v', size: 20, modified: '2026-10-18T10:00:00Z' }));
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        store = TestBed.inject(ListingStore);
        store.items.set([photo, notes, archive, clip]);
        fixture = TestBed.createComponent(ViewerComponent);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => TestBed.resetTestingModule());

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
});
