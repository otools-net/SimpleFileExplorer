import { TestBed } from '@angular/core/testing';
import { checkedOf, valueOf } from '../shared/events';
import { baseName, encodePath, parentPath } from './paths';
import { readPreference, writePreference } from './preferences';
import { ToastService } from './toast.service';

describe('paths', () => {
    it('encodes each segment, keeping the separators', () => {
        expect(encodePath('')).toBe('');
        expect(encodePath('a/b c/d')).toBe('a/b%20c/d');
        expect(encodePath('#?%+&=')).toBe('%23%3F%25%2B%26%3D');
        expect(encodePath('Ñandú/日本/S̷e')).toBe(`${encodeURIComponent('Ñandú')}/${encodeURIComponent('日本')}/${encodeURIComponent('S̷e')}`);
    });

    it('gives the parent and the name of a path, the root being ""', () => {
        expect(parentPath('a/b/c.jpg')).toBe('a/b');
        expect(parentPath('a')).toBe('');
        expect(parentPath('')).toBe('');
        expect(baseName('a/b/c.jpg')).toBe('c.jpg');
        expect(baseName('a')).toBe('a');
        expect(baseName('')).toBe('');
    });
});

describe('preferences', () => {
    beforeEach(() => localStorage.clear());
    afterEach(() => vi.restoreAllMocks());

    it('reads what was written, else the fallback', () => {
        expect(readPreference('sfe.x')).toBeNull();
        expect(readPreference('sfe.x', 'grid')).toBe('grid');
        writePreference('sfe.x', 'list');
        expect(readPreference('sfe.x', 'grid')).toBe('list');
    });

    it('treats an empty value as unset', () => {
        writePreference('sfe.x', '');
        expect(readPreference('sfe.x', 'grid')).toBe('grid');
    });

    it('falls back, and drops writes, when storage is unavailable', () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('denied', 'SecurityError'); });
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('full', 'QuotaExceededError'); });

        expect(readPreference('sfe.x', 'grid')).toBe('grid');
        expect(() => writePreference('sfe.x', 'list')).not.toThrow();
    });
});

describe('ToastService', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        TestBed.configureTestingModule({});
    });

    afterEach(() => vi.useRealTimers());

    it('shows messages in order and removes each after its timeout', () => {
        const toasts = TestBed.inject(ToastService);

        toasts.show('Saved');
        toasts.error('Failed', 1000);
        expect(toasts.toasts().map(t => [t.message, t.error])).toEqual([['Saved', false], ['Failed', true]]);

        vi.advanceTimersByTime(1000);
        expect(toasts.toasts().map(t => t.message)).toEqual(['Saved']);
        vi.advanceTimersByTime(3500);
        expect(toasts.toasts()).toEqual([]);
    });

    it('keeps the same messages apart', () => {
        const toasts = TestBed.inject(ToastService);

        toasts.show('Same', { timeout: 100 });
        toasts.show('Same', { timeout: 200 });
        const [first, second] = toasts.toasts();
        expect(first.id).not.toBe(second.id);
        vi.advanceTimersByTime(100);

        expect(toasts.toasts()).toEqual([second]);
    });
});

describe('events', () => {
    it('reads the value and the checked state of the control an event came from', () => {
        const input = document.createElement('input');
        input.value = 'typed';
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.checked = true;
        const read = (target: Element) => {
            const event = new Event('input');
            Object.defineProperty(event, 'target', { value: target });
            return event;
        };

        expect(valueOf(read(input))).toBe('typed');
        expect(checkedOf(read(box))).toBe(true);
    });
});
