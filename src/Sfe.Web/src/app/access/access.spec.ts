import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AccountSectionComponent } from '../account/account-section.component';
import { ApiError } from '../core/api.service';
import { SessionInfo } from '../core/access.models';
import { FolderRuleView } from './access.models';
import { Rights } from '../core/rights';
import { HelpComponent } from '../shared/help.component';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    LIBRARY,
    limitedConfig,
    loadConfig,
    mockApi,
    settingsValues,
    settle,
    testProviders,
} from '../../testing/fakes';
import { builtIns, catalog, group, member, space, user, WORK } from '../../testing/access';
import { AccountsPageComponent } from './accounts-page.component';
import { FolderAccessComponent } from './folder-access.component';
import { SpacesPageComponent } from './spaces-page.component';

let api: ApiMock;
let element: HTMLElement;

async function create<T>(component: Type<T>, config = fakeConfig()): Promise<void> {
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    const fixture = TestBed.createComponent(component);
    element = fixture.nativeElement;
    await settle();
}

function type(selector: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
}

function choose(selector: string, value: string): void {
    const select = element.querySelector<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
}

/** The checkbox of the label whose text contains `text`, among `selector`. */
function checkbox(selector: string, text: string, root: ParentNode = element): HTMLInputElement {
    return [...root.querySelectorAll<HTMLLabelElement>(selector)]
        .find((label) => label.textContent!.includes(text))!
        .querySelector('input')!;
}

beforeEach(() => (api = mockApi()));
afterEach(() => vi.restoreAllMocks());

describe('HelpComponent', () => {
    it('opens and closes its explanation under the button', async () => {
        TestBed.configureTestingModule({ providers: testProviders(api) });
        const fixture = TestBed.createComponent(HelpComponent);
        fixture.componentRef.setInput('about', 'Groups');
        element = fixture.nativeElement;
        await settle();
        const button = element.querySelector<HTMLButtonElement>('.help-button')!;

        expect(button.getAttribute('aria-expanded')).toBe('false');
        expect(button.title).toContain('Groups');
        expect(element.querySelector('.help-panel')).toBeNull();

        button.click();
        await settle();
        expect(button.getAttribute('aria-expanded')).toBe('true');
        expect(element.querySelector('.help-panel')!.id).toBe(button.getAttribute('aria-controls'));

        buttonNamed(element, 'Got it')!.click();
        await settle();
        expect(element.querySelector('.help-panel')).toBeNull();
    });
});

