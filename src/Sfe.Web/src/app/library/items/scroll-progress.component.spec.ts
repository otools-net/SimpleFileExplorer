import { TestBed } from '@angular/core/testing';
import { DirectoryListing, LibraryItem, SearchResults } from '../../core/models';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ListingStore } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { ListingComponent } from './listing.component';

/** Every listed item is this tall, one under the other (jsdom lays nothing out). */
const ITEM_HEIGHT = 300;

describe('ScrollProgressComponent', () => {
    let api: ApiMock;
    let store: ListingStore;
    let element: HTMLElement;

    const folder = item('Folder', { kind: 'directory' });
    const files = Array.from({ length: 9 }, (_, i) => item(`f${i}.jpg`));
    const progress = () => element.querySelector<HTMLElement>('.scroll-progress')!;

    beforeEach(async () => {
        localStorage.clear();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig({ pageSize: 4, searchPageSize: 4 }));
        store = TestBed.inject(ListingStore);
        vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
        api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
        vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
            const items = [...document.querySelectorAll('.content > .folders > *, .content > .files > *')];
            const top = items.indexOf(this) * ITEM_HEIGHT - window.scrollY;
            return { top, bottom: top + ITEM_HEIGHT, left: 0, right: 100, width: 100, height: ITEM_HEIGHT, x: 0, y: top } as DOMRect;
        });
    });

    afterEach(async () => {
        await scrollTo(0);
        vi.restoreAllMocks();
    });

    async function show(items: LibraryItem[], page: Partial<DirectoryListing> = {}): Promise<void> {
        api.browse.mockResolvedValue({ path: '', name: 'Root', parentPath: null, items, offset: 0, limit: 4, total: items.length, directoryCount: 0, fileCount: items.length, ...page });
        await store.load();
        const fixture = TestBed.createComponent(ListingComponent);
        element = fixture.nativeElement;
        document.body.append(element);
        await settle();
    }

    async function scrollTo(y: number): Promise<void> {
        Object.defineProperty(window, 'scrollY', { value: y, configurable: true });
        window.dispatchEvent(new Event('scroll'));
        await new Promise(resolve => requestAnimationFrame(resolve));
        await settle();
    }

    it('stays hidden at the top of the listing', async () => {
        await show([folder, ...files], { total: 30 });

        expect(progress().hidden).toBe(true);
    });

    it('says the page, the items scrolled to of the folder\'s total, and fills the bar as much', async () => {
        await show([folder, ...files], { total: 30 });

        // 768 px of screen from 1000 px down: the items whose top is above 1768 px, the folder included.
        await scrollTo(1000);

        expect(progress().hidden).toBe(false);
        expect(progress().textContent).toContain('Page 2 of 8');
        expect(progress().querySelector('.scroll-progress-count')!.textContent).toBe('6 / 30');
        expect(progress().querySelector<HTMLElement>('.scroll-progress-bar > span')!.style.width).toBe('20%');

        await scrollTo(2500);
        expect(progress().querySelector('.scroll-progress-count')!.textContent).toBe('10 / 30');
        expect(progress().textContent).toContain('Page 3 of 8');
    });

    it('takes the number of matches of a search, at least as many when the count stopped', async () => {
        const page: SearchResults = { query: 'f', path: '', items: files, limit: 4, hasMore: true, next: 'f8.jpg', total: 10000, totalCapped: true };
        api.search.mockResolvedValue(page);
        store.route.set({ path: '', q: 'f', sort: '', desc: false });
        await store.load();
        const fixture = TestBed.createComponent(ListingComponent);
        element = fixture.nativeElement;
        document.body.append(element);
        await settle();

        await scrollTo(1000);

        expect(progress().querySelector('.scroll-progress-count')!.textContent).toBe('6 / 10,000+');
        expect(progress().textContent).toContain('Page 2 of 2,500+');
    });

    it('measures again when more items are drawn', async () => {
        await show(files.slice(0, 4), { total: 9 });
        await scrollTo(1500);
        expect(progress().querySelector('.scroll-progress-count')!.textContent).toBe('4 / 9');

        api.browse.mockResolvedValue({ path: '', name: 'Root', parentPath: null, items: files.slice(4), offset: 4, limit: 4, total: 9, directoryCount: 0, fileCount: 9 });
        await store.loadMore();
        await settle();
        await new Promise(resolve => requestAnimationFrame(resolve));
        await settle();

        expect(progress().querySelector('.scroll-progress-count')!.textContent).toBe('8 / 9');
    });
});
