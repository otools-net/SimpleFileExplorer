import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { KeepAliveFolder } from '../../admin/admin.models';
import { TaskInfo, TaskRunSummary } from '../../core/task.models';
import { ScanStatus } from '../../library/library.models';
import { ApiMock, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { KeepAliveWidgetComponent } from './keep-alive-widget.component';
import { SCAN_POLL_MS, ScanWidgetComponent } from './scan-widget.component';
import { TasksWidgetComponent } from './tasks-widget.component';

function scan(overrides: Partial<ScanStatus> = {}): ScanStatus {
    return {
        state: 'completed',
        path: 'Photos',
        startedAt: '2026-11-28T10:00:00Z',
        finishedAt: new Date(Date.now() - 3 * 3600_000).toISOString(),
        directoriesScanned: 12,
        directoriesQueued: 0,
        filesIndexed: 340,
        removed: 0,
        errors: 0,
        currentDirectory: null,
        message: null,
        relinked: 0,
        missing: null,
        ...overrides,
    };
}

function task(kind: TaskInfo['task'], overrides: Partial<TaskInfo> = {}): TaskInfo {
    return {
        task: kind,
        state: 'idle',
        path: null,
        startedAt: null,
        progress: null,
        detail: '',
        canStart: true,
        canCancel: false,
        canSchedule: true,
        automaticNextRun: null,
        automaticEvery: null,
        lastRun: null,
        ...overrides,
    };
}

function run(id: number, outcome: TaskRunSummary['outcome'], summary: string | null = null): TaskRunSummary {
    const finished = new Date(Date.now() - 2 * 3600_000).toISOString();
    return {
        id,
        task: 'scan',
        path: null,
        trigger: 'schedule',
        scheduleId: 1,
        started: finished,
        finished,
        outcome,
        summary,
    };
}

function folder(path: string, overrides: Partial<KeepAliveFolder> = {}): KeepAliveFolder {
    return {
        path,
        isRoot: path === '',
        isNetwork: true,
        lastPing: new Date(Date.now() - 60_000).toISOString(),
        durationMilliseconds: 12,
        error: null,
        failingSince: null,
        waiting: false,
        method: 'list',
        ...overrides,
    };
}

describe('administrators dashboard widgets', () => {
    let api: ApiMock;

    async function render(component: Type<unknown>): Promise<HTMLElement> {
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        const fixture = TestBed.createComponent(component);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    beforeEach(() => {
        api = mockApi();
    });

    afterEach(() => {
        TestBed.resetTestingModule();
        vi.useRealTimers();
    });

    describe('index scan', () => {
        it('tells how the last scan ended, its folder opening the listing', async () => {
            api.scanStatus.mockResolvedValue(scan());
            const element = await render(ScanWidgetComponent);

            expect(element.querySelector('.dashboard-scan-state')!.textContent).toBe('Completed 3 hours ago');
            const link = element.querySelector('a')!;
            expect(link.textContent).toBe('“Photos”');
            expect(link.getAttribute('href')).toBe('/?path=Photos');
            expect(element.textContent).toContain('12 folders scanned');
            expect(element.textContent).toContain('340 files indexed');
        });

        it('says a failed scan with its reason, the whole library by its name', async () => {
            api.scanStatus.mockResolvedValue(scan({ state: 'failed', path: '', message: 'Disk gone' }));
            const element = await render(ScanWidgetComponent);

            expect(element.querySelector('.dashboard-scan-state')!.classList).toContain('warning');
            expect(element.querySelector('a')!.textContent).toBe('The whole library (Root)');
            expect(element.querySelector('a')!.getAttribute('href')).toBe('/');
            expect(element.textContent).toContain('Disk gone');
        });

        it('reads a running scan again until it ends', async () => {
            vi.useFakeTimers({ shouldAdvanceTime: true });
            api.scanStatus.mockResolvedValueOnce(scan({ state: 'running', finishedAt: null }));
            api.scanStatus.mockResolvedValue(scan());
            const element = await render(ScanWidgetComponent);
            expect(element.querySelector('.dashboard-scan-state')!.textContent).toBe('Running');

            await vi.advanceTimersByTimeAsync(SCAN_POLL_MS);
            expect(api.scanStatus).toHaveBeenCalledTimes(2);
            await vi.advanceTimersByTimeAsync(SCAN_POLL_MS * 2);
            expect(api.scanStatus).toHaveBeenCalledTimes(2);
        });

        it('says when no scan ran', async () => {
            api.scanStatus.mockResolvedValue(scan({ state: 'idle', finishedAt: null, startedAt: null }));
            const element = await render(ScanWidgetComponent);
            expect(element.textContent).toContain('No scan since the server started');
        });
    });

    describe('background tasks', () => {
        it('lists the tasks running, then those whose last run went wrong, each opening its page', async () => {
            api.tasks.mockResolvedValue({
                tasks: [
                    task('scan', { lastRun: run(7, 'failed', 'Disk gone') }),
                    task('trickplay', { state: 'running', progress: 0.4, detail: '12 of 30 videos' }),
                    task('trashPurge', { lastRun: run(8, 'completed') }),
                    task('similarVideos', { lastRun: run(9, 'interrupted') }),
                ],
                schedules: [],
                serverTime: '',
                timeZone: 'UTC',
                logRetentionDays: 30,
            });
            const element = await render(TasksWidgetComponent);

            const lines = [...element.querySelectorAll<HTMLElement>('.dashboard-task')];
            expect(lines.map((line) => line.dataset['task'])).toEqual(['trickplay', 'scan', 'similarVideos']);
            expect(lines[0].querySelector('[role="progressbar"]')!.getAttribute('aria-valuenow')).toBe('40');
            expect(lines[0].textContent).toContain('12 of 30 videos');
            expect(lines[0].querySelector('a')!.getAttribute('href')).toBe('/admin?section=tasks&page=tasks');
            expect(lines[1].classList).toContain('warning');
            expect(lines[1].textContent).toContain('Failed 2 hours ago · Disk gone');
            expect(lines[1].querySelector('a')!.getAttribute('href')).toBe('/admin?section=tasks&page=history&run=7');
            expect(lines[2].textContent).toContain('Interrupted');
        });

        it('says when all is well', async () => {
            api.tasks.mockResolvedValue({
                tasks: [task('scan', { lastRun: run(1, 'completed') })],
                schedules: [],
                serverTime: '',
                timeZone: 'UTC',
                logRetentionDays: 30,
            });
            const element = await render(TasksWidgetComponent);
            expect(element.textContent).toContain('Nothing running, and no last run went wrong');
        });
    });

    describe('keep alive', () => {
        it('lists the folders not answering, with why', async () => {
            api.keepAlive.mockResolvedValue({
                mode: 'always',
                intervalSeconds: 60,
                method: 'list',
                folders: [folder(''), folder('Archive', { error: 'The network path was not found.' })],
            });
            const element = await render(KeepAliveWidgetComponent);

            const link = element.querySelector('a')!;
            expect(link.textContent).toBe('1 folder not answering');
            expect(link.classList).toContain('warning');
            expect(link.getAttribute('href')).toBe('/admin?section=keepAlive&page=status');
            const failing = element.querySelectorAll<HTMLElement>('.dashboard-keep-alive');
            expect([...failing].map((line) => line.dataset['folder'])).toEqual(['Archive']);
            expect(failing[0].textContent).toContain('The network path was not found.');
        });

        it('tells when it last pinged, or that it is off', async () => {
            api.keepAlive.mockResolvedValue({
                mode: 'auto',
                intervalSeconds: 60,
                method: 'list',
                folders: [folder('')],
            });
            let element = await render(KeepAliveWidgetComponent);
            expect(element.textContent).toContain('On for folders on a network share.');
            expect(element.textContent).toContain('Last ping 1 minute ago');

            TestBed.resetTestingModule();
            api.keepAlive.mockResolvedValue({
                mode: 'off',
                intervalSeconds: 60,
                method: 'list',
                folders: [folder('')],
            });
            element = await render(KeepAliveWidgetComponent);
            expect(element.textContent).toContain("Off: the library folder isn't pinged.");
            expect(element.textContent).not.toContain('Last ping');
        });
    });
});
