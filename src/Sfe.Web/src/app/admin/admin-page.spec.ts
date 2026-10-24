import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError } from '../core/api.service';
import { ClientConfig, KeepAliveFolder, KeepAliveView, SettingLevel, SettingState, SettingsValues, SettingsView, TaskInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ListingStore } from '../library/listing.store';
import { ContextMenu } from '../menus/context-menu';
import { showChip } from './admin-page.component';
import { ApiMock, buttonNamed, adminPageProviders, deferred, fakeConfig, loadConfig, mockApi, setMediaQuery, settingsValues, settle, tag, type } from '../../testing/fakes';
import { setValue } from '../settings/settings-schema';
import { ADMIN_GROUPS, ADMIN_SECTIONS, groupId, visibleSections } from './admin-sections';

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
/** The breadcrumbs shown (CSS hides the group's on phones, jsdom doesn't). */
const crumbs = () => [...element.querySelectorAll('.admin-crumbs li')].map(li => li.textContent!.trim());
/** The section in the breadcrumbs when the page shown is one of its sub-pages. */
const eyebrow = () => element.querySelector('.admin-crumbs li:nth-child(3) a')?.textContent ?? null;
/** The links of the sections in the sidebar. */
const navItem = (title: string) => [...element.querySelectorAll<HTMLAnchorElement>('.admin-nav > ul > li > a')].find(a => a.querySelector('span')!.textContent === title)!;
/** The sub-pages listed under the open section. */
const subItems = () => [...element.querySelectorAll<HTMLAnchorElement>('.admin-subnav a')];
const subItem = (title: string) => subItems().find(a => a.querySelector('span')!.textContent === title)!;
const currentSubItem = () => element.querySelector<HTMLAnchorElement>('.admin-subnav a[aria-current=page]')!;
/** The sub-pages as chips (phones). */
const chips = () => [...element.querySelectorAll<HTMLButtonElement>('.admin-chips [role=tab]')];
const selectedChip = () => element.querySelector<HTMLButtonElement>('.admin-chips [role=tab][aria-selected=true]')!;

afterEach(() => vi.restoreAllMocks());

