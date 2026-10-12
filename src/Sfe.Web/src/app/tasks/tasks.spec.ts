import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { TaskInfo, TaskOverview, TaskRunDetails, TaskRunSummary, TaskSchedule } from '../core/models';
import { RemoveFromIndexDialog } from '../library/scan/remove-from-index.dialog';
import { ApiMock, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { ScheduleFormComponent } from './schedule-form.component';
import { TaskHistoryComponent } from './task-history.component';
import { TasksSectionComponent } from './tasks-section.component';
import { ALL_DAYS, describeSchedule, formatDays, formatInterval, fromLocalInputs, nextRunOf, toLocalInputs } from './tasks';

let api: ApiMock;

beforeEach(async () => {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
});

afterEach(() => vi.restoreAllMocks());

const buttonNamed = (root: HTMLElement, label: string) =>
    [...root.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === label);
const inHours = (hours: number) => new Date(Date.now() + hours * 3600_000).toISOString();

function task(overrides: Partial<TaskInfo> & Pick<TaskInfo, 'task'>): TaskInfo {
    return {
        state: 'idle', path: null, startedAt: null, progress: null, detail: 'Not run yet.', canStart: true, canCancel: false,
        canSchedule: overrides.task !== 'thumbnails', automaticNextRun: null, automaticEvery: null, lastRun: null,
        ...overrides,
    };
}

function schedule(overrides: Partial<TaskSchedule> = {}): TaskSchedule {
    return {
        id: 1, task: 'scan', path: 'Movies', kind: 'weekly', start: '2026-10-01T03:00:00', intervalMinutes: 0, days: ALL_DAYS,
        enabled: true, nextRun: inHours(5), lastRun: null, ...overrides,
    };
}

function run(overrides: Partial<TaskRunSummary> = {}): TaskRunSummary {
    return {
        id: 7, task: 'scan', path: 'Movies', trigger: 'schedule', scheduleId: 1, started: inHours(-2), finished: inHours(-1.9),
        outcome: 'completed', summary: '12 folders scanned, 300 files indexed', ...overrides,
    };
}

function overview(overrides: Partial<TaskOverview> = {}): TaskOverview {
    return {
        tasks: [
            task({ task: 'scan' }),
            task({ task: 'trickplay', automaticNextRun: inHours(2), automaticEvery: 6 }),
            task({ task: 'similarVideos' }),
            task({ task: 'thumbnails', canStart: false, state: 'running', canCancel: true, detail: '2 being rendered, 5 waiting' }),
        ],
        schedules: [],
        serverTime: new Date().toISOString(),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        logRetentionDays: 30,
        ...overrides,
    };
}

describe('task wording', () => {
    it('puts intervals and days into words', () => {
        expect(formatInterval(90)).toBe('every 90 minutes');
        expect(formatInterval(360)).toBe('every 6 hours');
        expect(formatInterval(1440)).toBe('every day');
        expect(formatInterval(2880)).toBe('every 2 days');
        expect(formatDays(ALL_DAYS)).toBe('every day');
        expect(formatDays(0b011_1110)).toBe('on weekdays');
        expect(formatDays(1 << 1)).toMatch(/^on \S+$/);
        expect(describeSchedule(schedule())).toMatch(/^Every day at /);
        expect(describeSchedule(schedule({ kind: 'interval', intervalMinutes: 120 }))).toMatch(/^Every 2 hours, from /);
    });

    it('round-trips local dates and times', () => {
        const iso = fromLocalInputs('2026-11-02', '22:30')!;
        expect(toLocalInputs(iso)).toEqual({ date: '2026-11-02', time: '22:30' });
        expect(fromLocalInputs('2026-11-02', '')).toBeNull();
    });

    it('takes the earliest of the schedules and the automatic run', () => {
        const trickplay = task({ task: 'trickplay', automaticNextRun: inHours(3) });
        const soon = schedule({ id: 2, task: 'trickplay', nextRun: inHours(1) });
        expect(nextRunOf(trickplay, [schedule(), soon])).toEqual({ at: soon.nextRun, schedule: soon });
        expect(nextRunOf(trickplay, [schedule({ task: 'trickplay', nextRun: inHours(4) })])).toEqual({ at: trickplay.automaticNextRun, schedule: null });
        expect(nextRunOf(task({ task: 'scan' }), [])).toBeNull();
    });
});

describe('TasksSectionComponent', () => {
    let fixture: ComponentFixture<TasksSectionComponent>;
    let element: HTMLElement;
    let selected: string[];

    async function create(answer = overview(), tab = 'tasks'): Promise<void> {
        api.tasks.mockResolvedValue(answer);
        api.taskRuns.mockResolvedValue({ runs: [], next: null });
        fixture = TestBed.createComponent(TasksSectionComponent);
        fixture.componentRef.setInput('tab', tab);
        selected = [];
        fixture.componentInstance.selectTab.subscribe(id => selected.push(id));
        element = fixture.nativeElement;
        await settle();
    }

    afterEach(() => fixture?.destroy());

    const card = (name: string) => [...element.querySelectorAll<HTMLElement>('.task-card')].find(c => c.querySelector('h3')!.textContent === name)!;
    const shown = (id: string) => !element.querySelector<HTMLElement>(`#tasks-panel-${id}`)!.hidden;

    it('shows each task with when it runs next, and why', async () => {
        await create(overview({ schedules: [schedule()] }));

        expect([...element.querySelectorAll('.task-card h3')].map(h => h.textContent)).toEqual(['Index scan', 'Trickplay', 'Similar videos', 'Thumbnails']);
        const scan = card('Index scan').querySelector('.task-next')!.textContent!;
        expect(scan).toContain('in 5 hours');
        expect(scan).toContain('schedule: every day at');
        expect(scan).toContain('“Movies”');
        expect(card('Trickplay').querySelector('.task-next')!.textContent).toContain('automatic check, every 6 hours');
        expect(card('Similar videos').querySelector('.task-next')!.textContent).toContain('Not planned');
        // Thumbnails can't be scheduled or started, only stopped.
        expect(card('Thumbnails').querySelector('.task-next')).toBeNull();
        expect(buttonNamed(card('Thumbnails'), 'Run now')).toBeUndefined();
        expect(element.querySelector('.task-summary')!.textContent).toContain('next: trickplay');
    });

    it('shows the tab asked for, and counts what runs and the schedules for the tab bar', async () => {
        await create(overview({ schedules: [schedule()] }), 'schedules');

        expect([shown('tasks'), shown('schedules'), shown('history')]).toEqual([false, true, false]);
        expect(fixture.componentInstance.badges()).toEqual({ tasks: '1', schedules: '1' });

        fixture.componentRef.setInput('tab', 'history');
        await settle();
        expect(shown('history')).toBe(true);
        expect(api.taskRuns).toHaveBeenCalled();
    });

    it('starts and stops tasks', async () => {
        await create();
        api.startTask.mockResolvedValue({ result: 'started', task: task({ task: 'similarVideos', state: 'running' }) });
        api.cancelTask.mockResolvedValue(task({ task: 'thumbnails' }));

        buttonNamed(card('Similar videos'), 'Run now')!.click();
        buttonNamed(card('Thumbnails'), 'Cancel')!.click();
        await settle();

        expect(api.startTask).toHaveBeenCalledWith('similarVideos');
        expect(api.cancelTask).toHaveBeenCalledWith('thumbnails');
    });

    it('asks before cancelling a scan', async () => {
        await create(overview({ tasks: [task({ task: 'scan', state: 'running', canCancel: true, canStart: false })] }));
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);

        buttonNamed(card('Index scan'), 'Cancel')!.click();
        await settle();

        expect(confirm).toHaveBeenCalled();
        expect(api.cancelTask).not.toHaveBeenCalled();
    });

    it('asks for the schedules tab to plan a task, and for the history to show its last run', async () => {
        api.taskRun.mockResolvedValue({ ...run(), log: [], omitted: 0 });
        await create(overview({ tasks: [task({ task: 'scan', lastRun: run() }), task({ task: 'similarVideos' })] }));

        buttonNamed(card('Similar videos'), 'Schedule')!.click();
        card('Index scan').querySelector<HTMLButtonElement>('.task-times .link-button')!.click();
        await settle();

        expect(selected).toEqual(['schedules', 'history']);
        expect(element.querySelector('sfe-schedule-form')).not.toBeNull();
        expect(api.taskRun).toHaveBeenCalledWith(7);
    });

    it('asks for the trickplay activity from the trickplay card', async () => {
        await create();
        const opened = vi.fn();
        fixture.componentInstance.openTrickplay.subscribe(opened);

        card('Trickplay').querySelector<HTMLButtonElement>('[aria-label=Details]')!.click();

        expect(opened).toHaveBeenCalled();
    });

    it('lists the schedules by next run, and turns one off', async () => {
        const later = schedule({ id: 1, nextRun: inHours(9) });
        const sooner = schedule({ id: 2, task: 'similarVideos', path: null, kind: 'interval', intervalMinutes: 360, nextRun: inHours(1) });
        const off = schedule({ id: 3, enabled: false, nextRun: null });
        await create(overview({ schedules: [later, sooner, off] }), 'schedules');
        api.updateSchedule.mockResolvedValue({ ...later, enabled: false });

        const rows = [...element.querySelectorAll<HTMLElement>('.schedule-row')];
        expect(rows.map(r => r.querySelector('.schedule-title strong')!.textContent)).toEqual(['Similar videos', 'Index scan', 'Index scan']);
        expect(rows[2].querySelector('.schedule-next')!.textContent).toContain('Off');

        rows[1].querySelector<HTMLInputElement>('.schedule-toggle input')!.click();
        await settle();
        expect(api.updateSchedule).toHaveBeenCalledWith(1, expect.objectContaining({ enabled: false, task: 'scan' }));
    });
});