describe('AccountsPageComponent', () => {
    const library = builtIns(LIBRARY.id, 10);
    const work = builtIns(WORK.id, 20);

    it('lists every account with its spaces and its groups in each', async () => {
        api.users.mockResolvedValue([
            user(1, 'admin', { isServerAdmin: true }),
            user(2, 'anna', { displayName: 'Anna', groups: [member(library[0]), member(work[1])] }),
            user(3, 'kids', { kind: 'guest', disabled: true, lockedOut: true }),
        ]);
        api.groups.mockResolvedValue([...library, ...work]);
        api.tenants.mockResolvedValue([space(LIBRARY.id, 'Library'), space(WORK.id, 'Work')]);
        await create(AccountsPageComponent);

        const rows = [...element.querySelectorAll<HTMLElement>('.access-row')];
        expect(api.users).toHaveBeenCalledWith(true);
        expect(api.groups).toHaveBeenCalledWith(true);
        expect(rows.map((row) => row.querySelector('strong')!.textContent)).toEqual(['admin', 'Anna', 'kids']);
        expect(rows[0].textContent).toContain('Server administrator');
        expect(rows[0].textContent).toContain('Opens every space');
        expect(
            [...rows[1].querySelectorAll('.access-memberships li')].map((li) =>
                [...li.querySelectorAll('span')].map((span) => span.textContent!.trim()).join(' '),
            ),
        ).toEqual(['Library Administrators', 'Work Users']);
        expect(rows[2].textContent).toContain('Guest account');
        expect(rows[2].textContent).toContain('Disabled');
        expect(rows[2].textContent).toContain('Locked');
        expect(rows[2].textContent).toContain('In no space');
        expect(buttonNamed(rows[0], 'Delete')).toBeUndefined();
    });

    it('creates an account in several spaces, with its groups in each', async () => {
        api.users.mockResolvedValue([]);
        api.groups.mockResolvedValue([...library, ...work]);
        api.tenants.mockResolvedValue([space(LIBRARY.id, 'Library'), space(WORK.id, 'Work')]);
        api.createUser.mockResolvedValue(user(4, 'bob'));
        await create(AccountsPageComponent);

        buttonNamed(element, 'New account')!.click();
        await settle();
        expect(element.textContent).toContain('In no space yet');
        type('#account-name', ' bob ');
        type('#account-password', 'bob-password');
        choose('#account-add-space', String(LIBRARY.id));
        await settle();
        choose('#account-add-space', String(WORK.id));
        await settle();
        const spaces = [...element.querySelectorAll<HTMLElement>('.access-space')];
        expect(spaces.map((fieldset) => fieldset.querySelector('.access-space-name')!.textContent)).toEqual([
            'Library',
            'Work',
        ]);
        expect(element.querySelector('#account-add-space')).toBeNull();
        checkbox('.access-chip', 'Administrators', spaces[0]).click();
        checkbox('.access-chip', 'Users', spaces[0]).click();
        await settle();
        buttonNamed(element, 'Create')!.click();
        await settle();

        expect(api.createUser).toHaveBeenCalledWith({
            userName: 'bob',
            displayName: null,
            password: 'bob-password',
            kind: 'user',
            isServerAdmin: false,
            disabled: false,
            unlock: false,
            groupIds: [21, 10],
        });
        expect(api.users).toHaveBeenCalledTimes(2);
    });

    it('takes an account out of a space with its groups there, keeping the others', async () => {
        const anna = user(2, 'anna', { groups: [member(library[1]), member(work[1])] });
        api.users.mockResolvedValue([anna]);
        api.groups.mockResolvedValue([...library, ...work]);
        api.tenants.mockResolvedValue([space(LIBRARY.id, 'Library'), space(WORK.id, 'Work')]);
        api.updateUser.mockResolvedValue(anna);
        await create(AccountsPageComponent);

        buttonNamed(element, 'Edit')!.click();
        await settle();
        const workSpace = [...element.querySelectorAll<HTMLElement>('.access-space')][1];
        buttonNamed(workSpace, 'Remove from this space')!.click();
        await settle();
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.updateUser).toHaveBeenCalledWith(2, expect.objectContaining({ groupIds: [11] }));
    });

    it('warns about a space where the account is in no group', async () => {
        api.users.mockResolvedValue([user(2, 'anna', { groups: [member(library[1])] })]);
        api.groups.mockResolvedValue(library);
        api.tenants.mockResolvedValue([space(LIBRARY.id, 'Library')]);
        await create(AccountsPageComponent);

        buttonNamed(element, 'Edit')!.click();
        await settle();
        checkbox('.access-chip', 'Users').click();
        await settle();

        expect(element.querySelector('.access-space')!.classList).toContain('empty');
        expect(element.querySelector('.access-space')!.textContent).toContain("won't see Library");
    });

    it("lets a space's administrator change only the groups here of an account other spaces share", async () => {
        const shared = user(4, 'carol', { groups: [member(library[1]), { tenantId: 9, groupId: 90, group: 'Users' }] });
        api.users.mockResolvedValue([shared]);
        api.groups.mockResolvedValue(library);
        api.updateUser.mockResolvedValue(shared);
        await create(AccountsPageComponent, limitedConfig([Rights.browse, Rights.manageMembers]));

        expect(api.users).toHaveBeenCalledWith(false);
        expect(api.groups).toHaveBeenCalledWith(false);
        expect(api.tenants).not.toHaveBeenCalled();
        expect(element.textContent).toContain('and another space');
        expect(buttonNamed(element, 'Remove')).toBeDefined();
        buttonNamed(element, 'Edit')!.click();
        await settle();

        expect(element.querySelector<HTMLInputElement>('#account-name')!.disabled).toBe(true);
        expect(element.textContent).not.toContain('Server administrator');
        expect(element.querySelector('#account-add-space')).toBeNull();
        expect(element.textContent).toContain('Also in another space, kept as it is');
        checkbox('.access-chip', 'Administrators').click();
        await settle();
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.updateUser).toHaveBeenCalledWith(4, expect.objectContaining({ groupIds: [11, 10] }));
    });

    it('shows the errors by their fields', async () => {
        api.users.mockResolvedValue([]);
        api.groups.mockResolvedValue(library);
        api.tenants.mockResolvedValue([space(LIBRARY.id, 'Library')]);
        api.createUser.mockRejectedValue(
            new ApiError(400, 'x', { userName: ['There is already an account named “bob”.'] }),
        );
        await create(AccountsPageComponent);

        buttonNamed(element, 'New account')!.click();
        await settle();
        buttonNamed(element, 'Create')!.click();
        await settle();

        expect(element.querySelector('.field-error')!.textContent).toContain('already an account');
    });
});

