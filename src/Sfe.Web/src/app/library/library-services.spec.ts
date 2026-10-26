import { TestBed } from '@angular/core/testing';
import { Api, ApiError } from '../core/api.service';
import { ScanStatus, TagSidebar } from '../core/models';
import { ToastService } from '../core/toast.service';
import { waitForTrickplay } from '../trickplay/trickplay';
import { ApiMock, deferred, loadConfig, mockApi, setHidden, settle, setMediaQuery, testProviders } from '../../testing/fakes';
import { Dialogs } from './dialogs.service';
import { ListingStore } from './listing.store';
import { describeScan, ScanService } from './scan/scan.service';
import { SidebarService } from './sidebar/sidebar.service';

const NARROW = '(max-width: 760px)';

describe('SidebarService', () => {
    let api: ApiMock;
    const data = (count: number): TagSidebar => ({ popular: Array(count).fill(null), searched: [], viewed: [], lucky: [], historyEnabled: true });

    beforeEach(() => {
        localStorage.clear();
        setMediaQuery(NARROW, false);
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
    });

    it('is open by default on wide screens, closed on phones', () => {
        expect(TestBed.inject(SidebarService).open()).toBe(true);

        TestBed.resetTestingModule();
        setMediaQuery(NARROW, true);
        TestBed.configureTestingModule({ providers: testProviders(api) });
        expect(TestBed.inject(SidebarService).open()).toBe(false);
    });

    it('remembers being closed on wide screens only', () => {
        const sidebar = TestBed.inject(SidebarService);

        sidebar.toggle();
        expect(sidebar.open()).toBe(false);
        expect(localStorage.getItem('sfe.sidebar')).toBe('closed');

        setMediaQuery(NARROW, true);
        sidebar.setOpen(true);
        expect(localStorage.getItem('sfe.sidebar')).toBe('closed');
    });

    it('follows the screen width: a drawer closes on phones, the preference comes back on wide screens', () => {
        const sidebar = TestBed.inject(SidebarService);

        setMediaQuery(NARROW, true);
        expect(sidebar.open()).toBe(false);
        setMediaQuery(NARROW, false);
        expect(sidebar.open()).toBe(true);
    });

    it('closes the drawer after a pick on phones only', () => {
        const sidebar = TestBed.inject(SidebarService);
        sidebar.closeIfOverlay();
        expect(sidebar.open()).toBe(true);

        setMediaQuery(NARROW, true);
        sidebar.setOpen(true, { remember: false });
        sidebar.closeIfOverlay();
        expect(sidebar.open()).toBe(false);
        expect(localStorage.getItem('sfe.sidebar')).toBeNull();
    });

    it('reloads once for a burst of refreshes', async () => {
        api.tagSidebar.mockResolvedValue(data(1));
        const sidebar = TestBed.inject(SidebarService);

        sidebar.refresh();
        sidebar.refresh();
        sidebar.refresh();
        await settle(350);

        expect(api.tagSidebar).toHaveBeenCalledTimes(1);
        expect(sidebar.data()).toEqual(data(1));
    });

    it('keeps the last answer asked for, and what it showed when the server fails', async () => {
        const slow = deferred<TagSidebar>();
        api.tagSidebar.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(data(2)).mockRejectedValueOnce(new ApiError(500, 'Boom'));
        const sidebar = TestBed.inject(SidebarService);

        const first = sidebar.reload();
        await sidebar.reload();
        slow.resolve(data(1));
        await first;
        expect(sidebar.data()).toEqual(data(2));

        await sidebar.reload();
        expect(sidebar.data()).toEqual(data(2));
    });
});

describe('describeScan', () => {
    const status = (overrides: Partial<ScanStatus>): ScanStatus => ({
        state: 'completed', path: '', startedAt: 'x', finishedAt: null, directoriesScanned: 1, directoriesQueued: 0, filesIndexed: 1,
        removed: 0, errors: 0, currentDirectory: null, message: null, relinked: 0, missing: null, ...overrides,
    });

    it('gives the counts, leaving out the zero ones', () => {
        expect(describeScan(status({}))).toBe('1 folder scanned · 1 file indexed');
        expect(describeScan(status({ directoriesScanned: 3, filesIndexed: 0 }))).toBe('3 folders scanned · 0 files indexed');
    });

    it('adds the waiting folders of a running scan, and where it is', () => {
        expect(describeScan(status({ state: 'running', directoriesQueued: 4, currentDirectory: 'Media/2020' })))
            .toBe('1 folder scanned · 4 waiting · 1 file indexed — Media/2020');
        expect(describeScan(status({ state: 'completed', directoriesQueued: 4 }))).not.toContain('waiting');
    });

    it('adds what was removed, relinked, missing and failed', () => {
        expect(describeScan(status({ removed: 2, relinked: 1, missing: 3, errors: 1 })))
            .toBe('1 folder scanned · 1 file indexed · 2 removed · 1 moved file relinked · 3 tagged files missing · 1 error');
    });
});

