import { TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api-error';
import { ConnectionsConfig } from '../../core/config.models';
import { ConnectionInfo } from '../../core/connection.models';
import { Rights } from '../../core/rights';
import {
    ApiMock,
    fakeConfig,
    limitedConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
    type,
} from '../../../testing/fakes';
import { WizardHarness } from '../../../testing/wizard-harness';
import { keepDraft } from '../wizard-kept';
import { AddConnectionDraft, initialDraft } from './add-connection-draft';
import { ADD_CONNECTION_WIZARD } from './add-connection.wizard';

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
        {
            kind: 'nas',
            name: 'NAS',
            icon: 'globe',
            signIn: [
                {
                    kind: 'password',
                    fields: [
                        { id: 'name', label: 'Name', type: 'text' },
                        { id: 'password', label: 'Password', type: 'secret' },
                    ],
                },
            ],
            capabilities: {},
        },
    ],
};

const made = { id: 9, name: 'Family Dropbox' } as ConnectionInfo;
let api: ApiMock;

async function open(
    config = fakeConfig({ connections: CONNECTIONS }),
    returned: Record<string, string> = {},
): Promise<WizardHarness> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    api.connections.mockResolvedValue({
        usable: [],
        managed: [],
        groups: [
            { id: 5, name: 'Family' },
            { id: 6, name: 'Guests' },
        ],
        libraries: [],
    });
    api.authorizeConnection.mockResolvedValue(null as never);
    return WizardHarness.open(ADD_CONNECTION_WIZARD, returned);
}

async function choose(wizard: WizardHarness, label: string): Promise<void> {
    const labels = [...wizard.element.querySelectorAll<HTMLLabelElement>('label.check')];
    labels
        .find((element) => element.textContent!.trim().startsWith(label))!
        .querySelector('input')!
        .click();
    await settle();
}

async function select(wizard: WizardHarness, selector: string, value: string): Promise<void> {
    const element = wizard.element.querySelector<HTMLSelectElement>(selector)!;
    element.value = value;
    element.dispatchEvent(new Event('change'));
    await settle();
}

/** Through the first three steps: Dropbox, this space for Family (read only), named "Family Dropbox". */
async function upToSignIn(wizard: WizardHarness): Promise<void> {
    await choose(wizard, 'Dropbox');
    await wizard.next();
    await choose(wizard, 'This space');
    await select(wizard, '[data-group="5"]', 'read');
    await wizard.next();
    type(wizard.element.querySelector<HTMLInputElement>('[data-name]')!, 'Family Dropbox');
    await settle();
    await wizard.next();
}

const request = {
    kind: 'dropbox',
    level: 'library',
    name: 'Family Dropbox',
    method: 'oAuthRedirect',
    rootPath: undefined,
    fields: {},
    groups: { 5: 'read' },
    return: 'wizard',
};

beforeEach(() => sessionStorage.clear());

