import { ComponentFixture, TestBed } from '@angular/core/testing';
import { item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
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