describe('ScanService', () => {
    let api: ApiMock;
    let scan: ScanService;
    const idle: ScanStatus = { state: 'idle', path: null, startedAt: null, finishedAt: null, directoriesScanned: 0, directoriesQueued: 0, filesIndexed: 0, removed: 0, errors: 0, currentDirectory: null, message: null, relinked: 0, missing: null };
    const running = (path = ''): ScanStatus => ({ ...idle, state: 'running', path, startedAt: '2026-10-10T10:00:00Z' });
    const completed: ScanStatus = { ...idle, state: 'completed', path: '', startedAt: '2026-10-10T10:00:00Z', directoriesScanned: 2, filesIndexed: 5 };

    beforeEach(async () => {
        localStorage.clear();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        scan = TestBed.inject(ScanService);
    });

    afterEach(() => vi.restoreAllMocks());

    it('asks before scanning the folder browsed', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValue(false);
        TestBed.inject(ListingStore).route.set({ path: 'Media', q: '', sort: '', desc: false });

        await scan.start();

        expect(ask.mock.lastCall![0]).toContain('Scan “Media” and all its sub-folders?');
        expect(api.startScan).not.toHaveBeenCalled();
    });

    it('names the whole library at the root', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValue(false);

        await scan.start();

        expect(ask.mock.lastCall![0]).toContain('the whole library (Root)');
    });

    it('starts the scan and follows it until it completes, then says so', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const show = vi.spyOn(TestBed.inject(ToastService), 'show');
        api.startScan.mockResolvedValue(running());
        api.scanStatus.mockResolvedValueOnce(running()).mockResolvedValueOnce(completed);

        await scan.start();
        expect(scan.running()).toBe(true);
        expect(scan.visible()).toBe(true);
        await settle(1100);

        expect(scan.status()).toEqual(completed);
        expect(show).toHaveBeenCalledWith('Index updated: 2 folders scanned · 5 files indexed');
    });

    it('does not poll a running scan while the page is hidden, and asks again once it shows', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        api.startScan.mockResolvedValue(running());
        api.scanStatus.mockResolvedValue(running());
        try {
            await scan.start();
            await settle(1100);
            const calls = api.scanStatus.mock.calls.length;

            setHidden(true);
            await settle(2300);
            expect(api.scanStatus.mock.calls.length).toBeLessThanOrEqual(calls + 1);
            const hidden = api.scanStatus.mock.calls.length;
            await settle(1100);
            expect(api.scanStatus.mock.calls.length).toBe(hidden);

            api.scanStatus.mockResolvedValue(completed);
            setHidden(false);
            await settle();
            expect(api.scanStatus.mock.calls.length).toBe(hidden + 1);
            expect(scan.status()).toEqual(completed);
        } finally {
            setHidden(false);
        }
    });

    it('says when another scan is already running elsewhere', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const show = vi.spyOn(TestBed.inject(ToastService), 'show');
        TestBed.inject(ListingStore).route.set({ path: 'Media', q: '', sort: '', desc: false });
        api.startScan.mockResolvedValue(running('Other'));

        await scan.start();

        expect(show).toHaveBeenCalledWith('Another scan is already running (Other).');
    });

    it('does not start twice while one runs', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        api.startScan.mockResolvedValue(running());
        await scan.start();

        await scan.start();

        expect(api.startScan).toHaveBeenCalledTimes(1);
    });

    it('reports a scan that could not start', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const error = vi.spyOn(TestBed.inject(ToastService), 'error');
        api.startScan.mockRejectedValue(new ApiError(400, 'Outside the library'));

        await scan.start();

        expect(error).toHaveBeenCalledWith('Scan failed to start: Outside the library');
        expect(scan.busy()).toBe(false);
    });

    it('hides a finished scan once dismissed, until the next one', async () => {
        api.scanStatus.mockResolvedValue(completed);
        scan.watch();
        await settle();
        expect(scan.visible()).toBe(true);

        scan.dismiss();
        expect(scan.visible()).toBe(false);
        expect(localStorage.getItem('sfe.scan.dismissed')).toBe(completed.startedAt);

        scan.status.set({ ...completed, startedAt: '2026-10-11T00:00:00Z' });
        expect(scan.visible()).toBe(true);
    });

    it('never shows the idle state', async () => {
        api.scanStatus.mockResolvedValue(idle);
        scan.watch();
        await settle();

        expect(scan.visible()).toBe(false);
    });

    it('cancels after asking', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
        api.cancelScan.mockRejectedValue(new ApiError(500, 'Boom'));
        api.scanStatus.mockResolvedValue(idle);

        await scan.cancel();
        expect(api.cancelScan).not.toHaveBeenCalled();
        await scan.cancel();

        expect(ask).toHaveBeenCalledTimes(2);
        expect(api.cancelScan).toHaveBeenCalledTimes(1);
    });
});

