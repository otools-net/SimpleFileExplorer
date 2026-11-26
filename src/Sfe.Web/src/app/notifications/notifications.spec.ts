import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { Notification, NOTIFICATION_LIMIT, Notifications } from '../core/notifications';
import { ToastService } from '../core/toast.service';
import { UndoHistory } from '../core/undo-history';
import { folderLink } from '../library/library-route';
import { Popovers } from '../library/topbar/popovers.service';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    LIBRARY,
    loadConfig,
    mockApi,
    setHidden,
    settle,
    testProviders,
} from '../../testing/fakes';
import { NotificationsComponent } from './notifications.component';

const KEY = 'sfe.notifications.1.1';
const DAY = 86_400_000;

let api: ApiMock;
let toasts: ToastService;
let notifications: Notifications;

async function setUp(): Promise<void> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
    toasts = TestBed.inject(ToastService);
    notifications = TestBed.inject(Notifications);
    TestBed.tick();
}

function stored(key = KEY): Notification[] {
    return JSON.parse(localStorage.getItem(key) ?? '[]') as Notification[];
}

function kept(message: string, at: number, overrides: Partial<Notification> = {}): Notification {
    return { id: `${at}`, message, level: 'info', at, read: true, links: [], ...overrides };
}

beforeEach(() => localStorage.clear());
afterEach(() => vi.restoreAllMocks());

describe('Notifications', () => {
    beforeEach(setUp);

    it('keeps every toast, newest first, in this browser for the account and space', () => {
        toasts.show('Renamed to “b.jpg”.');
        toasts.error('Could not move it.');

        expect(notifications.list().map((item) => [item.message, item.level])).toEqual([
            ['Could not move it.', 'error'],
            ['Renamed to “b.jpg”.', 'info'],
        ]);
        expect(stored().map((item) => item.message)).toEqual(['Could not move it.', 'Renamed to “b.jpg”.']);
    });

    it('counts as unread the warnings and errors, and anything that came while the page was hidden', () => {
        toasts.success('Saved.');
        toasts.warning('Saved in part.');
        toasts.error('Failed.');
        setHidden(true);
        try {
            toasts.show('Index updated.');
        } finally {
            setHidden(false);
        }

        expect(notifications.unread()).toBe(3);
        notifications.markAllRead();
        expect(notifications.unread()).toBe(0);
        expect(stored().every((item) => item.read)).toBe(true);
    });

    it('keeps the last 200 of the last 30 days', () => {
        const now = Date.now();
        localStorage.setItem(
            KEY,
            JSON.stringify([
                kept('old', now - 31 * DAY),
                ...Array.from({ length: NOTIFICATION_LIMIT }, (_, i) => kept(`n${i}`, now - DAY - i)),
            ]),
        );
        TestBed.resetTestingModule();
        return setUp().then(() => {
            toasts.show('new');
            const list = notifications.list();
            expect(list).toHaveLength(NOTIFICATION_LIMIT);
            expect(list[0].message).toBe('new');
            expect(list.at(-1)!.message).toBe(`n${NOTIFICATION_LIMIT - 2}`);
            expect(list.some((item) => item.message === 'old')).toBe(false);
        });
    });

    it('keeps the actions while the tab lasts, only those opening a page after a reload', async () => {
        const run = vi.fn();
        const link = folderLink('a/b', 3);
        toasts.show('Moved.', {
            actions: [
                { label: 'Undo', title: 'Undo it.', run },
                { label: 'Open', title: 'Open “b”.', run: vi.fn(), link },
            ],
        });
        expect(notifications.list()[0].actions.map((action) => action.label)).toEqual(['Undo', 'Open']);

        TestBed.resetTestingModule();
        await setUp();

        expect(notifications.list()[0].actions).toEqual([{ label: 'Open', title: 'Open “b”.', link }]);
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        toasts.perform(notifications.list()[0].actions[0]);
        expect(navigate).toHaveBeenCalledWith('/?c=3&path=a%2Fb');
    });

    it('keeps apart what each account sees, and what came before the configuration was known', async () => {
        TestBed.resetTestingModule();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        toasts = TestBed.inject(ToastService);
        notifications = TestBed.inject(Notifications);
        toasts.show('Before.');
        await loadConfig(api, fakeConfig({ user: { ...fakeConfig().user!, id: 7 } }));
        TestBed.tick();

        expect(stored('sfe.notifications.1.7').map((item) => item.message)).toEqual(['Before.']);
        TestBed.inject(ConfigService).clear();
        await loadConfig(api, fakeConfig({ tenant: { ...LIBRARY, id: 2 } }));
        TestBed.tick();
        expect(notifications.list()).toEqual([]);
    });

    it('follows the other tabs of the browser', () => {
        localStorage.setItem(KEY, JSON.stringify([kept('From another tab.', Date.now())]));
        window.dispatchEvent(new StorageEvent('storage', { key: KEY }));

        expect(notifications.list().map((item) => item.message)).toEqual(['From another tab.']);
    });

    it('removes one, or all', () => {
        toasts.show('a');
        toasts.show('b');
        notifications.remove(notifications.list()[0].id);
        expect(notifications.list().map((item) => item.message)).toEqual(['a']);
        notifications.clear();
        expect(stored()).toEqual([]);
    });
});

