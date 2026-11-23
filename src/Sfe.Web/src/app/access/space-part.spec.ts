import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Rights } from '../core/rights';
import { builtIns, catalog, group, member, space, user, WORK } from '../../testing/access';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    LIBRARY,
    limitedConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
} from '../../testing/fakes';
import { SpacePart, SpacePartComponent } from './space-part.component';

let api: ApiMock;
let element: HTMLElement;

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

/** The sub-page `part` at `address` (its query: `?space=2`). */
async function create(part: SpacePart, address = '/', config = fakeConfig()): Promise<void> {
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    await TestBed.inject(Router).navigateByUrl(address);
    const fixture = TestBed.createComponent(SpacePartComponent);
    fixture.componentRef.setInput('part', part);
    element = fixture.nativeElement;
    await settle();
    await settle();
}

function choose(selector: string, value: string): void {
    const select = element.querySelector<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
}

function checkbox(selector: string, text: string, root: ParentNode = element): HTMLInputElement {
    return [...root.querySelectorAll<HTMLLabelElement>(selector)]
        .find((label) => label.textContent!.includes(text))!
        .querySelector('input')!;
}

beforeEach(() => (api = mockApi()));
afterEach(() => vi.restoreAllMocks());

describe('SpacePartComponent', () => {
    it("shows the groups of the session's space, and another space picked above", async () => {
        serverSpaces();
        api.groups.mockResolvedValue(library);
        await create('groups');

        expect(api.groups).toHaveBeenLastCalledWith(false, null);
        expect(api.rights).toHaveBeenLastCalledWith(null);
        const picker = element.querySelector<HTMLSelectElement>('.space-pick select')!;
        expect([...picker.options].map((option) => option.textContent)).toEqual(['Library', 'Work']);
        expect(picker.value).toBe(String(LIBRARY.id));

        api.groups.mockResolvedValue([
            ...work,
            group(23, 'Kids', { tenantId: WORK.id, rights: [Rights.browse], memberIds: [3] }),
        ]);
        choose('.space-pick select', String(WORK.id));
        await settle();
        await settle();

        expect(TestBed.inject(Router).url).toContain(`space=${WORK.id}`);
        expect(api.groups).toHaveBeenLastCalledWith(false, WORK.id);
        expect(api.rights).toHaveBeenLastCalledWith(WORK.id);
        const groups = element.querySelector<HTMLElement>('.access-card')!;
        expect([...groups.querySelectorAll('.access-row strong')].map((name) => name.textContent)).toEqual([
            'Administrators',
            'Users',
            'Guests',
            'Kids',
        ]);
        expect(groups.textContent).toContain('used for visitors without an account');
        // one line each, the parts kept apart by their separators
        const details = [...groups.querySelectorAll('.access-row-detail')].map((detail) =>
            detail.textContent!.replace(/\s+/g, ' ').trim(),
        );
        expect(details[3]).toBe('1 right · 1 account');
        expect(groups.querySelectorAll('.access-row-detail > *').length).toBe(4);
    });

    it("lists every account, or a space's, which loses one and gains one of another space", async () => {
        serverSpaces();
        api.groups.mockResolvedValue([...library, ...work]);
        await create('accounts');

        const picker = element.querySelector<HTMLSelectElement>('.space-pick select')!;
        expect([...picker.options].map((option) => option.textContent)).toEqual(['Every space', 'Library', 'Work']);
        expect(element.querySelector('sfe-accounts-page')).not.toBeNull();
        expect(api.users).toHaveBeenLastCalledWith(true);

        api.groups.mockResolvedValue(work);
        api.updateUser.mockResolvedValue(anna);
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        choose('.space-pick select', String(WORK.id));
        await settle();
        await settle();

        const accounts = element.querySelector<HTMLElement>('.access-card')!;
        expect([...accounts.querySelectorAll('.access-row strong')].map((name) => name.textContent)).toEqual([
            'anna',
            'tom',
        ]);
        expect(accounts.textContent).toContain('Also in another space');
        expect([...accounts.querySelectorAll('select option')].map((option) => option.textContent)).toEqual([
            'Add an existing account…',
            'dave',
        ]);

        buttonNamed(accounts.querySelector('.access-row')!, 'Remove')!.click();
        await settle();
        expect(api.updateUser).toHaveBeenLastCalledWith(
            2,
            expect.objectContaining({ userName: 'anna', password: null, groupIds: [10] }),
        );

        choose('.access-add-existing', '5');
        await settle();
        expect(api.updateUser).toHaveBeenLastCalledWith(
            5,
            expect.objectContaining({ userName: 'dave', groupIds: [11, 21] }),
        );
    });

    it('lets visitors into the space picked, through the space itself', async () => {
        serverSpaces();
        api.setGuestAccess.mockResolvedValue({
            id: LIBRARY.id,
            name: 'Library',
            guestAccess: true,
            anonymousGuestsAllowed: true,
            disabledRights: [],
        });
        api.updateTenant.mockResolvedValue(space(WORK.id, 'Work'));
        await create('visitors');

        expect(api.groups).not.toHaveBeenCalled();
        const visitors = () => element.querySelector<HTMLElement>('.access-card')!;
        expect(visitors().querySelector('.field-hint')!.textContent!.replace(/\s+/g, ' ')).toContain(
            "only the space's accounts can open it.",
        );
        visitors().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
        await settle();
        expect(api.setGuestAccess).toHaveBeenCalledWith(true);

        choose('.space-pick select', String(WORK.id));
        await settle();
        await settle();
        expect(visitors().querySelector<HTMLInputElement>('input[type=checkbox]')!.checked).toBe(true);
        visitors().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
        await settle();
        expect(api.updateTenant).toHaveBeenCalledWith(WORK.id, {
            name: 'Work',
            guestAccess: false,
            disabled: false,
            disabledRights: null,
        });
    });

    it('never offers visitors the rights that change something, and creates groups in the space shown', async () => {
        serverSpaces();
        api.groups.mockResolvedValue(work.map((g) => (g.builtIn === 'guests' ? { ...g, rights: [Rights.browse] } : g)));
        api.createGroup.mockResolvedValue(group(24, 'Kids', { tenantId: WORK.id }));
        await create('groups', `/?space=${WORK.id}`);

        const groups = element.querySelector<HTMLElement>('.access-card')!;
        buttonNamed([...groups.querySelectorAll<HTMLElement>('.access-row')][2], 'Edit')!.click();
        await settle();
        expect(element.textContent).not.toContain(Rights.manageTenants);
        expect(checkbox('.access-choices.rights label', Rights.browse).checked).toBe(true);
        expect(checkbox('.access-choices.rights label', Rights.manageTags).disabled).toBe(true);
        expect(checkbox('.access-choices.rights label', Rights.manageTags).closest('label')!.textContent).toContain(
            'can never have this right',
        );
        expect(checkbox('.access-choices.rights label', Rights.download).closest('label')!.textContent).toContain(
            'Switched off for the whole server',
        );
        expect(element.querySelector<HTMLInputElement>('#group-name')!.disabled).toBe(true);
        expect(element.querySelector('.access-choices.members')).toBeNull();
        buttonNamed(element, 'Cancel')!.click();
        await settle();

        buttonNamed(element, 'New group')!.click();
        await settle();
        const name = element.querySelector<HTMLInputElement>('#group-name')!;
        name.value = 'Kids';
        name.dispatchEvent(new Event('input'));
        buttonNamed(element, 'Guests')!.click();
        await settle();
        checkbox('.access-choices.members label', 'tom').click();
        await settle();
        buttonNamed(element, 'Create')!.click();
        await settle();

        expect(api.createGroup).toHaveBeenCalledWith(
            { name: 'Kids', externalName: null, rights: [Rights.browse, Rights.download], memberIds: [3] },
            WORK.id,
        );
    });

    it('marks the rights that change things, those that change files the most, after the others', async () => {
        serverSpaces();
        api.groups.mockResolvedValue(work);
        await create('groups', `/?space=${WORK.id}`);

        buttonNamed(element, 'New group')!.click();
        await settle();
        const area = [...element.querySelectorAll<HTMLElement>('.access-choices.rights')].find(
            (candidate) => candidate.querySelector('legend')!.textContent === 'Library',
        )!;
        const titles = [...area.querySelectorAll('.right-title')].map((title) => title.firstChild!.textContent!.trim());
        const marks = (id: string) =>
            checkbox('.access-choices.rights label', id, area).closest('label')!.querySelector('.right-mark');

        expect(titles).toEqual([Rights.browse, Rights.download, Rights.editImages]);
        expect(marks(Rights.browse)).toBeNull();
        expect(marks(Rights.editImages)!.textContent).toBe('Changes files');
        expect(marks(Rights.editImages)!.classList).toContain('danger');
        expect(
            checkbox('.access-choices.rights label', Rights.manageTags).closest('label')!.querySelector('.right-mark')!
                .textContent,
        ).toBe('Changes things');
    });

    it('starts a rule on the folder of the address, among the rules of the space', async () => {
        serverSpaces();
        api.groups.mockResolvedValue(library);
        api.folderRules.mockResolvedValue([]);
        api.spaceFolders.mockImplementation(async (path) => ({
            path,
            parent: '',
            folders: [{ name: 'Kids', path: 'Photos/Kids' }],
        }));
        await create('folders', '/?folder=Photos');

        expect(api.folderRules).toHaveBeenCalledWith(null);
        expect(api.spaceFolders).toHaveBeenCalledWith('Photos', null);
        expect(element.querySelector('.folder-rule-form .folder-picker-path')!.textContent).toContain('Photos');

        choose('.space-pick select', String(WORK.id));
        await settle();
        expect(TestBed.inject(Router).url).not.toContain('folder=');
    });

    it("gives a space's administrator their own space, without a picker", async () => {
        api.tenantAccess.mockResolvedValue({
            id: LIBRARY.id,
            name: 'Library',
            guestAccess: false,
            anonymousGuestsAllowed: false,
            disabledRights: [],
        });
        api.groups.mockResolvedValue(library);
        api.users.mockResolvedValue([dave]);
        await create('visitors', '/', limitedConfig([Rights.browse, Rights.manageMembers]));

        expect(api.tenants).not.toHaveBeenCalled();
        expect(element.querySelector('.space-pick')).toBeNull();
        const visitors = element.querySelector<HTMLElement>('.access-card')!;
        expect(visitors.querySelector<HTMLInputElement>('input[type=checkbox]')!.disabled).toBe(true);
        expect(visitors.textContent).toContain('Sfe:Security:AnonymousGuests');
    });
});
