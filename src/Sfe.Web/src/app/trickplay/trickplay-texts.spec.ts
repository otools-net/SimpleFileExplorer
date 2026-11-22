import { TrickplayStatus } from './trickplay.models';
import { lastCheck, passCounts, waiting, workingOn } from './trickplay-texts';

function status(values: Partial<TrickplayStatus> = {}): TrickplayStatus {
    return {
        state: 'running',
        checked: 0,
        generated: 0,
        failed: 0,
        queued: 0,
        pending: 0,
        discovering: false,
        current: [],
        lastPassFinished: null,
        ...values,
    };
}

describe('trickplay texts', () => {
    it('counts what the pass did, failures only when there are some', () => {
        expect(passCounts(status({ checked: 1, generated: 1 }))).toEqual(['1 video checked', '1 created', null]);
        expect(passCounts(status({ checked: 1200, generated: 3, failed: 2 }))).toEqual([
            `${(1200).toLocaleString()} videos checked`,
            '3 created',
            '2 failed',
        ]);
    });

    it('says what waits, with a plus while the pass still looks for videos', () => {
        expect(waiting(status())).toEqual([null, null]);
        expect(waiting(status({ queued: 1, pending: 40 }))).toEqual(['1 opened video waiting', '40 to do']);
        expect(waiting(status({ queued: 2, pending: 40, discovering: true }))).toEqual([
            '2 opened videos waiting',
            '40+ to do',
        ]);
    });

    it('names the videos being processed as a list', () => {
        expect(workingOn(['a.mp4'])).toBe('Working on “a.mp4”.');
        expect(workingOn(['Trips/a.mp4', 'b.mp4'])).toBe('Working on “Trips/a.mp4” and “b.mp4”.');
    });

    it('says when the last complete check was, if there was one', () => {
        expect(lastCheck(status())).toBeNull();
        expect(lastCheck(status({ lastPassFinished: '2026-11-20T10:00:00Z' }))).toMatch(/^last complete check /);
    });
});
