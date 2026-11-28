import { Component, signal, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, convertToParamMap, Router, UrlTree } from '@angular/router';
import {
    ApiMock,
    fakeConfig,
    LIBRARY,
    limitedConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
    type,
} from '../../testing/fakes';
import { ConnectionsConfig } from '../core/config.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ConnectDialog } from './connect.dialog';
import { problemOf } from './sign-in';
import { ConnectionInfo, ConnectionsView } from '../core/connection.models';
import { connectionsLanding } from './connections-landing';
import { ConnectionsSectionComponent } from './connections-section.component';
import { ProviderSetupComponent } from './provider-setup.component';

const CONNECTIONS: ConnectionsConfig = {
    levels: ['account', 'library', 'server'],
    kinds: [
        {
            kind: 'dropbox',
            name: 'Dropbox',
            icon: 'globe',
            signIn: [
                { kind: 'oAuthRedirect', fields: [] },
                { kind: 'oAuthCode', fields: [] },
            ],
            capabilities: {},
        },
    ],
};

function connection(overrides: Partial<ConnectionInfo> = {}): ConnectionInfo {
    return {
        id: 7,
        kind: 'dropbox',
        name: 'My Dropbox',
        level: 'account',
        ownerId: 1,
        tenantId: null,
        allTenants: false,
        tenantIds: [],
        rootPath: '',
        account: 'me@example.org',
        state: 'ready',
        disabled: false,
        readOnly: false,
        created: '2026-11-13T12:00:00Z',
        createdById: 1,
        groups: [],
        ...overrides,
    };
}

function view(overrides: Partial<ConnectionsView> = {}): ConnectionsView {
    return { usable: [], managed: [], groups: [], libraries: [], ...overrides };
}