describe('ScheduleFormComponent', () => {
    let fixture: ComponentFixture<ScheduleFormComponent>;
    let element: HTMLElement;

    async function create(draft = {}): Promise<void> {
        fixture = TestBed.createComponent(ScheduleFormComponent);
        fixture.componentRef.setInput('draft', draft);
        element = fixture.nativeElement;
        await settle(400);
    }

    const select = (id: string, value: string) => {
        const field = element.querySelector<HTMLSelectElement>(`#${id}`)!;
        field.value = value;
        field.dispatchEvent(new Event('change'));
    };

    it('shows the next runs the server works out, then saves', async () => {
        const runs = [inHours(20), inHours(44)];
        api.previewSchedule.mockResolvedValue({ runs });
        await create({ task: 'trickplay' });

        expect(api.previewSchedule).toHaveBeenLastCalledWith(expect.objectContaining({ task: 'trickplay', kind: 'weekly', days: ALL_DAYS, path: null }));
        expect(element.querySelectorAll('.schedule-preview time')).toHaveLength(2);

        api.createSchedule.mockResolvedValue(schedule({ task: 'trickplay', nextRun: runs[0] }));
        const saved = vi.fn();
        fixture.componentInstance.saved.subscribe(saved);
        buttonNamed(element, 'Save')!.click();
        await settle();
        expect(saved).toHaveBeenCalled();
    });

    it('builds a repeated schedule from its interval, and shows the server\'s errors', async () => {
        api.previewSchedule.mockResolvedValue({ runs: [] });
        await create({ task: 'scan', path: 'Movies' });
        select('schedule-repeat', 'interval');
        await settle();
        const every = element.querySelector<HTMLInputElement>('#schedule-every')!;
        every.value = '2';
        every.dispatchEvent(new Event('input'));
        await settle(400);

        expect(api.previewSchedule).toHaveBeenLastCalledWith(expect.objectContaining({ kind: 'interval', intervalMinutes: 120, path: 'Movies' }));

        api.createSchedule.mockRejectedValue(new ApiError(400, 'Choose a time in the future.', { start: ['Choose a time in the future.'] }));
        buttonNamed(element, 'Save')!.click();
        await settle();
        expect(element.textContent).toContain('Choose a time in the future.');
    });
});

