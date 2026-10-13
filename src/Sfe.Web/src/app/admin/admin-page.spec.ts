import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError } from '../core/api.service';
import { ClientConfig, SettingLevel, SettingState, SettingsValues, SettingsView, TaskInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ListingStore } from '../library/listing.store';
import { ApiMock, buttonNamed, adminPageProviders, fakeConfig, loadConfig, mockApi, setMediaQuery, settingsValues, settle, tag } from '../../testing/fakes';
import { ADMIN_GROUPS, ADMIN_SECTIONS } from './admin-sections';

let api: ApiMock;
let harness: RouterTestingHarness;
let element: HTMLElement;

async function setUp(config: Partial<ClientConfig> = {}): Promise<void> {
    localStorage.clear();
    setMediaQuery('(prefers-color-scheme: light)', false);
    setMediaQuery('(max-width: 760px)', false);
    api = mockApi();
    TestBed.configureTestingModule({ providers: adminPageProviders(api) });
    await loadConfig(api, fakeConfig(config));
    api.settings.mockResolvedValue({ values: {}, customized: false });
    harness = await RouterTestingHarness.create();
    element = harness.fixture.nativeElement;
}

async function open(url: string, state?: Record<string, unknown>): Promise<void> {
    await TestBed.inject(Router).navigateByUrl(url, { state });
    await settle();
}

const url = () => TestBed.inject(Router).url;
const heading = () => element.querySelector('.admin-head h2')!.textContent;
const eyebrow = () => element.querySelector('.admin-head .admin-eyebrow')?.textContent ?? null;
/** The links of the sections in the sidebar. */
const navItem = (title: string) => [...element.querySelectorAll<HTMLAnchorElement>('.admin-nav > ul > li > a')].find(a => a.querySelector('span')!.textContent === title)!;
/** The sub-pages listed under the open section. */
const subItems = () => [...element.querySelectorAll<HTMLAnchorElement>('.admin-subnav a')];
const subItem = (title: string) => subItems().find(a => a.querySelector('span')!.textContent === title)!;
const currentSubItem = () => element.querySelector<HTMLAnchorElement>('.admin-subnav a[aria-current=page]')!;
/** The sub-pages as chips (phones). */
const chips = () => [...element.querySelectorAll<HTMLButtonElement>('[role=tab]')];
const selectedChip = () => element.querySelector<HTMLButtonElement>('[role=tab][aria-selected=true]')!;

afterEach(() => vi.restoreAllMocks());