describe('SpacesPageComponent', () => {
    const library = builtIns(LIBRARY.id, 10);
    const work = builtIns(WORK.id, 20);
    const anna = user(2, 'anna', { groups: [member(library[0]), member(work[1])] });
    const tom = user(3, 'tom', { groups: [member(work[1])] });
    const dave = user(5, 'dave', { groups: [member(library[1])] });

    function serverSpaces(): void {
        api.tenants.mockResolvedValue([
            space(LIBRARY.id, 'Library', { members: 1, groups: 1 }),
            space(WORK.id, 'Work', { members: 2, groups: 4, guestAccess: true }),
        ]);
        api.users.mockResolvedValue([anna, tom, dave]);
        api.rights.mockResolvedValue(catalog);
        api.tenantAccess.mockResolvedValue({
            id: LIBRARY.id,
            name: 'Library',
            guestAccess: false,
            anonymousGuestsAllowed: true,
            disabledRights: [],
        });
    }

    async function openSpace(name: string): Promise<void> {
        [...element.querySelectorAll<HTMLButtonElement>('.space-card')]
            .find((card) => card.textContent!.includes(name))!
            .click();
        await settle();
        await settle();
    }

    it('shows what a space holds and every space as a card', async () => {
        serverSpaces();
        await create(SpacesPageComponent);

        expect(element.querySelector('.access-model')!.textContent).toContain('A library');
        const cards = [...element.querySelectorAll<HTMLElement>('.space-card')];
        expect(cards.map((card) => card.querySelector('strong')!.textContent)).toEqual(['Library', 'Work']);
        expect(cards[0].textContent).toContain('You are here');
        expect(cards[0].textContent).toContain('D:\\Media\\Library');
        expect(cards[0].textContent).toMatch(/1 group/);
        expect(cards[0].textContent).toMatch(/1 account/);
        expect(cards[1].textContent).toContain('4 groups');
        expect(cards[1].textContent).toContain('2 accounts');
        expect(cards[1].textContent).toContain('Visitors let in');
    });

    it('opens another space: its library, and links to its groups, accounts, folder rules and visitors', async () => {
        serverSpaces();
        await create(SpacesPageComponent);

        await openSpace('Work');

        expect(TestBed.inject(Router).url).toContain(`space=${WORK.id}`);
        expect(element.querySelector('.space-title h3')!.textContent).toBe('Work');
        expect(element.querySelector('.access-library')!.textContent).toContain('D:\\Media\\Work');
        const links = [...element.querySelectorAll<HTMLAnchorElement>('.space-detail a.space-card')];
        expect(links.map((link) => link.querySelector('strong')!.textContent)).toEqual([
            'Groups',
            'Accounts',
            'Folder access',
            'Visitors',
        ]);
        expect(links.map((link) => new URL(link.href).search)).toEqual(
            ['groups', 'accounts', 'folders', 'visitors'].map((page) => `?section=access&page=${page}&space=2`),
        );
        expect(links[3].textContent).toContain('Visitors are let in');
        expect(api.groups).not.toHaveBeenCalled();

        buttonNamed(element, 'All spaces')!.click();
        await settle();
        expect(element.querySelectorAll('.space-card')).toHaveLength(2);
    });

    it('switches off at once the rights that change files, or every right that changes something', async () => {
        serverSpaces();
        api.groups.mockResolvedValue(work);
        api.updateTenant.mockResolvedValue(space(WORK.id, 'Work'));
        await create(SpacesPageComponent);
        await openSpace('Work');

        buttonNamed(element, 'Edit space')!.click();
        await settle();
        buttonNamed(element, 'Files read-only')!.click();
        await settle();
        expect(checkbox('.access-choices.rights label', Rights.editImages).checked).toBe(true);
        expect(checkbox('.access-choices.rights label', Rights.manageTags).checked).toBe(false);
        buttonNamed(element.querySelector('sfe-space-editor')!, 'Save')!.click();
        await settle();
        expect(api.updateTenant).toHaveBeenLastCalledWith(
            WORK.id,
            expect.objectContaining({ disabledRights: [Rights.editImages] }),
        );

        buttonNamed(element, 'Edit space')!.click();
        await settle();
        buttonNamed(element, 'Read-only')!.click();
        await settle();
        buttonNamed(element.querySelector('sfe-space-editor')!, 'Save')!.click();
        await settle();
        expect(api.updateTenant).toHaveBeenLastCalledWith(
            WORK.id,
            expect.objectContaining({ disabledRights: [Rights.editImages, Rights.manageTags] }),
        );
    });

    it('creates a space on a folder picked inside the allowed roots, then opens it', async () => {
        serverSpaces();
        api.groups.mockResolvedValue(work);
        api.tenantFolders.mockImplementation(async (path) =>
            path
                ? { path, parent: null, folders: [] }
                : { path: null, parent: null, folders: [{ name: 'D:\\Media', path: 'D:\\Media' }] },
        );
        api.createTenant.mockImplementation(async () => {
            api.tenants.mockResolvedValue([space(LIBRARY.id, 'Library'), space(WORK.id, 'Work')]);
            return space(WORK.id, 'Work');
        });
        api.tenants.mockResolvedValueOnce([space(LIBRARY.id, 'Library')]);
        await create(SpacesPageComponent);

        buttonNamed(element, 'New space')!.click();
        await settle();
        buttonNamed(element, 'D:\\Media')!.click();
        await settle();
        type('#space-name', 'Work');
        await settle();
        buttonNamed(element, 'Create')!.click();
        await settle();
        await settle();

        expect(api.tenantFolders).toHaveBeenCalledWith('D:\\Media');
        expect(api.createTenant).toHaveBeenCalledWith({
            name: 'Work',
            slug: null,
            root: 'D:\\Media',
            guestAccess: false,
            disabled: false,
            disabledRights: [],
        });
        expect(api.config).toHaveBeenCalledTimes(2);
        expect(element.querySelector('.space-title h3')!.textContent).toBe('Work');
    });

    it("shows a space's administrator their own space directly", async () => {
        api.tenantAccess.mockResolvedValue({
            id: LIBRARY.id,
            name: 'Library',
            guestAccess: false,
            anonymousGuestsAllowed: false,
            disabledRights: [],
        });
        await create(SpacesPageComponent, limitedConfig([Rights.browse, Rights.manageMembers]));
        await settle();

        expect(api.tenants).not.toHaveBeenCalled();
        expect(element.querySelector('.space-title h3')!.textContent).toBe('Library');
        expect(buttonNamed(element, 'All spaces')).toBeUndefined();
        expect(buttonNamed(element, 'Edit space')).toBeUndefined();
        const visitors = [...element.querySelectorAll<HTMLElement>('a.space-card')][3];
        expect(visitors.textContent).toContain("Only the space's accounts may open it.");
    });
});