describe('TaskHistoryComponent', () => {
    it('lists runs and opens one on its log', async () => {
        const details: TaskRunDetails = { ...run(), log: [{ time: inHours(-2), level: 'Information', message: 'Scan of /lib/Movies started' }], omitted: 0 };
        api.taskRuns.mockResolvedValue({ runs: [run(), run({ id: 6, task: 'trickplay', path: null, outcome: 'failed', trigger: 'automatic', summary: 'boom' })], next: 5 });
        api.taskRun.mockResolvedValue(details);
        const fixture = TestBed.createComponent(TaskHistoryComponent);
        const element: HTMLElement = fixture.nativeElement;
        await fixture.componentInstance.refresh();
        await settle();

        const rows = [...element.querySelectorAll<HTMLElement>('.task-run')];
        expect(rows).toHaveLength(2);
        expect(rows[0].textContent).toContain('300 files indexed');
        expect(rows[1].querySelector('.task-outcome')!.textContent).toBe('Failed');
        expect(buttonNamed(element, 'Load older runs')).toBeDefined();

        rows[0].querySelector<HTMLButtonElement>('.task-run-head')!.click();
        await settle();
        expect(api.taskRun).toHaveBeenCalledWith(7);
        expect(element.querySelector('.task-run-log')!.textContent).toContain('Scan of /lib/Movies started');
    });
});

describe('RemoveFromIndexDialog', () => {
    it('says how much is indexed, then removes the folder and keeps scans out of it', async () => {
        api.indexedFolder.mockResolvedValue({ path: 'Movies', indexed: true, directories: 3, files: 120, taggedFiles: 4, excluded: false });
        api.removeFromIndex.mockResolvedValue({ path: 'Movies', directories: 3, files: 120, taggedFiles: 4, excluded: true });
        const fixture = TestBed.createComponent(RemoveFromIndexDialog);
        const element: HTMLElement = fixture.nativeElement;
        await fixture.componentInstance.open('Movies');
        await settle();

        expect(element.textContent).toContain('3 folders and 120 files');
        expect(element.textContent).toContain('4 files have tags');
        expect(element.querySelector<HTMLInputElement>('.check input')!.checked).toBe(true);

        buttonNamed(element, 'Remove from index')!.click();
        await settle();
        expect(api.removeFromIndex).toHaveBeenCalledWith('Movies', true);
    });

    it('offers no exclusion for the whole library', async () => {
        api.indexedFolder.mockResolvedValue({ path: '', indexed: true, directories: 10, files: 500, taggedFiles: 0, excluded: false });
        api.removeFromIndex.mockResolvedValue({ path: '', directories: 10, files: 500, taggedFiles: 0, excluded: false });
        const fixture = TestBed.createComponent(RemoveFromIndexDialog);
        const element: HTMLElement = fixture.nativeElement;
        await fixture.componentInstance.open('');
        await settle();

        expect(element.querySelector('.check input')).toBeNull();
        buttonNamed(element, 'Remove from index')!.click();
        await settle();
        expect(api.removeFromIndex).toHaveBeenCalledWith('', false);
    });
});