describe('AdminPageComponent', () => {
    beforeEach(() => setUp());

    it('lists its sections by group, and opens on the first sub-page of the first one', async () => {
        await open('/admin');

        expect([...element.querySelectorAll('.admin-nav h2')].map(h => h.textContent)).toEqual(ADMIN_GROUPS.map(g => g.title));
        expect([...element.querySelectorAll('.admin-nav > ul > li > a')].map(a => a.querySelector('span')!.textContent)).toEqual(ADMIN_SECTIONS.map(s => s.title));
        expect(eyebrow()).toBe('Interface');
        expect(heading()).toBe('Appearance');
        expect(navItem('Interface').classList).toContain('open');
        // Only the open section lists its sub-pages, the current one marked.
        expect(subItems().map(a => a.textContent!.trim())).toEqual(['Appearance', 'Browsing', 'Search']);
        expect(currentSubItem().textContent!.trim()).toBe('Appearance');
        expect(navItem('Interface').getAttribute('aria-current')).toBeNull();
        expect(chips()).toHaveLength(0);
        expect(document.title).toBe('Appearance – Interface – Administration – SFE');
    });

    it('lists nothing under a section with a single page', async () => {
        await open('/admin?section=thumbnails');

        expect(heading()).toBe('Thumbnails');
        expect(eyebrow()).toBeNull();
        expect(subItems()).toHaveLength(0);
        expect(navItem('Thumbnails').getAttribute('aria-current')).toBe('page');
        expect(document.title).toBe('Thumbnails – Administration – SFE');
    });

    it('opens the section and sub-page of its address, the first sub-page when unknown', async () => {
        api.tags.mockResolvedValue({ matches: [tag('Cat')], descendants: [] });

        await open('/admin?section=tags&page=nope');

        expect(eyebrow()).toBe('Tags');
        expect(heading()).toBe('All tags');
        expect(subItems().map(a => a.textContent!.trim())).toEqual(['All tags', 'Settings']);
        expect(currentSubItem().textContent!.trim()).toBe('All tags');
        expect(api.tags).toHaveBeenCalledWith({ q: '', limit: 1000 });
        expect(element.querySelector('.tag-row')!.textContent).toContain('Cat');
    });

    it('still opens from its earlier address', async () => {
        await open('/config?section=tags');

        expect(url()).toBe('/admin?section=tags');
        expect(eyebrow()).toBe('Tags');
    });

    it('moves between sections and sub-pages, keeping "back" to where it was opened from', async () => {
        await open('/admin', { sfeFromLibrary: true });

        navItem('Background tasks').click();
        await settle();
        expect(heading()).toBe('Tasks');
        expect(url()).toBe('/admin?section=tasks');

        subItem('History').click();
        await settle();
        expect(heading()).toBe('History');
        expect(url()).toBe('/admin?section=tasks&page=history');
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true }));

        subItem('Tasks').click();
        await settle();
        expect(url()).toBe('/admin?section=tasks');

        const back = vi.spyOn(history, 'back').mockImplementation(() => { });
        element.querySelector<HTMLButtonElement>('[aria-label="Back to the library"]')!.click();
        expect(back).toHaveBeenCalled();
    });

    it('goes to the folder browsed last when not opened from the listing page', async () => {
        TestBed.inject(ListingStore).route.set({ path: 'Movies', q: '', sort: 'size', desc: true });
        await open('/admin');
        const back = vi.spyOn(history, 'back');

        element.querySelector<HTMLButtonElement>('[aria-label="Back to the library"]')!.click();
        await settle();

        expect(back).not.toHaveBeenCalled();
        expect(url()).toBe('/?path=Movies&sort=size&desc=1');
    });

    it('lists the sections on a page of their own on phones, a section showing its sub-pages as chips', async () => {
        setMediaQuery('(max-width: 760px)', true);
        await open('/admin', { sfeFromLibrary: true });

        expect(element.querySelector('.admin-head')).toBeNull();
        expect(element.querySelector('.admin-savebar')).toBeNull();
        expect(element.querySelector('[aria-current]')).toBeNull();
        expect(subItems()).toHaveLength(0);
        expect(navItem('Tags').querySelector('.admin-nav-description')!.textContent).toBe(ADMIN_SECTIONS.find(s => s.id === 'tags')!.description);

        navItem('Duplicates & moves').click();
        await settle();
        expect(heading()).toBe('Duplicates');
        expect(element.querySelector('.admin-nav')).toBeNull();
        expect(url()).toBe('/admin?section=tracking');
        expect(chips().map(c => c.textContent!.trim())).toEqual(['Duplicates', 'Similar videos', 'Moved files', 'Settings']);
        chips().find(c => c.textContent!.trim() === 'Settings')!.click();
        await settle();
        expect(url()).toBe('/admin?section=tracking&page=settings');
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true, sfeFromList: true }));

        const back = vi.spyOn(history, 'back').mockImplementation(() => { });
        element.querySelector<HTMLButtonElement>('[aria-label="Back to the sections"]')!.click();
        expect(back).toHaveBeenCalled();
    });

    it('goes from a section opened by its address to the list of sections on phones', async () => {
        setMediaQuery('(max-width: 760px)', true);
        await open('/admin?section=system');
        const back = vi.spyOn(history, 'back');

        element.querySelector<HTMLButtonElement>('[aria-label="Back to the sections"]')!.click();
        await settle();

        expect(back).not.toHaveBeenCalled();
        expect(url()).toBe('/admin');
        expect(element.querySelector('.admin-nav')).not.toBeNull();
        element.querySelector<HTMLButtonElement>('[aria-label="Back to the library"]')!.click();
        await settle();
        expect(url()).toBe('/');
    });

    it('moves between the chips with the arrow keys on phones, wrapping around', async () => {
        setMediaQuery('(max-width: 760px)', true);
        await open('/admin?section=tracking');
        const press = async (key: string) => {
            element.querySelector('[role=tablist]')!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
            await settle();
        };

        await press('ArrowLeft');
        expect(selectedChip().textContent!.trim()).toBe('Settings');
        await press('ArrowRight');
        expect(selectedChip().textContent!.trim()).toBe('Duplicates');
        await press('End');
        expect(selectedChip().textContent!.trim()).toBe('Settings');
        await press('Home');
        expect(selectedChip().textContent!.trim()).toBe('Duplicates');
        expect(document.activeElement).toBe(selectedChip());
    });

    it('counts the running tasks on their sub-page, and shows the trickplay activity from its card', async () => {
        const running: TaskInfo = {
            task: 'thumbnails', state: 'running', path: null, startedAt: null, progress: null, detail: '', canStart: false, canCancel: true,
            canSchedule: false, automaticNextRun: null, automaticEvery: null, lastRun: null,
        };
        api.tasks.mockResolvedValue({ tasks: [{ ...running, task: 'trickplay', state: 'idle', canStart: true, canSchedule: true }, running], schedules: [], serverTime: '', timeZone: 'UTC', logRetentionDays: 30 });
        api.taskRuns.mockResolvedValue({ runs: [], next: null });
        api.trickplayActivity.mockResolvedValue({ status: { state: 'idle', checked: 0, generated: 0, failed: 0, queued: 0, pending: 0, discovering: false, current: [], lastPassFinished: null }, running: [], queued: [], pending: [], recent: [] });
        await open('/admin?section=tasks');

        expect(currentSubItem().querySelector('.tracking-badge')!.textContent).toBe('1');

        element.querySelector<HTMLButtonElement>('.task-card [aria-label=Details]')!.click();
        await settle();

        expect(eyebrow()).toBe('Trickplay');
        expect(currentSubItem().textContent!.trim()).toBe('Activity');
        expect(element.querySelector('.trickplay-activity')!.textContent).toContain('Waiting.');
    });
});

