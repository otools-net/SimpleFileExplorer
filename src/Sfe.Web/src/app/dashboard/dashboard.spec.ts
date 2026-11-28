import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Api } from '../core/api.service';
import { ClientConfig } from '../core/config.models';
import { Rights } from '../core/rights';
import { ALL_RIGHTS, ApiMock, fakeConfig, limitedConfig, mockApi, settle } from '../../testing/fakes';
import { HomeFolder, QuickLink } from '../library/library.models';
import { DashboardPageComponent } from './dashboard-page.component';
import { arrangeLayout, DEFAULT_LAYOUT } from './dashboard-layout';
import { resetStartPage, startPage, takeStartConfig } from './start-page';

@Component({ selector: 'sfe-listing-stand-in', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class ListingStandInComponent {}

const HOME: HomeFolder = {
    path: 'Homes/tom',
    name: 'tom',
    exists: true,
    chosen: true,
    group: null,
    groupPath: null,
};

const LINKS: QuickLink[] = [
    { path: 'Photos', name: 'Photos', kind: 'directory', extension: null, exists: true },
    { path: 'Docs/plan.pdf', name: 'plan.pdf', kind: 'file', extension: 'pdf', exists: true },
    { path: 'Gone', name: 'Gone', kind: 'directory', extension: null, exists: false },
];

describe('dashboard', () => {
    let api: ApiMock;

    function setUp(config: ClientConfig): void {
        api = mockApi();
        api.config.mockResolvedValue(config);
        api.homeFolder.mockResolvedValue(HOME);
        api.quickLinks.mockResolvedValue(LINKS);
        api.storage.mockResolvedValue({ reported: true, used: 90, total: 100, free: 10 });
        api.uploads.mockResolvedValue([]);
        api.browse.mockResolvedValue({
            path: '',
            name: '',
            parentPath: null,
            items: [],
            offset: 0,
            limit: 13,
            total: 0,
            directoryCount: 0,
            fileCount: 0,
        });
        api.tagSidebar.mockResolvedValue({ popular: [], searched: [], viewed: [], lucky: [], historyEnabled: true });
        TestBed.configureTestingModule({
            providers: [
                { provide: Api, useValue: api },
                provideRouter([
                    { path: '', component: ListingStandInComponent, canActivate: [startPage] },
                    { path: 'dashboard', component: DashboardPageComponent },
                ]),
            ],
        });
    }

    /** Opens `url` as the app's first navigation, from a fresh history entry unless `inApp`. */
    async function openFirst(url: string, inApp = false): Promise<string> {
        resetStartPage(inApp);
        const router = TestBed.inject(Router);
        await router.navigateByUrl(url);
        await settle();
        return router.url;
    }

    afterEach(() => {
        resetStartPage(true);
        TestBed.resetTestingModule();
    });

    describe('layout', () => {
        const can = (rights: string[]) => (right: string) => rights.includes(right);

        it('puts the default widgets in their columns for whoever may see them', () => {
            const config = fakeConfig({ connections: { levels: ['account'], kinds: [] } });
            const all = arrangeLayout(DEFAULT_LAYOUT, can(ALL_RIGHTS), config);
            expect(all[1].map((widget) => widget.id)).toEqual(['storage', 'uploads', 'connections', 'tags']);

            const columns = arrangeLayout(DEFAULT_LAYOUT, can(ALL_RIGHTS));
            expect(columns.map((column) => column.map((widget) => widget.id))).toEqual([
                ['files', 'places', 'notifications'],
                ['storage', 'uploads', 'tags'],
            ]);
        });

        it('leaves out unknown widgets, those the session may not see, and repeats', () => {
            const layout = { columns: [['later', 'places', 'storage'], ['places'], ['storage']] };
            const columns = arrangeLayout(layout, can([Rights.quickLinks]));
            expect(columns.map((column) => column.map((widget) => widget.id))).toEqual([['places'], []]);
        });

        it('puts the columns beyond the last one at its end', () => {
            const columns = arrangeLayout({ columns: [[], [], ['storage', 'places']] }, can(ALL_RIGHTS));
            expect(columns.map((column) => column.map((widget) => widget.id))).toEqual([[], ['storage', 'places']]);
        });
    });

    describe('start page', () => {
        it('opens the dashboard at the address alone', async () => {
            setUp(fakeConfig({ startPage: 'dashboard' }));
            expect(await openFirst('/')).toBe('/dashboard');
            expect(api.config).toHaveBeenCalledTimes(1);
        });

        it('opens the home folder when chosen', async () => {
            setUp(fakeConfig({ startPage: 'home' }));
            expect(await openFirst('/')).toBe('/?path=Homes%2Ftom');
        });

        it('opens the top of the library when the home folder is not there', async () => {
            setUp(fakeConfig({ startPage: 'home' }));
            api.homeFolder.mockResolvedValue({ ...HOME, exists: false });
            expect(await openFirst('/')).toBe('/');
        });

        it('keeps the top of the library, and hands the listing the configuration it loaded', async () => {
            setUp(fakeConfig({ startPage: 'root' }));
            expect(await openFirst('/')).toBe('/');
            expect(takeStartConfig()?.startPage).toBe('root');
            expect(takeStartConfig()).toBeNull();
        });

        it('keeps a folder, a reload of the app, and later navigations', async () => {
            setUp(fakeConfig({ startPage: 'dashboard' }));
            expect(await openFirst('/?path=Photos')).toBe('/?path=Photos');
            expect(await openFirst('/', true)).toBe('/');

            await TestBed.inject(Router).navigateByUrl('/dashboard');
            await TestBed.inject(Router).navigateByUrl('/');
            expect(TestBed.inject(Router).url).toBe('/');
            expect(api.config).not.toHaveBeenCalled();
        });
    });

    describe('page', () => {
        async function openDashboard(config: ClientConfig): Promise<HTMLElement> {
            setUp(config);
            const harness = await RouterTestingHarness.create();
            await harness.navigateByUrl('/dashboard');
            const element = harness.routeNativeElement!;
            // The cards' code loads apart (a dynamic import each): until every card shows its content.
            await vi.waitFor(
                () => {
                    harness.detectChanges();
                    expect(element.querySelector('main.dashboard')).not.toBeNull();
                    expect(element.querySelector('.dashboard-card .spinner, .storage-widget .spinner')).toBeNull();
                },
                { timeout: 5000 },
            );
            return element;
        }

        const cards = (element: HTMLElement) =>
            [...element.querySelectorAll<HTMLElement>('.dashboard-card')].map((card) => card.dataset['widget']);

        it('shows the home folder, the quick links and the storage', async () => {
            const element = await openDashboard(fakeConfig());

            expect(cards(element)).toEqual(['files', 'places', 'notifications', 'storage', 'uploads', 'tags']);
            const places = element.querySelector('[data-widget="places"]')!;
            expect([...places.querySelectorAll('.quick-link-name')].map((name) => name.textContent)).toEqual([
                'Home',
                'Photos',
                'plan.pdf',
                'Gone',
            ]);
            const hrefs = [...places.querySelectorAll('a')].map((link) => link.getAttribute('href'));
            expect(hrefs).toEqual(['/?path=Homes%2Ftom', '/?path=Photos', '/?path=Docs', '/?path=Gone']);
            expect(places.querySelector('[data-quick-link="Gone"]')!.classList).toContain('missing');

            const storage = element.querySelector('[data-widget="storage"] .storage-bar')!;
            expect(storage.classList).toContain('nearly-full');
            expect(api.storage).toHaveBeenCalledWith(null, '');
            expect(document.title).toBe('Dashboard – SFE');
        });

        it('shows only the cards the account may use', async () => {
            const element = await openDashboard(limitedConfig([Rights.browse]));
            expect(cards(element)).toEqual(['files', 'notifications', 'storage']);
            expect(element.querySelector('a[href="/admin"]')).toBeNull();
        });

        it('says when there is nothing to show', async () => {
            const element = await openDashboard(limitedConfig([]));
            expect(cards(element)).toEqual([]);
            expect(element.querySelector('.empty')!.textContent).toContain('Nothing to show here');
        });
    });
});
