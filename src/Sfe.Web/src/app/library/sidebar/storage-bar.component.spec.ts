import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfigService } from '../../core/config.service';
import { ApiMock, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { StorageUsage } from '../library.models';
import { ListingStore } from '../listing.store';
import { ChangeFeed } from '../change-feed.service';
import { STORAGE_CHANGE_DELAY_MS, STORAGE_REFRESH_MS, StorageBarComponent } from './storage-bar.component';

describe('StorageBarComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<StorageBarComponent>;
    let host: HTMLElement;
    let now: number;

    const usage = (used: number, total: number): StorageUsage => ({ reported: true, used, total, free: total - used });

    async function create(answer: StorageUsage = usage(50, 100)): Promise<void> {
        api.storage.mockResolvedValue(answer);
        fixture = TestBed.createComponent(StorageBarComponent);
        host = fixture.nativeElement;
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        now = 1_000_000;
        vi.spyOn(Date, 'now').mockImplementation(() => now);
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
    });

    afterEach(() => vi.restoreAllMocks());

    it("shows the library disk's space with a bar", async () => {
        await create();

        expect(api.storage).toHaveBeenCalledWith(null, '');
        expect(host.hidden).toBe(false);
        expect(host.querySelector('.storage-line')!.textContent).toBe('50 B used of 100 B, 50 B free');
        expect(host.querySelector('[role=meter]')!.getAttribute('aria-valuenow')).toBe('50');
        expect(host.title).toBe('The disk holding the library is 50% full.');
    });

    it('turns orange, then red, as the space runs out', async () => {
        await create(usage(90, 100));
        expect(host.classList).toContain('nearly-full');

        api.storage.mockResolvedValue(usage(99, 100));
        now += STORAGE_REFRESH_MS;
        document.dispatchEvent(new Event('visibilitychange'));
        await settle();
        fixture.detectChanges();

        expect(host.classList).toContain('full');
        expect(host.classList).not.toContain('nearly-full');
    });

    it("shows a connection's account when a connection is shown", async () => {
        await create();
        TestBed.inject(ConfigService).place.set({ id: 7, name: 'Box' } as never);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();

        expect(api.storage).toHaveBeenLastCalledWith(7, '');
        expect(host.title).toBe('The account of the connection “Box” is 50% full.');
    });

    it('reads the disk of each library folder shown, a junction may lead to another', async () => {
        await create();
        const store = TestBed.inject(ListingStore);
        api.storage.mockResolvedValue(usage(10, 100));
        store.route.set({ path: 'Movies/2024', q: '', sort: '', desc: false });
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();

        expect(api.storage).toHaveBeenLastCalledWith(null, 'Movies/2024');
        expect(host.querySelector('.storage-line')!.textContent).toBe('10 B used of 100 B, 90 B free');

        store.route.set({ path: 'Movies/2024', q: 'cat', all: true, sort: '', desc: false });
        fixture.detectChanges();
        await settle();
        expect(api.storage).toHaveBeenLastCalledWith(null, '');
    });

    it('reads again once changes made through SFE stop', async () => {
        await create();
        vi.useFakeTimers();
        try {
            const feed = TestBed.inject(ChangeFeed);
            feed.spaceChanges.update((n) => n + 1);
            fixture.detectChanges();
            vi.advanceTimersByTime(STORAGE_CHANGE_DELAY_MS / 2);
            feed.spaceChanges.update((n) => n + 1);
            fixture.detectChanges();
            vi.advanceTimersByTime(STORAGE_CHANGE_DELAY_MS - 1);
            expect(api.storage).toHaveBeenCalledTimes(1);

            vi.advanceTimersByTime(1);
            expect(api.storage).toHaveBeenCalledTimes(2);
        } finally {
            vi.useRealTimers();
        }
    });

    it('says when the space is not reported, without a bar', async () => {
        await create({ reported: false, used: null, total: null, free: null });

        expect(host.querySelector('[role=meter]')).toBeNull();
        expect(host.querySelector('.storage-line')!.textContent).toBe('Free space not reported');
    });

    it('is hidden when the space cannot be read', async () => {
        api.storage.mockRejectedValue(new Error('needs signing in'));
        fixture = TestBed.createComponent(StorageBarComponent);
        host = fixture.nativeElement;
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();

        expect(host.hidden).toBe(true);
    });

    it('reads again only when shown and the last read is old', async () => {
        await create();
        const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');

        document.dispatchEvent(new Event('visibilitychange'));
        now += STORAGE_REFRESH_MS;
        visibility.mockReturnValue('hidden');
        document.dispatchEvent(new Event('visibilitychange'));
        expect(api.storage).toHaveBeenCalledTimes(1);

        visibility.mockReturnValue('visible');
        document.dispatchEvent(new Event('visibilitychange'));
        expect(api.storage).toHaveBeenCalledTimes(2);
    });
});
