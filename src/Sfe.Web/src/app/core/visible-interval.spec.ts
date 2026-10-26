import { setHidden } from '../../testing/fakes';
import { visibleInterval } from './visible-interval';

describe('visibleInterval', () => {
    beforeEach(() => vi.useFakeTimers());

    afterEach(() => {
        setHidden(false);
        vi.useRealTimers();
    });

    it('ticks at its pace while the page shows, not at all while hidden, and at once when shown again', () => {
        const tick = vi.fn();
        const stop = visibleInterval(tick, 2000);

        vi.advanceTimersByTime(4000);
        expect(tick).toHaveBeenCalledTimes(2);

        setHidden(true);
        vi.advanceTimersByTime(60_000);
        expect(tick).toHaveBeenCalledTimes(2);

        setHidden(false);
        expect(tick).toHaveBeenCalledTimes(3);
        vi.advanceTimersByTime(2000);
        expect(tick).toHaveBeenCalledTimes(4);

        stop();
        vi.advanceTimersByTime(10_000);
        setHidden(true);
        setHidden(false);
        expect(tick).toHaveBeenCalledTimes(4);
    });

    it('waits for the page to show when started hidden', () => {
        setHidden(true);
        const tick = vi.fn();
        const stop = visibleInterval(tick, 1000);

        vi.advanceTimersByTime(5000);
        expect(tick).not.toHaveBeenCalled();

        setHidden(false);
        vi.advanceTimersByTime(1000);
        expect(tick).toHaveBeenCalledTimes(2);
        stop();
    });
});
