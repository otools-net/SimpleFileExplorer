import { TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api-error';
import { space } from '../../../testing/access';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
    type,
} from '../../../testing/fakes';
import { WizardHarness } from '../../../testing/wizard-harness';
import { NewSpaceDraft } from './new-space-draft';
import { NEW_SPACE_WIZARD } from './new-space.wizard';

let api: ApiMock;

async function open(mode: 'local' | 'external' = 'external'): Promise<WizardHarness> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    api.tenantFolders.mockImplementation((path) =>
        Promise.resolve(
            path === null
                ? { path: null, parent: null, folders: [{ name: 'D:\\Media', path: 'D:\\Media' }] }
                : { path, parent: null, folders: [{ name: 'Holidays', path: 'D:\\Media\\Holidays' }] },
        ),
    );
    api.checkTenant.mockResolvedValue(undefined);
    await loadConfig(api, fakeConfig({ mode }));
    return WizardHarness.open(NEW_SPACE_WIZARD);
}

/** Opens `name` in the folder picker. */
async function pick(wizard: WizardHarness, name: string): Promise<void> {
    buttonNamed(wizard.element.querySelector('.folder-picker')!, name)!.click();
    await settle();
}

describe('the new-space wizard', () => {
    it('picks the folder, which names the space until a name is typed', async () => {
        const wizard = await open();
        expect(wizard.button('Next')!.disabled).toBe(true);

        await pick(wizard, 'D:\\Media');
        await pick(wizard, 'Holidays');

        expect(wizard.draft<NewSpaceDraft>()).toMatchObject({ root: 'D:\\Media\\Holidays', name: 'Holidays' });
        await wizard.next();
        const name = wizard.element.querySelector<HTMLInputElement>('#new-space-name')!;
        expect(name.value).toBe('Holidays');
        type(name, 'Summer');
        await wizard.press('Back');
        expect(wizard.element.querySelector('.folder-picker-path')!.textContent).toContain('D:\\Media\\Holidays');
        await pick(wizard, 'D:\\Media');
        expect(wizard.draft<NewSpaceDraft>()).toMatchObject({ root: 'D:\\Media', name: 'Summer' });
    });

    it("checks each step with the server's own rules, and shows a short name already taken on its field", async () => {
        const wizard = await open();
        await pick(wizard, 'D:\\Media');
        await wizard.next();
        type(wizard.element.querySelector<HTMLInputElement>('#new-space-slug')!, 'media');
        api.checkTenant.mockRejectedValueOnce(
            new ApiError(400, 'Refused', { slug: ['Another space has the short name “media”.'] }),
        );

        await wizard.next();

        expect(api.checkTenant).toHaveBeenLastCalledWith({
            name: 'Media',
            slug: 'media',
            root: 'D:\\Media',
            guestAccess: false,
            disabled: false,
            disabledRights: [],
        });
        expect(wizard.title()).toBe('Name');
        expect(wizard.errors()).toEqual(['Another space has the short name “media”.']);
    });

    it('creates the space on Finish only, then leads to its page', async () => {
        const wizard = await open();
        api.createTenant.mockResolvedValue(space(7, 'Holidays', { root: 'D:\\Media\\Holidays', guestAccess: true }));
        await pick(wizard, 'D:\\Media');
        await pick(wizard, 'Holidays');
        await wizard.next();
        await wizard.next();
        wizard.element.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
        await settle();
        await wizard.next();
        const review = [...wizard.element.querySelectorAll('.wizard-review-part dd')].map((dd) => dd.textContent);
        expect(review).toEqual([
            'D:\\Media\\Holidays',
            'Holidays',
            'Made from the name',
            "Let in, with the Guests group's rights",
        ]);
        expect(api.createTenant).not.toHaveBeenCalled();

        await wizard.next();

        expect(api.createTenant).toHaveBeenCalledWith(expect.objectContaining({ name: 'Holidays', guestAccess: true }));
        expect(wizard.title()).toBe('Done');
        expect(wizard.element.querySelector('.wizard-links a')!.getAttribute('href')).toBe(
            '/admin?section=access&page=spaces&space=7',
        );
        expect(api.config).toHaveBeenCalledTimes(2);
    });

    it('says visitors stay out of a server in Local mode', async () => {
        const wizard = await open('local');
        await wizard.fill({ root: 'D:\\Media', name: 'Media' });
        await wizard.next();
        await wizard.next();

        expect(wizard.title()).toBe('Visitors');
        expect(wizard.element.querySelector('.wizard-fields')!.textContent).toContain('Local mode');
    });
});
