import { TestBed } from '@angular/core/testing';
import { DirectoryListing } from '../../core/models';
import { ApiMock, fakeConfig, intersect, item, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ListingStore } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { LocationLinks } from './location-links.service';
import { LocationComponent } from './location.component';
import { LocationStripComponent } from './location-strip.component';

let api: ApiMock;
let store: ListingStore;
let links: LocationLinks;

async function show(path: string, q = ''): Promise<void> {
    store.route.set({ path, q, sort: '', desc: false });
    if (q)
        api.search.mockResolvedValue({ query: q, path, items: [], limit: 50, hasMore: false, next: null });
    else
        api.browse.mockResolvedValue({ path, name: '', parentPath: null, items: [], offset: 0, limit: 100, total: 0, directoryCount: 0, fileCount: 0 } as DirectoryListing);
    await store.load();
    await settle();
}

beforeEach(async () => {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, fakeConfig({ rootName: 'Library' }));
    store = TestBed.inject(ListingStore);
    links = TestBed.inject(LocationLinks);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: 'Media/2020/Summer', previous: item('Media/2020/Spring', { kind: 'directory' }), next: null, position: 2, count: 2 });
});

afterEach(() => vi.restoreAllMocks());

describe('LocationStripComponent', () => {
    function create(): HTMLElement {
        const fixture = TestBed.createComponent(LocationStripComponent);
        return fixture.nativeElement;
    }

    const bar = (element: HTMLElement) => element.querySelector<HTMLElement>('.location-strip-bar')!;
    const title = (element: HTMLElement) => element.querySelector<HTMLButtonElement>('.location-strip-title')!;

    it('stays hidden and out of reach while the location header is in view', async () => {
        const element = create();
        await show('Media/2020/Summer');

        expect(bar(element).classList).not.toContain('shown');
        expect(bar(element).hasAttribute('inert')).toBe(true);
    });

    it('shows the folder buttons, the parent and the folder once the header has scrolled away', async () => {
        const element = create();
        await show('Media/2020/Summer');
        links.headHidden.set(true);
        await settle();

        expect(bar(element).classList).toContain('shown');
        expect(bar(element).hasAttribute('inert')).toBe(false);
        expect([...element.querySelectorAll<HTMLAnchorElement>('.folder-nav a')].map(a => a.title))
            .toEqual(['Parent folder: 2020 (Backspace)', 'Previous folder: Spring (2 of 2)', 'Last folder']);
        expect(title(element).querySelector('.parent')!.textContent).toBe('2020');
        expect(title(element).querySelector('.current')!.textContent).toBe('Summer');
        expect(title(element).title).toBe('Back to the top: Library / Media / 2020 / Summer');
    });

    it('names the search, without folder buttons, and the root alone at the top', async () => {
        const element = create();
        links.headHidden.set(true);
        await show('Media', 'beach');

        expect(element.querySelector('.folder-nav')).toBeNull();
        expect(title(element).querySelector('.current')!.textContent).toBe('Media');
        expect(title(element).querySelector('.search-label')!.textContent).toBe('Search “beach”');

        await show('');
        expect(title(element).querySelector('.parent')).toBeNull();
        expect(title(element).querySelector('.current')!.textContent).toBe('Library');
    });

    it('goes back to the top when its name is clicked', async () => {
        const element = create();
        await show('Media');
        links.headHidden.set(true);
        await settle();
        const scroll = vi.spyOn(window, 'scrollTo').mockImplementation(() => { });

        title(element).click();

        expect(scroll).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
    });
});

describe('LocationComponent header visibility', () => {
    it('reports its header in view, and no longer hidden once gone', async () => {
        await show('Media');
        links.headHidden.set(true);
        const fixture = TestBed.createComponent(LocationComponent);
        await settle();

        intersect(fixture.nativeElement.querySelector('.location-head'));
        expect(links.headHidden()).toBe(false);

        links.headHidden.set(true);
        fixture.destroy();
        expect(links.headHidden()).toBe(false);
    });
});

describe('LocationComponent new folder button', () => {
    function button(): HTMLButtonElement | null {
        const fixture = TestBed.createComponent(LocationComponent);
        fixture.detectChanges();
        return (fixture.nativeElement as HTMLElement).querySelector('.new-folder-button');
    }

    it('is there with the right, in a folder', async () => {
        await show('Media');
        expect(button()).not.toBeNull();
    });

    it('is not there in a search, in a folder only on the way to another, or without the right', async () => {
        await show('Media', 'beach');
        expect(button()).toBeNull();

        api.browse.mockResolvedValue({
            path: 'Media', name: '', parentPath: null, items: [], offset: 0, limit: 100, total: 0, directoryCount: 0, fileCount: 0,
            access: { rights: ['library.browse', 'files.createFolders'], pathOnly: true },
        } as DirectoryListing);
        store.route.set({ path: 'Media', q: '', sort: '', desc: false });
        await store.load();
        expect(button()).toBeNull();

        await loadConfig(api, limitedConfig(['library.browse']));
        await show('Media');
        expect(button()).toBeNull();
    });
});