describe('FolderAccessComponent', () => {
    const groups = builtIns(LIBRARY.id, 10);
    const tom = user(3, 'tom', { groups: [member(groups[1])] });
    const rule = (id: number, path: string, overrides: Partial<FolderRuleView> = {}): FolderRuleView => ({
        id,
        path,
        groupId: 11,
        userId: null,
        name: 'Users',
        level: 'hidden',
        exists: true,
        created: '2026-10-16T00:00:00Z',
        ...overrides,
    });

    async function show(rules: FolderRuleView[]): Promise<void> {
        api.folderRules.mockResolvedValue(rules);
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig());
        const fixture = TestBed.createComponent(FolderAccessComponent);
        fixture.componentRef.setInput('groups', groups);
        fixture.componentRef.setInput('members', [tom]);
        element = fixture.nativeElement;
        await settle();
    }

    it('lists the rules by folder, and changes or removes one', async () => {
        await show([
            rule(1, 'Private'),
            rule(2, 'Private', { groupId: null, userId: 3, name: 'tom', level: 'read' }),
            rule(3, 'Old', { exists: false }),
        ]);
        api.setFolderRule.mockResolvedValue([rule(1, 'Private', { level: 'read' })]);

        const rows = [...element.querySelectorAll<HTMLElement>('.folder-rules > .access-row')];
        expect(rows.map((row) => row.querySelector('strong')!.textContent)).toEqual(['Old', 'Private']);
        expect(rows[0].textContent).toContain('Folder not found');
        expect(rows[1].querySelectorAll('.folder-rule-principals li')).toHaveLength(2);
        // each select shows its own rule's level, not the first option
        expect(element.querySelector<HTMLSelectElement>('select[aria-label="Level of tom at Private"]')!.value).toBe(
            'read',
        );

        choose('select[aria-label="Level of Users at Private"]', 'read');
        await settle();
        expect(api.setFolderRule).toHaveBeenCalledWith(
            { path: 'Private', groupId: 11, userId: null, level: 'read' },
            null,
        );

        choose('select[aria-label="Level of Users at Private"]', '');
        await settle();
        expect(api.setFolderRule).toHaveBeenLastCalledWith(
            { path: 'Private', groupId: 11, userId: null, level: null },
            null,
        );
    });

    it('sets a new rule on a folder picked among every folder of the space', async () => {
        await show([]);
        api.spaceFolders.mockImplementation(async (path) =>
            path
                ? { path, parent: '', folders: [] }
                : { path: '', parent: null, folders: [{ name: 'Kids', path: 'Kids' }] },
        );
        api.setFolderRule.mockResolvedValue([
            rule(1, 'Kids', { userId: 3, groupId: null, name: 'tom', level: 'write' }),
        ]);

        expect(element.textContent).toContain('No folder rules');
        buttonNamed(element, 'New rule')!.click();
        await settle();
        buttonNamed(element, 'Kids')!.click();
        await settle();
        choose('#folder-rule-principal', 'u3');
        choose('#folder-rule-level', 'write');
        buttonNamed(element, 'Set the rule')!.click();
        await settle();

        expect(api.setFolderRule).toHaveBeenCalledWith(
            { path: 'Kids', groupId: null, userId: 3, level: 'write' },
            null,
        );
        expect(element.querySelector('.folder-rules strong')!.textContent).toBe('Kids');
    });

    it('tells who has access to a folder, and re-points the rules of one that is gone', async () => {
        await show([rule(1, 'Private'), rule(2, 'Gone', { exists: false })]);
        api.folderAccess.mockResolvedValue({
            path: 'Private',
            exists: true,
            groups: [],
            accounts: [
                {
                    groupId: null,
                    userId: 3,
                    name: 'tom',
                    level: null,
                    explicit: false,
                    sees: false,
                    changesFiles: false,
                },
            ],
            visitors: null,
        });
        api.spaceFolders.mockImplementation(async (path) =>
            path
                ? { path, parent: '', folders: [] }
                : { path: '', parent: null, folders: [{ name: 'Moved', path: 'Moved' }] },
        );
        api.repointFolderRules.mockResolvedValue([rule(1, 'Private'), rule(2, 'Moved')]);

        buttonNamed(element, 'Who has access')!.click();
        await settle();
        expect(element.querySelector('.folder-access-report')!.textContent).toContain('tom');
        expect(api.folderAccess).toHaveBeenCalledWith('Private', null);

        buttonNamed(element, 'Re-point…')!.click();
        await settle();
        buttonNamed(element, 'Moved')!.click();
        await settle();
        buttonNamed(element, 'Move the rules here')!.click();
        await settle();
        expect(api.repointFolderRules).toHaveBeenCalledWith('Gone', 'Moved', null);
    });
});