describe('the add-a-connection wizard', () => {
    it('picks a kind the server offers, named after it until a name is typed', async () => {
        const wizard = await open();
        expect(wizard.button('Next')!.disabled).toBe(true);

        await choose(wizard, 'Dropbox');
        expect(wizard.draft<AddConnectionDraft>()).toMatchObject({ kind: 'dropbox', name: 'Dropbox' });
        await choose(wizard, 'NAS');

        expect(wizard.draft<AddConnectionDraft>()).toMatchObject({
            kind: 'nas',
            name: 'NAS',
            method: 'password',
            fieldIds: ['name', 'password'],
        });
        expect(wizard.button('Next')!.disabled).toBe(false);
    });

    it('says when no kind is set up, with the way to set one up for who may', async () => {
        const wizard = await open(fakeConfig({ connections: { ...CONNECTIONS, kinds: [] } }));

        expect(wizard.element.querySelector('[data-no-kinds]')).not.toBeNull();
        expect(wizard.element.querySelector('a')!.getAttribute('href')).toBe('/admin?section=connections&page=setup');
        expect(wizard.button('Next')!.disabled).toBe(true);
    });

    it('offers the levels of its rights, and the groups for a space', async () => {
        const wizard = await open(limitedConfig([Rights.libraryConnections], { connections: CONNECTIONS }));
        await choose(wizard, 'Dropbox');
        await wizard.next();

        const levels = [...wizard.element.querySelectorAll('[name="add-connection-level"]')];
        expect(levels.map((input) => (input as HTMLInputElement).value)).toEqual(['library']);
        await choose(wizard, 'This space');
        expect(wizard.element.querySelectorAll('[data-groups] select')).toHaveLength(2);
    });

    it("checks each step with the server, nothing made, a later step's refusal waiting for it", async () => {
        const wizard = await open();
        await choose(wizard, 'Dropbox');
        await wizard.next();
        await choose(wizard, 'This space');
        await select(wizard, '[data-group="5"]', 'read');
        api.authorizeConnection.mockRejectedValueOnce(refusal('name', 'A connection needs a name.'));
        // The name typed by the kind step is cleared: the server refuses it, a field of the next step.
        await wizard.fill({ name: '' });

        await wizard.next();

        expect(api.authorizeConnection).toHaveBeenCalledWith({ ...request, name: '' }, true);
        expect(wizard.title()).toBe('Name and folder');
    });

    it('sends the browser to the provider on Finish, the draft kept for its return', async () => {
        const wizard = await open();
        await upToSignIn(wizard);
        expect(wizard.element.querySelector('[data-redirect]')!.textContent).toContain('Dropbox');
        await wizard.next();
        expect(wizard.title()).toBe('Review');
        const leave = vi.spyOn(wizard.wizard, 'leaveFor').mockImplementation(() => {});
        api.authorizeConnection.mockResolvedValueOnce({
            connection: null,
            address: 'https://provider/authorize',
            state: 's',
            fields: [],
        });

        await wizard.next();

        expect(api.authorizeConnection).toHaveBeenLastCalledWith(request);
        expect(leave).toHaveBeenCalledWith('https://provider/authorize');
        expect(JSON.parse(sessionStorage.getItem('sfe.wizard.add-connection')!).draft.groups).toEqual({ 5: 'read' });
    });

    it('ends when the provider sends the browser back, with who uses the connection', async () => {
        keepDraft('add-connection', {
            ...draft(),
            level: 'library',
            groups: { 5: 'read' },
            groupNames: { 5: 'Family' },
        });
        const wizard = await open(undefined, { made: '9' });

        expect(wizard.title()).toBe('Done');
        const lines = [...wizard.element.querySelectorAll('.wizard-end-details dd')].map((dd) => dd.textContent);
        expect(lines).toEqual(['This space', 'Read only']);
        const links = [...wizard.element.querySelectorAll<HTMLAnchorElement>('.wizard-links a')];
        expect(links.map((link) => link.getAttribute('href'))).toEqual([
            '/?c=9',
            '/admin?section=connections&page=connections',
        ]);
    });

    it("shows the provider's refusal at the review, for Finish to try again", async () => {
        keepDraft('add-connection', draft());
        const wizard = await open(undefined, { error: 'refused' });

        expect(wizard.title()).toBe('Review');
        expect(wizard.errors()).toEqual(["Could not connect: the storage didn't give access."]);
    });

    it('tries a password on Next, and makes the connection on Finish, never keeping the password', async () => {
        const wizard = await open();
        await choose(wizard, 'NAS');
        await wizard.next();
        await choose(wizard, 'Only me');
        await wizard.next();
        await wizard.next();
        expect(wizard.button('Next')!.disabled).toBe(true);
        type(wizard.element.querySelector<HTMLInputElement>('[data-field="name"]')!, 'ann');
        type(wizard.element.querySelector<HTMLInputElement>('[data-field="password"]')!, 'secret');
        await settle();
        api.authorizeConnection.mockResolvedValueOnce({ status: 409, title: 'Conflict', detail: 'Wrong.' } as never);

        await wizard.next();
        expect(wizard.errors()).toEqual(['Wrong.']);
        await wizard.next();
        expect(wizard.title()).toBe('Review');
        expect(api.authorizeConnection).toHaveBeenLastCalledWith(
            expect.objectContaining({ method: 'password', fields: { name: 'ann', password: 'secret' } }),
            true,
        );
        api.authorizeConnection.mockResolvedValueOnce({ connection: made, address: null, state: null, fields: null });
        await wizard.next();

        expect(wizard.title()).toBe('Done');
        expect(sessionStorage.length).toBe(0);
    });

    it('signs in with the code the provider shows, begun again after an earlier step changed', async () => {
        const wizard = await open();
        await upToSignIn(wizard);
        await choose(wizard, 'Sign in at the provider and copy');
        const tab = vi.spyOn(window, 'open').mockReturnValue(null);
        api.authorizeConnection.mockResolvedValueOnce({
            connection: null,
            address: 'https://p/a',
            state: 's1',
            fields: [],
        });

        wizard.element.querySelector<HTMLButtonElement>('[data-open-sign-in]')!.click();
        await settle();
        type(wizard.element.querySelector<HTMLInputElement>('[data-code]')!, 'the-code');
        await settle();
        expect(tab).toHaveBeenCalledWith('https://p/a', '_blank', 'noopener');
        expect(wizard.button('Next')!.disabled).toBe(false);
        await wizard.fill({ name: 'Renamed' });
        expect(wizard.button('Next')!.disabled).toBe(true);
        await wizard.fill({ name: 'Family Dropbox' });
        await wizard.next();
        api.completeConnection.mockResolvedValueOnce({ connection: made, reconnected: false, otherAccount: false });
        await wizard.next();

        expect(api.completeConnection).toHaveBeenCalledWith('s1', { code: 'the-code' });
        expect(wizard.title()).toBe('Done');
        tab.mockRestore();
    });
});

function draft(): AddConnectionDraft {
    return { ...initialDraft(), kind: 'dropbox', kindName: 'Dropbox', level: 'account', name: 'Family Dropbox' };
}

function refusal(field: string, message: string): ApiError {
    return new ApiError(400, 'Refused', { [field]: [message] });
}
