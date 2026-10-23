import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { ClientConfig, DirectoryListing, LibraryItem, SessionInfo } from '../../core/models';
import { Rights } from '../../core/rights';
import { ApiMock, fakeConfig, item, LIBRARY, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ChangeFeed } from '../change-feed.service';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { LocationComponent } from '../location/location.component';
import { RemoveFromIndexDialog } from '../scan/remove-from-index.dialog';
import type { ScopeTagsDialog } from '../../tags/scope-tags.dialog';
import { ScanService } from '../scan/scan.service';
import { SidebarService } from '../sidebar/sidebar.service';
import { MainMenuComponent } from './main-menu.component';
import { Popovers } from './popovers.service';

let api: ApiMock;
let store: ListingStore;
let navigate: ReturnType<typeof vi.spyOn>;

async function setUp(config: Partial<ClientConfig> = {}): Promise<void> {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, fakeConfig(config));
    store = TestBed.inject(ListingStore);
    navigate = vi.spyOn(store, 'navigate').mockImplementation(() => { });
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
}

afterEach(() => vi.restoreAllMocks());

describe('LocationComponent', () => {
    let fixture: ComponentFixture<LocationComponent>;
    let element: HTMLElement;

    const crumbs = () => [...element.querySelectorAll('.crumbs > a, .crumbs > span')].map(c => c.textContent!.trim());
    const nav = () => [...element.querySelectorAll<HTMLAnchorElement>('.folder-nav a')];
    const summary = () => element.querySelector('.summary')!.textContent!;
    const sortOptions = () => [...element.querySelectorAll<HTMLOptionElement>('#sort-select option')].map(o => o.value);

    async function show(path: string, q = '', items: LibraryItem[] = [], page: Partial<DirectoryListing> = {}): Promise<void> {
        store.route.set({ path, q, sort: '', desc: false });
        if (q)
            api.search.mockResolvedValue({ query: q, path, items, limit: 50, hasMore: false, next: null });
        else
            api.browse.mockResolvedValue({ path, name: '', parentPath: null, items, offset: 0, limit: 100, total: items.length, directoryCount: 0, fileCount: 0, ...page });
        await store.load();
        fixture ??= TestBed.createComponent(LocationComponent);
        element = fixture.nativeElement;
        await settle();
    }

    beforeEach(async () => {
        await setUp({ rootName: 'Library' });
        api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
        fixture = undefined!;
    });

    it('shows the root and every folder down to the current one', async () => {
        await show('Media/2020/Summer');

        expect(crumbs()).toEqual(['Library', 'Media', '2020', 'Summer']);
        expect(element.querySelector('.crumbs [aria-current=page]')!.textContent).toBe('Summer');
    });

    it('adds the search after the folder searched in, which stays a link', async () => {
        await show('Media', 'beach');

        expect(crumbs()).toEqual(['Library', 'Media', 'Search “beach”']);
        expect(element.querySelector('.crumbs a:last-of-type')!.textContent).toBe('Media');
    });

    it('opens the tags of the folder browsed, or of the results of the search', async () => {
        const open = vi.fn();
        TestBed.inject(Dialogs).scopeTags.set({ open } as unknown as ScopeTagsDialog);
        const button = () => element.querySelector<HTMLButtonElement>('.scope-tags-button')!;

        await show('Media');
        button().click();
        expect(open).toHaveBeenLastCalledWith({ path: 'Media', recursive: false, query: null });

        await show('Media', 'beach');
        expect(button().title).toContain('all the results of this search');
        button().click();
        expect(open).toHaveBeenLastCalledWith({ path: 'Media', recursive: false, query: 'beach' });
    });

    it('sums up a folder and a search', async () => {
        await show('Media', '', [], { directoryCount: 2, fileCount: 1 });
        expect(summary()).toBe('2 folders · 1 file');

        await show('Media', 'x', [item('a.jpg'), item('b.jpg')]);
        expect(summary()).toBe('2 results in Media');
    });

    it('offers the parent, previous and next folders, disabled at the ends', async () => {
        api.siblings.mockResolvedValue({ path: 'Media/B', previous: item('Media/A', { kind: 'directory' }), next: null, position: 2, count: 2 });
        await show('Media/B');

        expect(nav().map(a => a.title)).toEqual(['Parent folder: Media (Backspace)', 'Previous folder: A (2 of 2)', 'Last folder']);
        expect(nav().map(a => a.getAttribute('aria-disabled'))).toEqual(['false', 'false', 'true']);
    });

    it('names the root as the parent of a top folder, and has no folder links at the root or in a search', async () => {
        await show('Media');
        expect(nav()[0].title).toBe('Parent folder: Library (Backspace)');

        await show('');
        expect(nav()).toEqual([]);
        await show('Media', 'x');
        expect(nav()).toEqual([]);
    });

    it('offers the path, duration and resolution orders in searches only', async () => {
        await show('Media');
        expect(sortOptions()).toEqual(['name', 'modified', 'size', 'type']);

        await show('Media', 'x');
        expect(sortOptions()).toEqual(['name', 'modified', 'size', 'type', 'duration', 'resolution', 'path']);
    });

    it('leaves the default sort out of the URL, and flips the direction', async () => {
        await show('Media');
        const select = element.querySelector<HTMLSelectElement>('#sort-select')!;

        select.value = 'size';
        select.dispatchEvent(new Event('change'));
        expect(navigate).toHaveBeenLastCalledWith({ sort: 'size' }, { replace: true });
        select.value = 'name';
        select.dispatchEvent(new Event('change'));
        expect(navigate).toHaveBeenLastCalledWith({ sort: '' }, { replace: true });

        element.querySelector<HTMLButtonElement>('.sort-direction')!.click();
        expect(navigate).toHaveBeenLastCalledWith({ desc: true }, { replace: true });
    });

    it('copies the folder\'s full path only when the server allows it', async () => {
        await show('Media');
        expect(element.querySelector('.copy-folder-path')).toBeNull();

        await loadConfig(api, fakeConfig({ rootPath: '/srv/' }));
        await settle();
        expect(element.querySelector('.copy-folder-path')).not.toBeNull();
    });
});

