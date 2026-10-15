import { TestBed } from '@angular/core/testing';
import { ApiMock, fakeConfig, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ApiError } from '../../core/api.service';
import { Rights } from '../../core/rights';
import { ListingStore } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { ListingComponent } from './listing.component';

describe('ListingComponent', () => {
    let api: ApiMock;

    async function show(error: ApiError, config = fakeConfig()): Promise<HTMLElement> {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, config);
        vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
        api.browse.mockRejectedValue(error);
        await TestBed.inject(ListingStore).load();
        const fixture = TestBed.createComponent(ListingComponent);
        await settle();
        return fixture.nativeElement;
    }

    afterEach(() => vi.restoreAllMocks());

    it('says when the whole library folder is missing, and where an administrator picks another', async () => {
        const element = await show(new ApiError(404, 'The library folder E:\\Media\\ does not exist on the server.', null, 'library-root-missing'));

        expect(element.querySelector('sfe-empty-state')!.textContent).toContain('The library folder is missing');
        expect(element.textContent).toContain('E:\\Media\\');
        const link = [...element.querySelectorAll<HTMLAnchorElement>('a')].find(a => a.textContent!.includes('Pick another folder'))!;
        expect(link.getAttribute('href')).toBe('/admin?section=system&page=installation');
    });

    it('sends the others to an administrator', async () => {
        const element = await show(new ApiError(404, 'The library folder E:\\Media\\ does not exist on the server.', null, 'library-root-missing'),
            limitedConfig([Rights.browse]));

        expect(element.textContent).toContain('Ask an administrator');
        expect(element.textContent).not.toContain('Pick another folder');
    });

    it('keeps saying a missing sub-folder may have moved', async () => {
        const element = await show(new ApiError(404, 'Not Found'));

        expect(element.textContent).toContain('Folder not found');
        expect(element.textContent).toContain('Go to library root');
    });
});