describe('NotificationsComponent', () => {
    let fixture: ComponentFixture<NotificationsComponent>;
    let element: HTMLElement;

    const bell = () => element.querySelector<HTMLButtonElement>('.notifications-button')!;
    const count = () => element.querySelector('.notifications-count')?.textContent?.trim() ?? '';
    const rows = () => [...element.querySelectorAll('.notification')];

    beforeEach(async () => {
        await setUp();
        fixture = TestBed.createComponent(NotificationsComponent);
        element = fixture.nativeElement as HTMLElement;
        await settle();
    });

    it('shows the unread count on the bell; opening the list reads them, marking those that were unread', async () => {
        toasts.success('Saved.');
        toasts.error('Could not save it.');
        await settle();
        expect(count()).toBe('1');
        expect(bell().getAttribute('aria-label')).toBe('Notifications: 1 unread');

        bell().click();
        await settle();

        expect(count()).toBe('');
        expect(rows().map((row) => row.textContent!.replace(/\s+/g, ' ').trim())).toEqual([
            expect.stringContaining('Error: Could not save it.'),
            expect.stringContaining('Done: Saved.'),
        ]);
        expect(rows().map((row) => row.classList.contains('unread'))).toEqual([true, false]);
        expect(rows()[0].classList).toContain('error');
    });

    it('runs a button, hiding an Undo once done otherwise', async () => {
        const undos = TestBed.inject(UndoHistory);
        const run = vi.fn();
        toasts.success('Renamed.', { action: undos.add({ done: 'Renamed.', hint: 'Undo it.', run }) });
        bell().click();
        await settle();
        expect(buttonNamed(element, 'Undo')).toBeDefined();

        undos.undoLatest();
        await settle();

        expect(run).toHaveBeenCalledTimes(1);
        expect(buttonNamed(element, 'Undo')).toBeUndefined();
    });

    it('opens a link and closes', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        toasts.show('Extracted.', { actions: [{ label: 'Open', title: 'Open it.', link: folderLink('x') }] });
        bell().click();
        await settle();

        buttonNamed(element, 'Open')!.click();
        await settle();

        expect(navigate).toHaveBeenCalledWith('/?path=x');
        expect(TestBed.inject(Popovers).open()).toBeNull();
    });

    it('clears them, closes on Escape and says when there is nothing', async () => {
        toasts.show('a');
        bell().click();
        await settle();
        buttonNamed(element, 'Clear all')!.click();
        await settle();
        expect(element.querySelector('.notifications-empty')?.textContent).toContain('Nothing yet.');

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
        await settle();
        expect(element.querySelector('.notifications-panel')).toBeNull();
    });
});