describe('MainMenuComponent', () => {
    let fixture: ComponentFixture<MainMenuComponent>;
    let element: HTMLElement;

    const entry = (text: string) => [...element.querySelectorAll<HTMLButtonElement>('[role=menuitem]')].find(e => e.textContent!.trim().startsWith(text));

    async function create(config: Partial<ClientConfig> = {}): Promise<void> {
        await setUp(config);
        fixture = TestBed.createComponent(MainMenuComponent);
        element = fixture.nativeElement;
        await settle();
    }

    it('opens and closes from its button', async () => {
        await create();
        const toggle = element.querySelector<HTMLButtonElement>('[aria-label=More]')!;

        toggle.click();
        await settle();
        expect(element.querySelector<HTMLElement>('#menu')!.hidden).toBe(false);
        expect(toggle.getAttribute('aria-expanded')).toBe('true');

        toggle.click();
        await settle();
        expect(element.querySelector<HTMLElement>('#menu')!.hidden).toBe(true);
    });

    it('offers the scans, the administration page, full screen and signing out', async () => {
        await create();

        expect([...element.querySelectorAll('[role=menuitem]')].map(e => e.textContent!.trim().split(/\s+/)[0]))
            .toEqual(['admin', 'Scan', 'Remove', 'Administration', 'Toggle', 'Sign']);
        expect(element.querySelector('.menu-account')!.textContent).toContain('admin');
        expect(element.querySelector('.menu-account small')!.textContent).toBe('Library');
    });

    it('offers a visitor without an account only full screen and signing in', async () => {
        await create({ rights: [Rights.browse, Rights.viewFiles, Rights.viewTags], user: null, tenants: [] });

        expect([...element.querySelectorAll('[role=menuitem]')].map(e => e.textContent!.trim())).toEqual(['Toggle fullscreen', 'Sign in']);
        expect(element.querySelector('.menu-account')!.textContent).toContain('Guest');
    });

    it('shows the administration page to an account with one of its rights only', async () => {
        await create({ rights: [Rights.browse, Rights.ownPassword] });

        expect(entry('Administration')).toBeDefined();
        expect(entry('Scan this folder')).toBeUndefined();
    });

    it('switches to the account\'s other libraries, starting the page again there', async () => {
        await create({ tenants: [LIBRARY, { id: 2, slug: 'films', name: 'Films' }] });
        api.switchTenant.mockResolvedValue({} as SessionInfo);
        const assign = vi.fn();
        vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, assign } as Location);

        entry('Films')!.click();
        await settle();

        expect(api.switchTenant).toHaveBeenCalledWith('films');
        expect(assign).toHaveBeenCalledWith(document.baseURI);
        expect(entry('Library')).toBeUndefined();
        vi.restoreAllMocks();
    });

    it('signs out, then shows the sign-in page', async () => {
        await create();
        api.signOut.mockResolvedValue({} as SessionInfo);
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        entry('Sign out')!.click();
        await settle();

        expect(api.signOut).toHaveBeenCalled();
        expect(navigate).toHaveBeenCalledWith(['/login']);
        expect(TestBed.inject(ConfigService).config()).toBeNull();
    });

    it('toggles full screen, closing first', async () => {
        await create();
        const request = vi.fn(async () => { });
        document.documentElement.requestFullscreen = request;
        TestBed.inject(Popovers).toggle('menu');

        entry('Toggle fullscreen')!.click();

        expect(request).toHaveBeenCalled();
        expect(TestBed.inject(Popovers).open()).toBeNull();
        delete (document.documentElement as Partial<HTMLElement>).requestFullscreen;
    });

    it('links to the administration page, closing first, so that "back" returns here', async () => {
        await create();
        TestBed.inject(Popovers).toggle('menu');
        await settle();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        const link = entry('Administration') as unknown as HTMLAnchorElement;

        expect(link.getAttribute('href')).toBe('/admin');
        link.click();
        await settle();

        expect(TestBed.inject(Popovers).open()).toBeNull();
        expect(String(navigate.mock.lastCall![0])).toBe('/admin');
        expect(navigate.mock.lastCall![1]).toEqual(expect.objectContaining({ state: { sfeFromLibrary: true } }));
    });

    it('links the account line to Administration › My account, closing first', async () => {
        await create();
        TestBed.inject(Popovers).toggle('menu');
        await settle();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        const link = element.querySelector<HTMLAnchorElement>('a.menu-account')!;

        expect(link.getAttribute('href')).toBe('/admin?section=account');
        link.click();
        await settle();

        expect(TestBed.inject(Popovers).open()).toBeNull();
        expect(String(navigate.mock.lastCall![0])).toBe('/admin?section=account');
        expect(navigate.mock.lastCall![1]).toEqual(expect.objectContaining({ state: { sfeFromLibrary: true } }));
    });

    it('keeps the account line plain without the rights of My account', async () => {
        await create({ rights: [Rights.browse, Rights.scan] });

        expect(element.querySelector('a.menu-account')).toBeNull();
        expect(element.querySelector('p.menu-account')!.textContent).toContain('admin');
    });

    it('closes before opening the removal from the index', async () => {
        await create();
        const removal = { open: vi.fn() };
        TestBed.inject(Dialogs).removeFromIndex.set(removal as unknown as RemoveFromIndexDialog);
        TestBed.inject(Popovers).toggle('menu');

        entry('Remove from index')!.click();

        expect(removal.open).toHaveBeenCalled();
        expect(TestBed.inject(Popovers).open()).toBeNull();
    });

    it('shows the version, with the commit when known', async () => {
        await create({ version: '1.2.3', commit: 'abc1234' });

        const version = element.querySelector<HTMLElement>('.menu-version')!;
        expect(version.textContent).toBe('SFE 1.2.3');
        expect(version.title).toBe('Version 1.2.3, commit abc1234');
    });

    it('disables the scan while one runs', async () => {
        await create();
        api.scanStatus.mockResolvedValue({ state: 'running', path: '', startedAt: 'x', finishedAt: null, directoriesScanned: 0, directoriesQueued: 0, filesIndexed: 0, removed: 0, errors: 0, currentDirectory: null, message: null, relinked: 0, missing: null });
        TestBed.inject(ScanService).watch();
        await settle();

        expect(entry('Scan this folder')!.disabled).toBe(true);
        expect(entry('Scan this folder')!.title).toBe('A scan is in progress');
    });
});