describe('AccountSectionComponent', () => {
    it('changes the password of the signed-in account', async () => {
        api.changePassword.mockResolvedValue({} as SessionInfo);
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        const fixture = TestBed.createComponent(AccountSectionComponent);
        fixture.componentRef.setInput('tab', 'password');
        element = fixture.nativeElement;
        await settle();

        type('#password-current', 'old-password');
        type('#password-new', 'new-password');
        type('#password-confirm', 'new-password');
        await settle();
        buttonNamed(element, 'Change the password')!.click();
        await settle();

        expect(api.changePassword).toHaveBeenCalledWith('old-password', 'new-password');
        expect(element.querySelector<HTMLInputElement>('#password-current')!.value).toBe('');
    });

    it('edits only the preferences users set for themselves', async () => {
        api.preferences.mockResolvedValue({
            values: settingsValues(),
            customized: false,
            level: 'user',
            keys: {
                'player.seekSeconds': {
                    scope: 'user',
                    source: 'server',
                    editable: true,
                    lockable: false,
                    locked: false,
                    lockedBy: null,
                    inherited: 10,
                },
                'player.loop': {
                    scope: 'user',
                    source: 'tenant',
                    editable: false,
                    lockable: false,
                    locked: false,
                    lockedBy: 'tenant',
                    inherited: false,
                },
                'scan.computeHashes': {
                    scope: 'tenant',
                    source: 'environment',
                    editable: false,
                    lockable: false,
                    locked: false,
                    lockedBy: null,
                    inherited: true,
                },
            },
        });
        api.savePreferenceChanges.mockResolvedValue({
            values: settingsValues(),
            customized: true,
            level: 'user',
            keys: null,
        });
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        const fixture = TestBed.createComponent(AccountSectionComponent);
        fixture.componentRef.setInput('tab', 'preferences');
        element = fixture.nativeElement;
        await settle();

        const seek = element.querySelector<HTMLInputElement>('#setting-player-seekSeconds')!;
        expect(seek.closest<HTMLElement>('.field')!.hidden).toBe(false);
        expect(element.querySelector<HTMLInputElement>('#setting-player-loop')!.disabled).toBe(true);
        expect(element.querySelector('#setting-player-loop')!.closest('.setting-check')!.textContent).toContain(
            'Locked by the space',
        );
        expect(element.querySelector('#setting-scan-computeHashes')).toBeNull();

        seek.value = '30';
        seek.dispatchEvent(new Event('input'));
        await settle();
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.savePreferenceChanges).toHaveBeenCalledWith({ values: { 'player.seekSeconds': 30 } });
    });
});
