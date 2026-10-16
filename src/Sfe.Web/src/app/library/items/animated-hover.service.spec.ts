import { TestBed } from '@angular/core/testing';
import { ClientConfig, LibraryItem } from '../../core/models';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, testProviders } from '../../../testing/fakes';
import { FileTileComponent } from './file-tile.component';

let api: ApiMock;

/** A tile of `file`; images over 1000 bytes are shown through their thumbnail. */
async function tile(file: LibraryItem, config: Partial<ClientConfig> = {}): Promise<HTMLElement> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, fakeConfig({ largeImageSize: 1000, ...config }));
    api.imageAnimation.mockResolvedValue({ animated: true });
    const fixture = TestBed.createComponent(FileTileComponent);
    fixture.componentRef.setInput('item', file);
    await fixture.whenStable();
    vi.useFakeTimers();     // for the hover delay; after the set-up, which waits on real ones
    return fixture.nativeElement as HTMLElement;
}

function pointer(target: HTMLElement, type: string): void {
    target.dispatchEvent(Object.assign(new MouseEvent(type), { pointerType: 'mouse', isPrimary: true }));
}

/** Rests the mouse on the tile past the hover delay. */
async function hover(card: HTMLElement): Promise<void> {
    pointer(card, 'pointerenter');
    await vi.advanceTimersByTimeAsync(400);
}

const gif = (overrides: Partial<LibraryItem> = {}) => item('fun.gif', { kind: 'image', extension: 'gif', size: 5000, ...overrides });

describe('AnimatedHoverService', () => {
    afterEach(() => {
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('plays the original of a large animated image over its thumbnail while hovered', async () => {
        const card = await tile(gif());
        await hover(card);

        expect(api.imageAnimation).toHaveBeenCalledWith('fun.gif');
        const original = card.querySelector<HTMLImageElement>('.preview img.animated-hover')!;
        expect(original.getAttribute('src')).toBe('api/files/raw/fun.gif');
        expect(original.hidden).toBe(true);
        expect(card.querySelector('.animated-hover-loading')).not.toBeNull();

        original.dispatchEvent(new Event('load'));
        expect(original.hidden).toBe(false);
        expect(card.querySelector('.animated-hover-loading')).toBeNull();
        // Under the marks.
        expect(original.nextElementSibling?.classList.contains('preview-marks')).toBe(true);

        pointer(card, 'pointerleave');
        expect(card.querySelector('.animated-hover')).toBeNull();
    });

    it('shows a downloaded original again at once, asking once whether it is animated', async () => {
        const card = await tile(gif());
        await hover(card);
        const original = card.querySelector<HTMLImageElement>('.animated-hover')!;
        original.dispatchEvent(new Event('load'));
        pointer(card, 'pointerleave');

        await hover(card);

        expect(card.querySelector('.animated-hover')).toBe(original);
        expect(original.hidden).toBe(false);
        expect(card.querySelector('.animated-hover-loading')).toBeNull();
        expect(api.imageAnimation).toHaveBeenCalledTimes(1);
    });

    it('stops the download when the mouse leaves before the original came', async () => {
        const card = await tile(gif());
        await hover(card);
        const original = card.querySelector<HTMLImageElement>('.animated-hover')!;

        pointer(card, 'pointerleave');

        expect(original.isConnected).toBe(false);
        expect(original.hasAttribute('src')).toBe(false);
        expect(card.querySelector('.animated-hover-loading')).toBeNull();
    });

    it('leaves still images as they are', async () => {
        const card = await tile(gif());
        api.imageAnimation.mockResolvedValue({ animated: false });
        await hover(card);

        expect(card.querySelector('.animated-hover')).toBeNull();
    });

    it('asks nothing for images shown whole, formats that cannot move, or when turned off', async () => {
        for (const [file, config] of [
            [gif({ size: 500 }), {}],
            [item('big.jpg', { kind: 'image', extension: 'jpg', size: 5000 }), {}],
            [gif(), { animatedHover: false }],
        ] as [LibraryItem, Partial<ClientConfig>][]) {
            TestBed.resetTestingModule();
            vi.useRealTimers();
            const card = await tile(file, config);
            await hover(card);

            expect(api.imageAnimation).not.toHaveBeenCalled();
            expect(card.querySelector('.animated-hover')).toBeNull();
        }
    });
});