describe('AdminPageComponent', () => {
    beforeEach(() => setUp());

    it('lists its sections by group, and opens on their overview, as cards with their sub-pages', async () => {
        await open('/admin');

        expect([...element.querySelectorAll('.admin-nav h2')].map(h => h.textContent)).toEqual(ADMIN_GROUPS.map(g => g.title));
        expect([...element.querySelectorAll('.admin-nav > ul > li > a')].map(a => a.querySelector('span')!.textContent)).toEqual(ADMIN_SECTIONS.map(s => s.title));
        expect(heading()).toBe('Administration');
        expect(element.querySelector('.admin-crumbs')).toBeNull();
        expect(element.querySelector('.admin-nav a.open, .admin-nav [aria-current]')).toBeNull();
        expect(subItems()).toHaveLength(0);
        expect(document.title).toBe('Administration – SFE');

        expect([...element.querySelectorAll('.admin-overview h3')].map(h => h.id)).toEqual(ADMIN_GROUPS.map(g => groupId(g.title)));
        const cards = [...element.querySelectorAll('.admin-card')];
        expect(cards.map(c => c.querySelector('.admin-card-title span')!.textContent)).toEqual(ADMIN_SECTIONS.map(s => s.title));
        const playback = cards.find(c => c.querySelector('.admin-card-title span')!.textContent === 'Playback')!;
        expect(playback.querySelector('p')!.textContent).toBe(ADMIN_SECTIONS.find(s => s.id === 'playback')!.description);
        expect([...playback.querySelectorAll('.admin-card-pages a')].map(a => a.textContent!.trim())).toEqual(['Player', 'Streaming', 'Video streams']);
        // A section with one page has no page links.
        expect(cards.find(c => c.textContent!.includes('Thumbnails'))!.querySelector('.admin-card-pages')).toBeNull();

        [...playback.querySelectorAll<HTMLAnchorElement>('.admin-card-pages a')][1].click();
        await settle();
        expect(url()).toBe('/admin?section=playback&page=streaming');
        expect(heading()).toBe('Streaming');
    });

    it('has a menu of its own (back, reload, its address), and link menus on its links', async () => {
        await open('/admin');
        const menu = TestBed.inject(ContextMenu);

        expect(menu.page()).toMatchObject({ kind: 'page', name: 'Administration' });
        expect(menu.page()!.entries().map(entry => entry.id)).toEqual(['back', 'reload', 'newTab', 'copyLink']);
        // A section's link: the link's menu (the page's menu takes the right clicks nothing else handles).
        const link = navItem(ADMIN_SECTIONS[0].title);
        const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
        Object.defineProperty(event, 'target', { value: link.querySelector('span') });
        menu.fromPage(event);
        expect(menu.open()?.target).toMatchObject({ kind: 'appLink', name: ADMIN_SECTIONS[0].title });
    });

    it('opens on the first sub-page of a section', async () => {
        await open('/admin?section=account');

        expect(ADMIN_SECTIONS[0].id).toBe('account');
        expect(eyebrow()).toBe('My account');
        expect(heading()).toBe('Preferences');
        expect(navItem('My account').classList).toContain('open');
        // Only the open section lists its sub-pages, the current one marked.
        expect(subItems().map(a => a.textContent!.trim())).toEqual(['Preferences', 'Password']);
        expect(currentSubItem().textContent!.trim()).toBe('Preferences');
        expect(navItem('My account').getAttribute('aria-current')).toBeNull();
        expect(chips()).toHaveLength(0);
        expect(document.title).toBe('Preferences – My account – Administration – SFE');
    });

    it('lists nothing under a section with a single page', async () => {
        await open('/admin?section=thumbnails');

        expect(heading()).toBe('Thumbnails');
        expect(eyebrow()).toBeNull();
        expect(crumbs()).toEqual(['Administration', 'Media', 'Thumbnails']);
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

    it('opens a sub-page that moved from its earlier address, in place of it', async () => {
        await open('/admin?section=system&page=streams', { sfeFromLibrary: true });

        expect(url()).toBe('/admin?section=playback&page=streams');
        expect(heading()).toBe('Video streams');
        expect(element.querySelector('sfe-video-streams')).not.toBeNull();
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true }));

        await open('/admin?section=playback&page=settings');
        expect(url()).toBe('/admin?section=playback');
        expect(heading()).toBe('Player');
    });

    it('describes the sub-page shown, the section when it has a single one', async () => {
        const system = ADMIN_SECTIONS.find(s => s.id === 'system')!;
        await open('/admin?section=system&page=maintenance');
        expect(element.querySelector('.admin-head p')!.textContent).toBe(system.subpages.find(p => p.id === 'maintenance')!.description);

        await open('/admin?section=thumbnails');
        expect(element.querySelector('.admin-head p')!.textContent).toBe(ADMIN_SECTIONS.find(s => s.id === 'thumbnails')!.description);
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

    it('follows the sections and sub-pages in its breadcrumbs, which lead back up', async () => {
        await open('/admin?section=account', { sfeFromLibrary: true });
        expect(crumbs()).toEqual(['Administration', 'Account', 'My account', 'Preferences']);
        expect(element.querySelector('.admin-crumbs [aria-current=page]')!.textContent).toBe('Preferences');

        navItem('Background tasks').click();
        await settle();
        subItem('History').click();
        await settle();
        expect(crumbs()).toEqual(['Administration', 'Server', 'Background tasks', 'History']);

        element.querySelector<HTMLAnchorElement>('.admin-crumbs li:nth-child(3) a')!.click();
        await settle();
        expect(url()).toBe('/admin?section=tasks');
        expect(crumbs()).toEqual(['Administration', 'Server', 'Background tasks', 'Tasks']);
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true }));

        const scroll = vi.spyOn(Element.prototype, 'scrollIntoView').mockImplementation(() => { });
        element.querySelector<HTMLAnchorElement>('.admin-crumbs li:nth-child(2) a')!.click();
        await settle();
        expect(url()).toBe('/admin#admin-group-server');
        expect(heading()).toBe('Administration');
        expect(scroll.mock.contexts[0]).toBe(element.querySelector('#admin-group-server'));
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true }));

        navItem('Logs').click();
        await settle();
        element.querySelector<HTMLAnchorElement>('.admin-crumbs li:first-child a')!.click();
        await settle();
        expect(url()).toBe('/admin');
        expect(element.querySelector('.admin-overview')).not.toBeNull();
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true }));
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

    it('goes from its breadcrumbs back to the list of sections on phones', async () => {
        setMediaQuery('(max-width: 760px)', true);
        await open('/admin', { sfeFromLibrary: true });
        expect(element.querySelector('.admin-crumbs')).toBeNull();

        navItem('Duplicates & moves').click();
        await settle();
        chips().find(c => c.textContent!.trim() === 'Moved files')!.click();
        await settle();
        expect(crumbs()).toEqual(['Administration', 'Library', 'Duplicates & moves', 'Moved files']);

        const back = vi.spyOn(history, 'back').mockImplementation(() => { });
        element.querySelector<HTMLButtonElement>('.admin-crumbs button')!.click();
        expect(back).toHaveBeenCalled();
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

describe('AdminPageComponent search', () => {
    beforeEach(() => setUp());

    const box = () => element.querySelector<HTMLInputElement>('.admin-search input')!;
    const results = () => [...element.querySelectorAll<HTMLAnchorElement>('.admin-search-results a')];
    const titles = () => results().map(a => a.querySelector('.admin-search-title')!.textContent!.trim());
    const count = () => element.querySelector('.admin-search-count')!.textContent!.trim();
    async function search(text: string): Promise<void> {
        type(box(), text);
        await settle();
    }
    async function press(key: string): Promise<void> {
        box().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
        await settle();
    }

    it('lists the settings and pages found in place of the sections, the words marked', async () => {
        await open('/admin');

        await search('high RESOLUTION');

        expect(element.querySelector('.admin-nav h2')).toBeNull();
        expect(titles().slice(0, 3)).toEqual(['Mark high and low resolution', 'Photos are high resolution from (pixels)', 'Videos are high resolution from (pixels)']);
        expect(results()[0].querySelector('.admin-search-where')!.textContent).toBe('Interface › Browsing › Resolution and duration');
        expect([...results()[0].querySelectorAll('.admin-search-title mark')].map(m => m.textContent)).toEqual(['high', 'resolution']);
        expect(count()).toBe(`${results().length} found`);

        await search('nothing like this');
        expect(results()).toHaveLength(0);
        expect(count()).toBe('Nothing found');

        await press('Escape');
        expect(box().value).toBe('');
        expect(element.querySelector('.admin-search-results')).toBeNull();
        expect(navItem('Interface')).toBeDefined();
    });

    it('clears the search with its button, shown only while there is text', async () => {
        await open('/admin');
        const clear = () => element.querySelector<HTMLButtonElement>('.admin-search [aria-label="Clear the search"]');
        expect(clear()).toBeNull();

        await search('trickplay');
        clear()!.click();
        await settle();

        expect(box().value).toBe('');
        expect(document.activeElement).toBe(box());
        expect(element.querySelector('.admin-search-results')).toBeNull();
        expect(navItem('Interface')).toBeDefined();
        expect(clear()).toBeNull();
    });

    it('opens a setting found, in the full view when the simple one hides it, and focuses it', async () => {
        await open('/admin', { sfeFromLibrary: true });
        expect(localStorage.getItem('sfe.settings.showAll')).toBeNull();

        await search('largest');
        const largest = results().find(a => a.querySelector('.admin-search-where')!.textContent === 'Interface › Browsing › Listings')!;
        largest.click();
        await settle();

        expect(url()).toBe('/admin?section=interface&page=browsing');
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true }));
        const field = element.querySelector<HTMLInputElement>('#setting-browse-maxPageSize')!;
        expect(field.closest<HTMLElement>('.field')!.hidden).toBe(false);
        expect(document.activeElement).toBe(field);
        expect(field.closest('.field')!.classList).toContain('setting-found');
        // Shown for this once: the browser's choice is not changed.
        expect(localStorage.getItem('sfe.settings.showAll')).toBeNull();
        // The results stay, to open another one.
        expect(titles()).toContain('Largest page');
    });

    it('opens the first page found with Enter', async () => {
        await open('/admin');

        await search('similar');
        await press('Enter');

        expect(url()).toBe('/admin?section=tracking&page=similar');
        expect(heading()).toBe('Similar videos');
    });

    it('finds only in the sections the session has rights for', async () => {
        TestBed.resetTestingModule();
        await setUp({ rights: [Rights.browse, Rights.ownPassword, Rights.ownPreferences], user: { id: 2, userName: 'alice', displayName: null, kind: 'user', isServerAdmin: false, local: true } });
        await open('/admin');

        await search('password');
        expect(titles()).toEqual(['Password', 'My account']);
        await search('resolution');
        expect(results()).toHaveLength(0);
    });

    it('searches from the list of sections on phones', async () => {
        setMediaQuery('(max-width: 760px)', true);
        await open('/admin', { sfeFromLibrary: true });

        await search('trickplay');
        results()[0].click();
        await settle();

        expect(url()).toBe('/admin?section=trickplay');
        expect(TestBed.inject(Location).getState()).toEqual(expect.objectContaining({ sfeFromLibrary: true, sfeFromList: true }));
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
    it('shows the version, with the commit when known', async () => {
        await setUp({ version: '1.2.3', commit: 'abc1234' });
        await open('/admin?section=system');

        expect(element.querySelector('.system-facts')!.textContent).toContain('SFE 1.2.3');
        expect(element.querySelector('.system-facts code')!.textContent).toBe('abc1234');
        expect(element.querySelector<HTMLAnchorElement>('a[href="api/docs"]')).not.toBeNull();
        // Backups are on Maintenance.
        expect(element.querySelector('a[href="api/system/backup"]')).toBeNull();
    });

    it('offers a backup of the databases and configuration files to those with the right', async () => {
        await setUp();
        await open('/admin?section=system&page=maintenance');
        const link = element.querySelector<HTMLAnchorElement>('a[href="api/system/backup"]');
        expect(link?.hasAttribute('download')).toBe(true);
        expect(link?.title).toContain('backup');

        TestBed.resetTestingModule();
        await setUp({ rights: [Rights.apiDocs] });
        await open('/admin?section=system&page=maintenance');
        expect(element.querySelector('a[href="api/system/backup"]')).toBeNull();
    });
});

describe('KeepAliveStatusComponent', () => {
    const view = (overrides: Partial<KeepAliveView> = {}): KeepAliveView => ({ mode: 'auto', intervalSeconds: 240, method: 'list', folders: [], ...overrides });
    const folder = (overrides: Partial<KeepAliveFolder> = {}): KeepAliveFolder => ({
        path: '', isRoot: true, isNetwork: true, lastPing: '2026-10-22T10:00:00Z', durationMilliseconds: 14.4, error: null, failingSince: null, waiting: false,
        method: 'list', ...overrides,
    });
    const text = () => element.querySelector('.keep-alive-status')!.textContent!;

    it('says it is off, and pings the library folder now on request', async () => {
        await setUp();
        api.keepAlive.mockResolvedValue(view({ mode: 'off' }));
        api.pingKeepAlive.mockResolvedValue(view({ mode: 'off', folders: [folder({ isNetwork: false })] }));
        await open('/admin?section=keepAlive');

        expect(eyebrow()).toBe('Keep alive');
        expect(text()).toContain('Off: the library folder is not pinged.');
        expect(text()).toContain('Ping now tries the library folder once.');

        buttonNamed(element, 'Ping now')!.click();
        await settle();

        expect(api.pingKeepAlive).toHaveBeenCalled();
        expect(element.querySelector('.keep-alive-name')!.textContent).toBe('Library folder');
        expect(element.querySelector('.keep-alive-network')!.textContent).toBe('Not on a network share');
        expect(element.querySelector('.keep-alive-detail')!.textContent).toContain('14 ms');
    });

    it('shows the folders pinged, and those failing with why and since when', async () => {
        await setUp();
        api.keepAlive.mockResolvedValue(view({ folders: [folder({ error: 'The network name cannot be found.', failingSince: '2026-10-22T09:00:00Z', durationMilliseconds: null })] }));
        await open('/admin?section=keepAlive&page=status');

        expect(element.querySelector('.settings-status')!.getAttribute('data-state')).toBe('unavailable');
        expect(text()).toContain('1 folder not answering');
        expect(element.querySelector('.keep-alive-row')!.classList).toContain('failed');
        expect(element.querySelector('.keep-alive-error')!.textContent).toContain('The network name cannot be found.');
        expect(element.querySelector('.keep-alive-error')!.textContent).toContain('Failing since');

        TestBed.resetTestingModule();
        await setUp();
        api.keepAlive.mockResolvedValue(view({ folders: [folder()] }));
        await open('/admin?section=keepAlive');
        expect(element.querySelector('.settings-status')!.getAttribute('data-state')).toBe('idle');
        expect(text()).toContain('On for folders on a network share, every 240 seconds.');
    });

    it('lists more folders after the library folder, and says when a file was written', async () => {
        await setUp();
        api.keepAlive.mockResolvedValue(view({ method: 'touch', folders: [folder({ method: 'touch' }), folder({ path: 'NAS/Photos', isRoot: false })] }));
        await open('/admin?section=keepAlive');

        expect([...element.querySelectorAll('.keep-alive-name')].map(n => n.textContent)).toEqual(['Library folder', 'NAS/Photos']);
        const details = [...element.querySelectorAll('.keep-alive-detail')].map(d => d.textContent!);
        expect(details[0]).toContain('file written');
        expect(details[1]).not.toContain('file written');
    });

    it('opens its settings from the status', async () => {
        await setUp();
        api.keepAlive.mockResolvedValue(view());
        await open('/admin?section=keepAlive');

        buttonNamed(element, 'Settings')!.click();
        await settle();

        expect(url()).toBe('/admin?section=keepAlive&page=settings');
        expect(element.textContent).toContain('Keep the library folder alive');
    });

    it('is only for those with its right', () => {
        const pages = (rights: string[]) =>
            visibleSections(wanted => wanted.some(r => rights.includes(r))).find(s => s.id === 'keepAlive')?.subpages.map(p => p.id) ?? [];
        expect(pages([Rights.keepAliveSettings])).toEqual(['status', 'settings']);
        expect(pages([Rights.serverSettings])).toEqual(['settings']);
        expect(pages([Rights.scanningSettings])).toEqual([]);
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

        expect(heading()).toBe('Player');
        expect(element.querySelector('.settings-level')).toBeNull();
        expect(api.settings).toHaveBeenCalledWith('tenant');
    });

    it('lets server administrators edit the library\'s settings or the server\'s', async () => {
        await setUp();
        api.settings.mockImplementation(async level => view(level ?? 'server'));
        await open('/admin?section=playback');
        const tabs = () => [...element.querySelectorAll<HTMLButtonElement>('.settings-level [role=tab]')];

        expect(element.querySelector('.settings-level')!.getAttribute('role')).toBe('tablist');
        expect(tabs().map(tab => tab.getAttribute('aria-selected'))).toEqual(['true', 'false']);
        expect(tabs()[0].textContent).toContain('This space (Library)');
        expect(element.querySelector('sfe-settings-panel')!.getAttribute('aria-labelledby')).toBe('settings-level-tenant');
        expect(api.settings).toHaveBeenLastCalledWith('tenant');

        tabs()[1].click();
        await settle();

        expect(tabs().map(tab => tab.getAttribute('aria-selected'))).toEqual(['false', 'true']);
        expect(api.settings).toHaveBeenLastCalledWith('server');
        expect(localStorage.getItem('sfe.settings.level')).toBe('server');

        tabs()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        await settle();

        expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
        expect(document.activeElement).toBe(tabs()[0]);
        expect(api.settings).toHaveBeenLastCalledWith('tenant');
    });

    describe('changes of both levels', () => {
        const seek = () => element.querySelector<HTMLInputElement>('#setting-player-seekSeconds')!;
        const levelTab = (level: SettingLevel) => element.querySelector<HTMLButtonElement>(`#settings-level-${level}`)!;
        const marked = (level: SettingLevel) => levelTab(level).querySelector('.admin-mark') !== null;
        const status = () => element.querySelector('.admin-savebar-status')!.textContent;
        const fieldError = () => seek().closest('.field')!.querySelector('.field-error')?.textContent ?? null;
        /** The settings with the seek step at `seconds`. */
        const seekOf = (seconds: number) => {
            const values = structuredClone(settingsValues());
            setValue(values, 'player.seekSeconds', seconds);
            return values;
        };
        let views: Record<'server' | 'tenant', SettingsView>;

        async function edit(seconds: string): Promise<void> {
            type(seek(), seconds);
            await settle();
        }

        async function show(level: SettingLevel): Promise<void> {
            levelTab(level).click();
            await settle();
        }

        beforeEach(async () => {
            await setUp();
            views = { server: view('server', {}, seekOf(10)), tenant: view('tenant', {}, seekOf(12)) };
            api.settings.mockImplementation(async level => views[level as 'server' | 'tenant']);
            api.saveSettingChanges.mockImplementation(async (_, level) => views[level as 'server' | 'tenant']);
            await open('/admin?section=playback');
        });

        it('keeps a level\'s changes while the other shows, without asking, and loads it again on return', async () => {
            const ask = vi.spyOn(window, 'confirm');
            await edit('20');

            await show('server');
            expect(ask).not.toHaveBeenCalled();
            expect(seek().value).toBe('10');
            expect(marked('tenant')).toBe(true);
            expect(marked('server')).toBe(false);
            expect(status()).toBe('Not saved yet in This space');

            // The space's settings changed meanwhile (another level's save): the change goes over the new ones.
            api.settings.mockClear();
            views.tenant = view('tenant', {}, seekOf(14));
            await show('tenant');

            expect(api.settings).toHaveBeenCalledWith('tenant');
            expect(seek().value).toBe('20');
            expect(status()).toBe('Not saved yet: Playback › Player');
            expect(api.saveSettingChanges).not.toHaveBeenCalled();
        });

        it('forgets a level\'s kept changes once it shows them unchanged', async () => {
            await edit('20');
            await show('server');
            await show('tenant');
            await edit('12');
            await show('server');

            expect(marked('tenant')).toBe(false);
            expect(buttonNamed(element, 'Save')!.disabled).toBe(true);
        });

        it('saves the changes of both levels in one go, the server\'s first, sending only what changed', async () => {
            await edit('20');
            await show('server');
            await edit('15');
            expect(status()).toBe('Not saved yet: Playback › Player; also in This space');

            buttonNamed(element, 'Save')!.click();
            await settle();

            expect(api.saveSettingChanges.mock.calls).toEqual([
                [{ values: { 'player.seekSeconds': 15 }, lock: [], unlock: [] }, 'server'],
                [{ values: { 'player.seekSeconds': 20 }, lock: [], unlock: [] }, 'tenant'],
            ]);
            expect(levelTab('server').getAttribute('aria-selected')).toBe('true');
            expect(marked('server') || marked('tenant')).toBe(false);
            expect(buttonNamed(element, 'Save')!.disabled).toBe(true);
        });

        it('shows the level whose values are refused, with its errors and changes; the one saved before stays saved', async () => {
            const toast = vi.spyOn(TestBed.inject(ToastService), 'show');
            api.saveSettingChanges.mockImplementation(async (_, level) => {
                if (level === 'tenant')
                    throw new ApiError(400, 'x', { 'player.seekSeconds': ['Too long a step.'] });
                return views.server;
            });
            await edit('20');
            await show('server');
            await edit('15');

            buttonNamed(element, 'Save')!.click();
            await settle();

            expect(levelTab('tenant').getAttribute('aria-selected')).toBe('true');
            expect(seek().value).toBe('20');
            expect(fieldError()).toBe('Too long a step.');
            expect(marked('server')).toBe(false);
            expect(marked('tenant')).toBe(true);
            expect(toast).toHaveBeenCalledWith('Saved the settings of the whole server; those of this space need fixing.');
        });

        it('checks the numbers of the other level before sending anything, and shows them', async () => {
            await edit('2.5');
            await show('server');
            await edit('15');

            buttonNamed(element, 'Save')!.click();
            await settle();

            expect(api.saveSettingChanges).not.toHaveBeenCalled();
            expect(levelTab('tenant').getAttribute('aria-selected')).toBe('true');
            expect(fieldError()).toBe('Enter a whole number.');
            expect(marked('server')).toBe(true);
        });

        it('drops a kept change the level can no longer make (locked above meanwhile)', async () => {
            await edit('20');
            await show('server');
            views.tenant = view('tenant', { 'player.seekSeconds': { editable: false, lockedBy: 'server' } }, seekOf(10));
            await show('tenant');

            expect(seek().value).toBe('10');
            expect(seek().disabled).toBe(true);
            expect(marked('tenant')).toBe(false);
            expect(buttonNamed(element, 'Save')!.disabled).toBe(true);
        });

        it('keeps a kept lock only where the level can still lock', async () => {
            views.tenant = view('tenant', { 'player.loop': { lockable: true } }, seekOf(12));
            await show('server');
            await show('tenant');
            element.querySelector<HTMLInputElement>('.settings-show-all input')!.click();
            await settle();
            const lock = () => element.querySelector('#setting-player-loop')!.closest('.setting-check')!.querySelector<HTMLInputElement>('.setting-lock input');
            lock()!.click();
            await settle();
            await show('server');
            await show('tenant');
            expect(lock()!.checked).toBe(true);

            await show('server');
            views.tenant = view('tenant', { 'player.loop': { lockable: false } }, seekOf(12));
            await show('tenant');
            expect(lock()).toBeNull();
            expect(marked('tenant')).toBe(false);
        });

        it('discards the changes of both levels', async () => {
            await edit('20');
            await show('server');
            await edit('15');

            buttonNamed(element, 'Discard')!.click();
            await settle();
            expect(seek().value).toBe('10');
            await show('tenant');

            expect(seek().value).toBe('12');
            expect(marked('server') || marked('tenant')).toBe(false);
            expect(buttonNamed(element, 'Save')!.disabled).toBe(true);
        });

        it('asks before leaving while only the hidden level has changes', async () => {
            await edit('20');
            await show('server');
            const unload = new Event('beforeunload', { cancelable: true });

            window.dispatchEvent(unload);

            expect(unload.defaultPrevented).toBe(true);
        });

        it('ignores a level\'s answer coming after another level was picked', async () => {
            const late = deferred<SettingsView>();
            api.settings.mockImplementation(level => level === 'server' ? late.promise : Promise.resolve(views.tenant));
            await show('server');
            await show('tenant');

            late.resolve(views.server);
            await settle();

            expect(levelTab('tenant').getAttribute('aria-selected')).toBe('true');
            expect(seek().value).toBe('12');
        });
    });

    it('says why a setting cannot change at this level, and locks those it may', async () => {
        await setUp();
        api.settings.mockResolvedValue(view('tenant', {
            'player.seekSeconds': { editable: false, lockedBy: 'environment' },
            'player.loop': { lockable: true },
            'dataServer': { scope: 'server', editable: false },
        }));
        api.saveSettingChanges.mockResolvedValue(view('tenant', { 'player.loop': { lockable: true, locked: true } }));
        await open('/admin?section=playback&page=streaming');
        element.querySelector<HTMLInputElement>('.settings-show-all input')!.click();
        await settle();
        expect(element.querySelector('#setting-dataServer')!.closest('.field')!.textContent).toContain('A setting of the whole server');
        await open('/admin?section=playback');

        const seek = element.querySelector<HTMLInputElement>('#setting-player-seekSeconds')!;
        expect(seek.disabled).toBe(true);
        expect(seek.closest('.field')!.querySelector('.setting-lock-note')!.textContent).toContain('server\'s configuration');
        const lock = element.querySelector('#setting-player-loop')!.closest('.setting-check')!.querySelector<HTMLInputElement>('.setting-lock input')!;
        expect(element.querySelector('#setting-player-loop')!.closest('.setting-check')!.textContent).toContain('Lock for users');

        lock.click();
        await settle();
        expect(element.querySelector('.admin-savebar-status')!.textContent).toContain('Not saved yet');
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.saveSettingChanges).toHaveBeenCalledWith({ values: {}, lock: ['player.loop'], unlock: [] }, 'tenant');
        expect(lock.checked).toBe(true);
        expect(element.querySelector('.admin-savebar-status')!.textContent).toBe('Following the server\'s settings');
    });
});

describe('showChip (phones: the line of sub-page chips)', () => {
    /** A strip from 0 to 300 px scrolled to `scrollLeft`, and a chip at `left`–`right` on the screen. */
    function strip(left: number, right: number, scrollLeft = 0) {
        const line = document.createElement('div');
        const chip = document.createElement('button');
        line.append(chip);
        line.scrollLeft = scrollLeft;
        line.getBoundingClientRect = () => ({ left: 0, right: 300 }) as DOMRect;
        chip.getBoundingClientRect = () => ({ left, right }) as DOMRect;
        return { line, chip };
    }

    it('scrolls the chip shown into view, with room beside it, and leaves one in view alone', () => {
        const after = strip(320, 400);
        showChip(after.chip);
        expect(after.line.scrollLeft).toBe(112);

        const before = strip(-50, 40, 200);
        showChip(before.chip);
        expect(before.line.scrollLeft).toBe(138);

        const shown = strip(20, 120, 30);
        showChip(shown.chip);
        expect(shown.line.scrollLeft).toBe(30);
    });
});