describe('Dialogs', () => {
    it('closes every open dialog of the page', () => {
        TestBed.configureTestingModule({});
        const open = [document.createElement('dialog'), document.createElement('dialog')];
        const closed = document.createElement('dialog');
        for (const dialog of [...open, closed])
            document.body.append(dialog);
        open.forEach(dialog => dialog.showModal());

        TestBed.inject(Dialogs).closeAll();

        expect(document.querySelectorAll('dialog[open]')).toHaveLength(0);
        [...open, closed].forEach(dialog => dialog.remove());
    });
});

describe('waitForTrickplay', () => {
    let api: ApiMock;
    const manifest = { version: 1 } as never;

    beforeEach(() => {
        vi.useFakeTimers();
        api = mockApi();
    });

    afterEach(() => vi.useRealTimers());

    it('gives the trickplay right away when there is one', async () => {
        api.trickplay.mockResolvedValue(manifest);

        expect(await waitForTrickplay(api as unknown as Api, 'a.mp4', () => true)).toBe(manifest);
        expect(api.trickplayStates).not.toHaveBeenCalled();
    });

    it('gives null when none is coming', async () => {
        api.trickplay.mockRejectedValue(new ApiError(404, 'Not Found'));
        api.trickplayStates.mockResolvedValue({ running: true, files: {} });

        expect(await waitForTrickplay(api as unknown as Api, 'a.mp4', () => true)).toBeNull();
        // Asked once more: it may have just been made.
        expect(api.trickplay).toHaveBeenCalledTimes(2);
    });

    it('asks again while the video waits, telling each time', async () => {
        const waiting = vi.fn();
        api.trickplay.mockRejectedValueOnce(new ApiError(404, 'x')).mockRejectedValueOnce(new ApiError(404, 'x')).mockResolvedValueOnce(manifest);
        api.trickplayStates.mockResolvedValue({ running: true, files: { 'a.mp4': 'pending' } });

        const result = waitForTrickplay(api as unknown as Api, 'a.mp4', () => true, waiting);
        await vi.advanceTimersByTimeAsync(4000);
        await vi.advanceTimersByTimeAsync(4000);

        expect(await result).toBe(manifest);
        expect(waiting).toHaveBeenCalledTimes(2);
    });

    it('stops waiting when the video is no longer the current one', async () => {
        let current = true;
        api.trickplay.mockRejectedValue(new ApiError(404, 'x'));
        api.trickplayStates.mockResolvedValue({ running: true, files: { 'a.mp4': 'running' } });

        const result = waitForTrickplay(api as unknown as Api, 'a.mp4', () => current);
        await vi.advanceTimersByTimeAsync(0);
        current = false;
        await vi.advanceTimersByTimeAsync(4000);

        expect(await result).toBeNull();
        expect(api.trickplay).toHaveBeenCalledTimes(1);
    });

    it('treats a failed state request as nothing coming', async () => {
        api.trickplay.mockRejectedValue(new ApiError(404, 'x'));
        api.trickplayStates.mockRejectedValue(new ApiError(0, 'offline'));

        expect(await waitForTrickplay(api as unknown as Api, 'a.mp4', () => true)).toBeNull();
    });
});