describe('AdminPageComponent opened directly', () => {
    it('loads the configuration, and says when the server can\'t be reached', async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: adminPageProviders(api) });
        api.config.mockRejectedValue(new ApiError(0, 'The server is not reachable'));
        harness = await RouterTestingHarness.create();
        element = harness.fixture.nativeElement;

        await open('/admin');

        expect(api.config).toHaveBeenCalled();
        expect(element.querySelector('sfe-empty-state')!.textContent).toContain('The server is not reachable');
        expect(element.querySelector('.admin-nav')).toBeNull();
    });
});

describe('Interface › Appearance', () => {
    it('picks this browser\'s theme next to the default one, right away and without saving', async () => {
        await setUp({ theme: 'dark' });
        await open('/admin?section=interface&page=appearance');
        const theme = element.querySelector<HTMLSelectElement>('#browser-theme')!;

        expect(theme.value).toBe('');
        expect(element.querySelector('#setting-appearance-theme')).not.toBeNull();
        theme.value = 'light';
        theme.dispatchEvent(new Event('change'));
        await settle();

        expect(document.documentElement.dataset['theme']).toBe('light');
        expect(localStorage.getItem('sfe.theme')).toBe('light');
        expect(element.querySelector('.admin-savebar')!.classList).not.toContain('changed');
    });
});

describe('SystemSectionComponent', () => {
    const commands = (features: Partial<ClientConfig['features']>) => setUp({ features: { thumbnails: true, trickplay: false, shutdown: false, dismount: false, ...features } });

    it('shows the version, with the commit when known', async () => {
        await setUp({ version: '1.2.3', commit: 'abc1234' });
        await open('/admin?section=system');

        expect(element.querySelector('.system-facts')!.textContent).toContain('SFE 1.2.3');
        expect(element.querySelector('.system-facts code')!.textContent).toBe('abc1234');
        expect(element.querySelector<HTMLAnchorElement>('a[href="api/docs"]')).not.toBeNull();
    });

    it('offers a backup of the databases and configuration files to those with the right', async () => {
        await setUp();
        await open('/admin?section=system');
        const link = element.querySelector<HTMLAnchorElement>('a[href="api/system/backup"]');
        expect(link?.hasAttribute('download')).toBe(true);
        expect(link?.title).toContain('backup');

        TestBed.resetTestingModule();
        await setUp({ rights: [Rights.apiDocs] });
        await open('/admin?section=system');
        expect(element.querySelector('a[href="api/system/backup"]')).toBeNull();
    });

    it('shows the server commands only when they are on', async () => {
        await commands({});
        await open('/admin?section=system');
        expect(buttonNamed(element, 'Shut down server')).toBeUndefined();
        expect(element.textContent).toContain('None configured.');

        TestBed.resetTestingModule();
        await commands({ shutdown: true, dismount: true });
        await open('/admin?section=system');
        expect(buttonNamed(element, 'Shut down server')).toBeDefined();
        expect(buttonNamed(element, 'Dismount drives')).toBeDefined();
    });

    it('runs a server command after asking, with what it answered', async () => {
        await commands({ shutdown: true, dismount: true });
        await open('/admin?section=system');
        const show = vi.spyOn(TestBed.inject(ToastService), 'show');
        vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValue(true);
        api.shutdown.mockResolvedValue({ output: 'Bye' });
        api.dismount.mockResolvedValue({ output: '' });

        buttonNamed(element, 'Shut down server')!.click();
        await settle();
        expect(api.shutdown).not.toHaveBeenCalled();

        buttonNamed(element, 'Shut down server')!.click();
        buttonNamed(element, 'Dismount drives')!.click();
        await settle();
        expect(show).toHaveBeenCalledWith('Shutdown: Bye');
        expect(show).toHaveBeenCalledWith('Dismount requested.');
    });

    it('reports a command that failed', async () => {
        await commands({ shutdown: true });
        await open('/admin?section=system');
        const error = vi.spyOn(TestBed.inject(ToastService), 'error');
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        api.shutdown.mockRejectedValue(new ApiError(409, 'Shutdown is disabled.'));

        buttonNamed(element, 'Shut down server')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('Shutdown failed: Shutdown is disabled.');
    });
});

