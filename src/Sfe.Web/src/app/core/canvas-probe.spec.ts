import { TestBed } from '@angular/core/testing';
import { fakeCanvases } from '../../testing/fakes';
import { CANVAS_REFUSED, canvasCheck, canvasReadable } from './canvas-probe';
import { ToastService } from './toast.service';

describe('canvas probe', () => {
    afterEach(() => vi.restoreAllMocks());

    it('reads back the colour it drew', () => {
        fakeCanvases();
        expect(canvasReadable()).toBe(true);
    });

    it('is refused when the read gives white (Firefox resistFingerprinting) or nothing can be drawn', () => {
        fakeCanvases({ readable: false });
        expect(canvasReadable()).toBe(false);

        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);
        expect(canvasReadable()).toBe(false);
    });

    it('is refused when the read throws', () => {
        fakeCanvases();
        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
            () =>
                ({
                    fillRect: () => {},
                    getImageData: () => {
                        throw new DOMException('blocked', 'SecurityError');
                    },
                }) as never,
        );
        expect(canvasReadable()).toBe(false);
    });

    it('lets slightly nudged colours through (Firefox fingerprinting protection without resistFingerprinting)', () => {
        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
            () =>
                ({
                    fillRect: () => {},
                    getImageData: (_x: number, _y: number, w: number, h: number) => ({
                        data: Uint8ClampedArray.from({ length: w * h * 4 }, (_, i) => [15, 137, 213, 255][i % 4]),
                    }),
                }) as never,
        );
        expect(canvasReadable()).toBe(true);
    });

    it('explains the refusal in an error toast', () => {
        fakeCanvases({ readable: false });
        const check = TestBed.runInInjectionContext(() => canvasCheck());

        expect(check()).toBe(false);

        const toasts = TestBed.inject(ToastService).toasts();
        expect(toasts.map((toast) => [toast.message, toast.level === 'error'])).toEqual([[CANVAS_REFUSED, true]]);
    });

    it('says nothing when canvases can be read', () => {
        fakeCanvases();
        expect(TestBed.runInInjectionContext(() => canvasCheck())()).toBe(true);
        expect(TestBed.inject(ToastService).toasts()).toEqual([]);
    });
});