describe('Connections', () => {
    let api: ApiMock;

    beforeEach(() => {
        api = mockApi();
        api.connections.mockResolvedValue(view());
        TestBed.configureTestingModule({ providers: testProviders(api) });
    });

    afterEach(() => TestBed.resetTestingModule());

    describe('the add dialog', () => {
        @Component({
            imports: [ConnectDialog],
            template: `<sfe-connect-dialog (done)="done.set($event)" />`,
        })
        class Host {
            readonly dialog = viewChild.required(ConnectDialog);
            readonly done = signal<ConnectionInfo | null>(null);
        }

        let fixture: ComponentFixture<Host>;
        const root = () => document.body;

        async function open(): Promise<ConnectDialog> {
            await loadConfig(api, fakeConfig({ connections: CONNECTIONS }));
            fixture = TestBed.createComponent(Host);
            await settle();
            const dialog = fixture.componentInstance.dialog();
            dialog.open({ level: 'account' });
            await settle();
            return dialog;
        }

        it('sends the browser to the address the server gives for the redirect sign-in', async () => {
            const dialog = await open();
            const navigate = vi.spyOn(dialog as unknown as { navigate: (a: string) => void }, 'navigate');
            navigate.mockImplementation(() => undefined);
            api.authorizeConnection.mockResolvedValue({
                connection: null,
                address: 'https://provider/authorize?state=s',
                state: 's',
                fields: [],
            });
            type(root().querySelector<HTMLInputElement>('[data-name]')!, 'Holidays');
            type(root().querySelector<HTMLInputElement>('[data-root]')!, '/Photos');

            root().querySelector<HTMLButtonElement>('[data-start]')!.click();
            await settle();

            expect(api.authorizeConnection).toHaveBeenCalledWith({
                kind: 'dropbox',
                level: 'account',
                name: 'Holidays',
                method: 'oAuthRedirect',
                rootPath: '/Photos',
                fields: {},
            });
            expect(navigate).toHaveBeenCalledWith('https://provider/authorize?state=s');
        });

        it('asks for the code of the code flow and finishes with it', async () => {
            const dialog = await open();
            const tab = vi.spyOn(dialog as unknown as { openTab: (a: string) => void }, 'openTab');
            tab.mockImplementation(() => undefined);
            const select = root().querySelector<HTMLSelectElement>('[data-method]')!;
            select.value = 'oAuthCode';
            select.dispatchEvent(new Event('change'));
            api.authorizeConnection.mockResolvedValue({
                connection: null,
                address: 'https://provider/authorize',
                state: 'the-state',
                fields: [{ id: 'code', label: 'Code shown by Dropbox', type: 'text' }],
            });
            api.completeConnection.mockResolvedValue({
                connection: connection(),
                reconnected: false,
                otherAccount: false,
            });

            root().querySelector<HTMLButtonElement>('[data-start]')!.click();
            await settle();
            type(root().querySelector<HTMLInputElement>('[data-field="code"]')!, 'abc');
            root().querySelector<HTMLButtonElement>('[data-complete]')!.click();
            await settle();

            expect(tab).toHaveBeenCalledWith('https://provider/authorize');
            expect(api.completeConnection).toHaveBeenCalledWith('the-state', { code: 'abc' });
            expect(fixture.componentInstance.done()?.id).toBe(7);
        });

        it("shows the server's refusals by field, and a problem as it is", async () => {
            await open();
            const { ApiError } = await import('../core/api.service');
            api.authorizeConnection.mockRejectedValueOnce(
                new ApiError(400, 'Invalid', { name: ['A connection has a name.'] }),
            );
            root().querySelector<HTMLButtonElement>('[data-start]')!.click();
            await settle();
            expect(root().textContent).toContain('A connection has a name.');

            // A 409 resolves with its problem (Api), not with what the route answers otherwise.
            api.authorizeConnection.mockResolvedValueOnce({
                status: 409,
                title: 'Conflict',
                detail: 'Too many sign-ins.',
            } as never);
            root().querySelector<HTMLButtonElement>('[data-start]')!.click();
            await settle();
            expect(root().querySelector('[data-problem]')!.textContent).toContain('Too many sign-ins.');
        });

        it('says when no kind is configured yet', async () => {
            await loadConfig(api, fakeConfig({ connections: { ...CONNECTIONS, kinds: [] } }));
            fixture = TestBed.createComponent(Host);
            await settle();
            fixture.componentInstance.dialog().open({ level: 'account' });
            await settle();
            expect(root().querySelector('[data-no-kinds]')).not.toBeNull();
            expect(root().querySelector('[data-start]')).toBeNull();
        });
    });

    describe('the section', () => {
        @Component({
            imports: [ConnectionsSectionComponent],
            template: `<sfe-connections-section [tab]="tab()" />`,
        })
        class Host {
            readonly tab = signal('mine');
        }

        let fixture: ComponentFixture<Host>;
        const el = () => fixture.nativeElement as HTMLElement;

        async function create(tab: string, config = fakeConfig({ connections: CONNECTIONS })): Promise<void> {
            await loadConfig(api, config);
            fixture = TestBed.createComponent(Host);
            fixture.componentInstance.tab.set(tab);
            await settle();
        }

        it("lists one's own connections on My account, the others on the administration's page", async () => {
            api.connections.mockResolvedValue(
                view({
                    managed: [
                        { connection: connection(), manage: true, remove: true },
                        {
                            connection: connection({ id: 8, name: 'Shared', level: 'server', ownerId: null }),
                            manage: true,
                            remove: true,
                        },
                    ],
                }),
            );
            await create('mine');
            expect(
                [...el().querySelectorAll('[data-connection]')].map((e) => e.getAttribute('data-connection')),
            ).toEqual(['7']);
            expect(el().querySelector('[data-add="account"]')).not.toBeNull();

            fixture.componentInstance.tab.set('shared');
            await settle();
            expect(
                [...el().querySelectorAll('[data-connection]')].map((e) => e.getAttribute('data-connection')),
            ).toEqual(['8']);
            expect(el().querySelector('[data-add="library"]')).not.toBeNull();
            expect(el().querySelector('[data-add="server"]')).not.toBeNull();
        });

        it("offers only what the session may do: someone else's connection is removed, never managed", async () => {
            api.connections.mockResolvedValue(
                view({
                    managed: [
                        { connection: connection({ id: 9, ownerId: 5, name: "Bob's" }), manage: false, remove: true },
                    ],
                }),
            );
            await create('shared');
            el().querySelector<HTMLButtonElement>('[data-manage]')!.click();
            await settle();
            expect(el().querySelector('[data-sign-in-again]')).toBeNull();
            expect(api.connectionIndex).not.toHaveBeenCalled();
            expect(el().querySelector('[data-sync-now]')).toBeNull();
            expect(el().textContent).not.toContain('Read only');
            vi.spyOn(window, 'confirm').mockReturnValue(true);
            api.removeConnection.mockResolvedValue(undefined);
            el().querySelector<HTMLButtonElement>('[data-remove]')!.click();
            await settle();
            expect(api.removeConnection).toHaveBeenCalledWith(9);
        });

        it("shows where the connection's index stands in the space and syncs it now", async () => {
            api.connections.mockResolvedValue(
                view({ managed: [{ connection: connection(), manage: true, remove: true }] }),
            );
            const known = {
                indexed: true,
                running: false,
                listing: false,
                files: 3,
                folders: 2,
                lastSync: new Date().toISOString(),
                tooLong: 0,
                lastError: null,
                activity: null,
            };
            api.connectionIndex.mockResolvedValue(known);
            api.syncConnectionIndex.mockResolvedValue({ ...known, running: true, activity: 'Reading the changes' });
            await create('mine');
            el().querySelector<HTMLButtonElement>('[data-manage]')!.click();
            await settle();
            expect(api.connectionIndex).toHaveBeenCalledWith(7);
            expect(el().querySelector('[data-index-status]')!.textContent).toContain('3 files and 2 folders');

            el().querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
            await settle();
            expect(api.syncConnectionIndex).toHaveBeenCalledWith(7);
            expect(el().querySelector('[data-index-status]')!.textContent).toContain('Reading the changes');
            expect(el().querySelector<HTMLButtonElement>('[data-sync-now]')!.disabled).toBe(true);
        });

        it("gives a server connection to the space's groups", async () => {
            const shared = connection({ id: 8, name: 'Shared', level: 'server', ownerId: null, allTenants: true });
            api.connections.mockResolvedValue(
                view({
                    managed: [{ connection: shared, manage: false, remove: false }],
                    groups: [{ id: 3, name: 'Users' }],
                }),
            );
            api.setConnectionGroups.mockResolvedValue(shared);
            await create('shared');
            el().querySelector<HTMLButtonElement>('[data-manage]')!.click();
            await settle();
            const select = el().querySelector<HTMLSelectElement>('[data-group="3"]')!;
            select.value = 'read';
            select.dispatchEvent(new Event('change'));
            await settle();
            expect(api.setConnectionGroups).toHaveBeenCalledWith(8, { 3: 'read' });
        });

        it("opens a space's or the server's connection's folder rules to those managing the space's", async () => {
            const own = connection({ id: 8, name: 'Shared', level: 'library', ownerId: null, tenantId: LIBRARY.id });
            const theirs = connection({ id: 9, ownerId: 5, name: "Bob's" });
            api.connections.mockResolvedValue(
                view({
                    managed: [
                        { connection: own, manage: true, remove: true },
                        { connection: theirs, manage: false, remove: true },
                    ],
                    groups: [{ id: 3, name: 'Users' }],
                }),
            );
            api.connectionRules.mockResolvedValue([]);
            await create('shared');
            const row = (id: number) => el().querySelector<HTMLElement>(`[data-connection="${id}"]`)!;
            row(9).querySelector<HTMLButtonElement>('[data-manage]')!.click();
            await settle();
            expect(row(9).querySelector('[data-folder-rules]')).toBeNull();

            row(8).querySelector<HTMLButtonElement>('[data-manage]')!.click();
            await settle();
            row(8).querySelector<HTMLButtonElement>('[data-folder-rules]')!.click();
            await settle();

            expect(api.connectionRules).toHaveBeenCalledWith(8);
            expect(row(8).querySelector('sfe-folder-access')).not.toBeNull();
            expect(row(8).querySelector('#folder-rule-principal, .access-card')).not.toBeNull();
        });

        it("hides the folder rules from who manages only the server's connections", async () => {
            const shared = connection({ id: 8, name: 'Shared', level: 'server', ownerId: null, allTenants: true });
            api.connections.mockResolvedValue(view({ managed: [{ connection: shared, manage: true, remove: true }] }));
            await create(
                'shared',
                limitedConfig([Rights.serverConnections], { connections: CONNECTIONS, tenant: LIBRARY }),
            );
            el().querySelector<HTMLButtonElement>('[data-manage]')!.click();
            await settle();
            expect(el().querySelector('[data-folder-rules]')).toBeNull();
        });

        it('adds nothing at a level the session may not make', async () => {
            await create(
                'shared',
                limitedConfig([Rights.libraryConnections], { connections: CONNECTIONS, tenant: LIBRARY }),
            );
            expect(el().querySelector('[data-add="library"]')).not.toBeNull();
            expect(el().querySelector('[data-add="server"]')).toBeNull();
        });

        it('says when connections are off', async () => {
            await create('mine', fakeConfig({ connections: null }));
            expect(el().querySelector('[data-off]')).not.toBeNull();
        });
    });

    describe('the setup of a kind', () => {
        @Component({ imports: [ProviderSetupComponent], template: `<sfe-provider-setup kind="dropbox" />` })
        class Host {}

        it('shows the return address and where the secret comes from, and sends a secret it then forgets', async () => {
            api.connectionProvider.mockResolvedValue({
                kind: 'dropbox',
                name: 'Dropbox',
                offered: false,
                returnAddress: 'https://sfe/api/connections/oauth/callback',
                appSecret: 'none',
            });
            api.setProviderSecret.mockResolvedValue(undefined);
            await loadConfig(api);
            const fixture = TestBed.createComponent(Host);
            await settle();
            const el = fixture.nativeElement as HTMLElement;
            expect(el.querySelector<HTMLInputElement>('[data-return-address]')!.value).toBe(
                'https://sfe/api/connections/oauth/callback',
            );
            expect(el.querySelector('[data-secret-state]')!.textContent).toContain('Optional');

            const secret = el.querySelector<HTMLInputElement>('[data-secret]')!;
            type(secret, 'top-secret');
            await settle();
            el.querySelector<HTMLButtonElement>('[data-save-secret]')!.click();
            await settle();

            expect(api.setProviderSecret).toHaveBeenCalledWith('dropbox', 'top-secret');
            expect(el.querySelector<HTMLInputElement>('[data-secret]')!.value).toBe('');
        });
    });

    describe('the page the provider sends back to', () => {
        async function land(query: Record<string, string>): Promise<string> {
            await loadConfig(api, fakeConfig({ connections: CONNECTIONS }));
            const route = { queryParamMap: convertToParamMap(query) } as ActivatedRouteSnapshot;
            const tree = (await TestBed.runInInjectionContext(() =>
                connectionsLanding(route, { url: '/connections', root: route } as never),
            )) as UrlTree;
            return TestBed.inject(Router).serializeUrl(tree);
        }

        it('opens the page of the connection made and says so', async () => {
            const toasts = TestBed.inject(ToastService);
            const shown = vi.spyOn(toasts, 'show');
            api.connections.mockResolvedValue(
                view({
                    managed: [
                        {
                            connection: connection({ id: 8, level: 'server', ownerId: null }),
                            manage: true,
                            remove: true,
                        },
                    ],
                }),
            );
            expect(await land({ made: '8' })).toBe('/admin?section=connections&page=connections');
            expect(shown).toHaveBeenCalledWith('Connection added.');

            await land({ reconnected: '8', otherAccount: 'true' });
            expect(shown.mock.calls.at(-1)![0]).toContain('another account');
        });

        it('says what went wrong', async () => {
            const errors = vi.spyOn(TestBed.inject(ToastService), 'error');
            expect(await land({ error: 'expired' })).toBe('/admin?section=account&page=connections');
            expect(errors.mock.calls[0][0]).toContain('Start again');
        });
    });

    it('reads a problem answered for a 409, and nothing else', () => {
        expect(problemOf({ status: 409, title: 'Conflict', detail: 'Busy.' })).toBe('Busy.');
        expect(problemOf({ connection: null, address: 'x' })).toBeNull();
        expect(problemOf(undefined)).toBeNull();
    });
});
