import { TestBed } from '@angular/core/testing';
import { DirectoryListing, LibraryItem } from '../../core/models';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FilesWidgetComponent } from './files-widget.component';

function listing(path: string, items: LibraryItem[], overrides: Partial<DirectoryListing> = {}): DirectoryListing {
    const directoryCount = items.filter((entry) => entry.kind === 'directory').length;
    return {
        path,
        name: path.split('/').pop() ?? '',
        parentPath: null,
        items,
        offset: 0,
        limit: 13,
        total: items.length,
        directoryCount,
        fileCount: items.length - directoryCount,
        ...overrides,
    };
}

const PATH_ONLY: Partial<DirectoryListing> = { access: { rights: [], pathOnly: true } };

describe('dashboard Files widget', () => {
    let api: ApiMock;

    async function render(): Promise<HTMLElement> {
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig());
        const fixture = TestBed.createComponent(FilesWidgetComponent);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    const folderLinks = (element: HTMLElement) =>
        [...element.querySelectorAll<HTMLAnchorElement>('[data-folder] a')].map((link) => link.getAttribute('href'));

    beforeEach(() => {
        api = mockApi();
    });

    afterEach(() => TestBed.resetTestingModule());

    it('shows the top of the library, its folders each opening its listing, and what it holds', async () => {
        api.browse.mockResolvedValue(listing('', [item('Photos'), item('Music'), item('notes.txt')]));
        const element = await render();

        const top = element.querySelector<HTMLAnchorElement>('[data-start-folder]')!;
        expect(top.textContent).toContain('Root');
        expect(top.getAttribute('href')).toBe('/');
        expect(folderLinks(element)).toEqual(['/?path=Photos', '/?path=Music']);
        expect(element.textContent).toContain('2 folders, 1 file.');
        expect(api.browse).toHaveBeenCalledWith({ path: '', offset: 0, limit: 13, sort: 'name', desc: false });
    });

    it("shows the folder further down where the account's folders start", async () => {
        api.browse.mockImplementation(async ({ path }) =>
            path === ''
                ? listing('', [item('Homes')], { access: { rights: [], pathOnly: true, start: 'Homes/tom' } })
                : listing('Homes/tom', [item('Homes/tom/Trips')]),
        );
        const element = await render();

        const top = element.querySelector<HTMLAnchorElement>('[data-start-folder]')!;
        expect(top.textContent).toContain('/Homes/tom');
        expect(top.getAttribute('href')).toBe('/?path=Homes%2Ftom');
        expect(folderLinks(element)).toEqual(['/?path=Homes%2Ftom%2FTrips']);
        expect(api.browse).toHaveBeenCalledTimes(2);
    });

    it("stays at the top when the account's folders start in several places", async () => {
        api.browse.mockResolvedValue(listing('', [item('Homes'), item('Shared')], PATH_ONLY));
        const element = await render();
        expect(folderLinks(element)).toEqual(['/?path=Homes', '/?path=Shared']);
        expect(api.browse).toHaveBeenCalledTimes(1);
    });

    it('says when the files cannot be read', async () => {
        api.browse.mockRejectedValue(new Error('down'));
        const element = await render();
        expect(element.textContent).toContain('The files could not be read.');
    });
});