describe('MainMenuComponent keep alive', () => {
    let fixture: ComponentFixture<MainMenuComponent>;
    let element: HTMLElement;

    async function create(state: 'none' | 'ok' | 'failing', config: Partial<ClientConfig> = {}): Promise<void> {
        await setUp(config);
        TestBed.inject(ChangeFeed).keepAlive.set(state);
        fixture = TestBed.createComponent(MainMenuComponent);
        element = fixture.nativeElement;
        await settle();
    }

    const button = () => element.querySelector<HTMLButtonElement>('button[aria-haspopup=menu]')!;
    const badge = () => button().querySelector('.menu-keep-alive');
    const line = () => element.querySelector('.menu-keep-alive-line');

    it('shows nothing while keep alive is off', async () => {
        await create('none');

        expect(badge()).toBeNull();
        expect(line()).toBeNull();
        expect(button().title).toBe('Scan this folder, administration, your account and more');
    });

    it('shows a green dot while the library folder answers', async () => {
        await create('ok');

        expect(badge()!.classList).not.toContain('failing');
        expect(badge()!.textContent).toBe('');
        expect(button().title).toContain('Network folder answering');
        expect(button().getAttribute('aria-label')).toBe('More');
        expect(line()!.textContent).toContain('Network folder answering');
    });

    it('shows a yellow ! when it does not, linking to its status for those with the right', async () => {
        await create('failing');

        expect(badge()!.classList).toContain('failing');
        expect(badge()!.textContent).toBe('!');
        expect(button().getAttribute('aria-label')).toBe('More (network folder not answering)');
        expect(line()!.tagName).toBe('A');
        expect(line()!.getAttribute('href')).toBe('/admin?section=keepAlive');
        expect(line()!.textContent).toContain('Network folder not answering');
    });

    it('follows the feed, and is a plain line without the right', async () => {
        await create('ok', { rights: [Rights.browse] });
        expect(line()!.tagName).toBe('P');

        TestBed.inject(ChangeFeed).keepAlive.set('failing');
        await settle();
        expect(badge()!.textContent).toBe('!');
    });

    it('shows a yellow ! and a line of its own while the server does not answer, even with keep alive off', async () => {
        await create('none');
        const feed = TestBed.inject(ChangeFeed);
        feed.serverLost.set(true);
        await settle();

        expect(badge()!.classList).toContain('failing');
        expect(badge()!.textContent).toBe('!');
        expect(button().getAttribute('aria-label')).toBe('More (server not answering)');
        expect(button().title).toContain('Server not answering');
        expect(line()!.textContent).toContain('Server not answering');
        expect(element.querySelectorAll('.menu-keep-alive-line')).toHaveLength(1);

        // Back: as before (nothing, keep alive being off).
        feed.serverLost.set(false);
        await settle();
        expect(badge()).toBeNull();
        expect(line()).toBeNull();
    });
});