describe('AdminPageComponent by rights and levels', () => {
    const alice = { id: 2, userName: 'alice', displayName: null, kind: 'user' as const, isServerAdmin: false, local: true };

    /** The settings of a level with the state of some keys (the others editable, from the configuration). */
    function view(level: SettingLevel, states: Record<string, Partial<SettingState>> = {}, values: SettingsValues = {}): SettingsView {
        const keys: Record<string, SettingState> = {};
        for (const [key, state] of Object.entries(states))
            keys[key] = { scope: 'user', source: 'environment', editable: true, lockable: false, locked: false, lockedBy: null, inherited: null, ...state };
        return { values: { ...settingsValues(), ...values }, customized: false, level, keys };
    }


    it('shows only the sections the session has rights for', async () => {
        await setUp({ rights: [Rights.browse, Rights.ownPassword], user: alice });
        await open('/admin');

        expect([...element.querySelectorAll('.admin-nav > ul > li > a')].map(a => a.querySelector('span')!.textContent)).toEqual(['My account']);
        expect(heading()).toBe('My account');
        expect(element.querySelector('#password-current')).not.toBeNull();
    });

    it('says there is nothing to administer without any such right', async () => {
        await setUp({ rights: [Rights.browse, Rights.viewFiles], user: null });
        await open('/admin');

        expect(element.querySelector('sfe-empty-state')!.textContent).toContain('Nothing to administer');
        expect(element.querySelector('.admin-nav')).toBeNull();
    });

    it('shows a section\'s settings to whoever has its right, without the level choice', async () => {
        await setUp({ rights: [Rights.browse, Rights.playbackSettings], user: alice });
        api.settings.mockResolvedValue(view('tenant'));
        await open('/admin');

        expect(heading()).toBe('Playback');
        expect(element.querySelector('.settings-level')).toBeNull();
        expect(api.settings).toHaveBeenCalledWith('tenant');
    });

    it('lets server administrators edit the library\'s settings or the server\'s', async () => {
        await setUp();
        api.settings.mockImplementation(async level => view(level ?? 'server'));
        await open('/admin?section=playback');
        const radios = [...element.querySelectorAll<HTMLInputElement>('.settings-level input')];

        expect(radios.map(radio => radio.checked)).toEqual([true, false]);
        expect(element.querySelector('.settings-level')!.textContent).toContain('this space (Library)');
        expect(api.settings).toHaveBeenLastCalledWith('tenant');

        radios[1].click();
        await settle();

        expect(api.settings).toHaveBeenLastCalledWith('server');
        expect(localStorage.getItem('sfe.settings.level')).toBe('server');
    });

    it('says why a setting cannot change at this level, and locks those it may', async () => {
        await setUp();
        api.settings.mockResolvedValue(view('tenant', {
            'player.seekSeconds': { editable: false, lockedBy: 'environment' },
            'player.loop': { lockable: true },
            'dataServer': { scope: 'server', editable: false },
        }));
        api.saveSettings.mockImplementation(async values => view('tenant', { 'player.loop': { lockable: true } }, values));
        api.saveSettingLocks.mockResolvedValue(view('tenant', { 'player.loop': { lockable: true, locked: true } }));
        await open('/admin?section=playback');
        element.querySelector<HTMLInputElement>('.settings-show-all input')!.click();
        await settle();

        const seek = element.querySelector<HTMLInputElement>('#setting-player-seekSeconds')!;
        expect(seek.disabled).toBe(true);
        expect(seek.closest('.field')!.querySelector('.setting-lock-note')!.textContent).toContain('server\'s configuration');
        expect(element.querySelector('#setting-dataServer')!.closest('.field')!.textContent).toContain('A setting of the whole server');
        const lock = element.querySelector('#setting-player-loop')!.closest('.setting-check')!.querySelector<HTMLInputElement>('.setting-lock input')!;
        expect(element.querySelector('#setting-player-loop')!.closest('.setting-check')!.textContent).toContain('Lock for users');

        lock.click();
        await settle();
        expect(element.querySelector('.admin-savebar-status')!.textContent).toContain('Not saved yet');
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.saveSettings).toHaveBeenCalledWith(expect.anything(), 'tenant');
        expect(api.saveSettingLocks).toHaveBeenCalledWith(['player.loop'], 'tenant');
        expect(lock.checked).toBe(true);
        expect(element.querySelector('.admin-savebar-status')!.textContent).toBe('Following the server\'s settings');
    });
});
