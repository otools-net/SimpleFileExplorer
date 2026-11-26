import { TestBed } from '@angular/core/testing';
import { Notifications } from '../core/notifications';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { fakeConfig, loadConfig, mockApi, limitedConfig, testProviders } from '../../testing/fakes';
import { TaskEnded, taskEndedMessage, TaskNotices } from './task-notices';

const SINCE = 'sfe.taskSince.1.1';

let notices: TaskNotices;
let notifications: Notifications;

async function setUp(config = fakeConfig()): Promise<void> {
    const api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    notices = TestBed.inject(TaskNotices);
    notifications = TestBed.inject(Notifications);
}

function ended(overrides: Partial<TaskEnded> = {}): TaskEnded {
    return {
        id: 7,
        task: 'scan',
        path: 'Photos',
        outcome: 'completed',
        summary: '300 files indexed.',
        finished: '2026-11-26T10:00:00Z',
        mine: true,
        ...overrides,
    };
}

afterEach(() => vi.restoreAllMocks());

describe('TaskNotices', () => {
    it('gives the feed no time the first time, then the time it kept', async () => {
        await setUp();

        expect(notices.since(new Date('2026-11-26T09:00:00Z'))).toBeNull();
        expect(notices.since()).toBe('2026-11-26T09:00:00.000Z');
    });

    it('tells a run that ended once, with a link to it in the task history', async () => {
        await setUp();
        const show = vi.spyOn(TestBed.inject(ToastService), 'show');

        notices.told(ended());
        notices.told(ended());

        expect(show).toHaveBeenCalledTimes(1);
        const [item] = notifications.list();
        expect(item.message).toBe('The index scan of “Photos” is done. 300 files indexed.');
        expect(item.level).toBe('success');
        expect(item.links).toEqual([
            {
                label: 'Open',
                title: 'Open this run and its log in the task history',
                link: '/admin?section=tasks&page=history&run=7',
            },
        ]);
        expect(localStorage.getItem(SINCE)).toBe('2026-11-26T10:00:00Z');
    });

    it('leaves out what ended before the time kept (told in another tab, or before a reconnection)', async () => {
        await setUp();
        localStorage.setItem(SINCE, '2026-11-26T11:00:00Z');

        notices.told(ended());

        expect(notifications.list()).toEqual([]);
    });

    it('tells a failure as an error, without a link to a history the account may not see', async () => {
        await setUp(limitedConfig([Rights.browse]));

        notices.told(ended({ task: 'similarVideos', path: null, outcome: 'failed', summary: 'ffmpeg is missing.' }));

        const [item] = notifications.list();
        expect(item.message).toBe('The search for similar videos failed. ffmpeg is missing.');
        expect(item.level).toBe('error');
        expect(item.links).toEqual([]);
    });

    it('words each task and outcome in one sentence', () => {
        expect(taskEndedMessage(ended({ path: '' }))).toBe('The index scan of the whole library is done.');
        expect(taskEndedMessage(ended({ task: 'connectionIndex', path: 'Dropbox', outcome: 'cancelled' }))).toBe(
            'Bringing the index of “Dropbox” up to date was cancelled.',
        );
        expect(taskEndedMessage(ended({ task: 'trashPurge', path: null, outcome: 'failed' }))).toBe(
            'The trash purge failed.',
        );
    });
});
