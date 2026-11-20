import { TestBed } from '@angular/core/testing';
import { ApiMock, fakeConfig, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ApiError } from '../../core/api.service';
import { ClientConfig, ConnectionLevelName } from '../../core/config.models';
import { UsableConnection } from '../../core/connection.models';
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
        vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
        api.browse.mockRejectedValue(error);
        await TestBed.inject(ListingStore).load();
        const fixture = TestBed.createComponent(ListingComponent);
        await settle();
        return fixture.nativeElement;
    }

    afterEach(() => vi.restoreAllMocks());

    it('says when the whole library folder is missing, and where an administrator picks another', async () => {
        const element = await show(
            new ApiError(
                404,
                'The library folder E:\\Media\\ does not exist on the server.',
                null,
                'library-root-missing',
            ),
        );

        expect(element.querySelector('sfe-empty-state')!.textContent).toContain('The library folder is missing');
        expect(element.textContent).toContain('E:\\Media\\');
        const link = [...element.querySelectorAll<HTMLAnchorElement>('a')].find((a) =>
            a.textContent!.includes('Pick another folder'),
        )!;
        expect(link.getAttribute('href')).toBe('/admin?section=system&page=installation');
    });

    it('sends the others to an administrator', async () => {
        const element = await show(
            new ApiError(
                404,
                'The library folder E:\\Media\\ does not exist on the server.',
                null,
                'library-root-missing',
            ),
            limitedConfig([Rights.browse]),
        );

        expect(element.textContent).toContain('Ask an administrator');
        expect(element.textContent).not.toContain('Pick another folder');
    });

    it('keeps saying a missing sub-folder may have moved', async () => {
        const element = await show(new ApiError(404, 'Not Found'));

        expect(element.textContent).toContain('Folder not found');
        expect(element.textContent).toContain('Go to library root');
    });
});

describe('ListingComponent on a connection that cannot be used', () => {
    const CONNECTIONS = {
        levels: ['account', 'library'] as ConnectionLevelName[],
        kinds: [{ kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} }],
    };
    const reconnect = new ApiError(
        409,
        'The connection must be signed in to again.',
        null,
        'connection-needs-reconnecting',
    );

    async function show(error: ApiError, level: ConnectionLevelName, config: ClientConfig): Promise<HTMLElement> {
        const api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, config);
        vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
        const usable: UsableConnection[] = [
            { id: 7, kind: 'dropbox', name: 'Shared', level, state: 'needsReconnecting', access: 'read' },
        ];
        api.connections.mockResolvedValue({ usable, managed: [], groups: [], libraries: [] });
        api.connectionBrowse.mockRejectedValue(error);
        const store = TestBed.inject(ListingStore);
        store.route.set({ c: 7, path: '', q: '', sort: '', desc: false });
        await store.load();
        const fixture = TestBed.createComponent(ListingComponent);
        await settle();
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        return fixture.nativeElement;
    }

    const link = (element: HTMLElement, text: string) =>
        [...element.querySelectorAll<HTMLAnchorElement>('a')].find((a) => a.textContent!.includes(text)) ?? null;

    afterEach(() => vi.restoreAllMocks());

    it('asks the owner of an account-level one to sign in again on their connections page', async () => {
        const element = await show(reconnect, 'account', fakeConfig({ connections: CONNECTIONS }));

        expect(element.querySelector('[data-connection-problem]')!.textContent).toContain(
            'This connection must be signed in to again',
        );
        expect(link(element, 'Open connections')!.getAttribute('href')).toBe('/admin?section=account&page=connections');
        expect(link(element, 'Go to library root')!.getAttribute('href')).not.toContain('c=7');
    });

    it('sends who manages a library-level one to the shared connections page', async () => {
        const element = await show(reconnect, 'library', fakeConfig({ connections: CONNECTIONS }));

        expect(link(element, 'Open connections')!.getAttribute('href')).toBe(
            '/admin?section=connections&page=connections',
        );
    });

    it('tells who only uses a shared one to ask its manager', async () => {
        const config = limitedConfig([Rights.browse, Rights.useConnections], { connections: CONNECTIONS });
        const element = await show(reconnect, 'library', config);

        expect(element.textContent).toContain('Ask whoever manages the connection');
        expect(link(element, 'Open connections')).toBeNull();
    });

    it('says why a connection switched off is not available', async () => {
        const off = new ApiError(409, 'The connection is switched off.', null, 'connection-unusable');
        const element = await show(off, 'library', fakeConfig({ connections: CONNECTIONS }));

        expect(element.textContent).toContain('This connection is not available');
        expect(element.textContent).toContain('The connection is switched off.');
    });
});
