import { TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api-error';
import { FolderAccessEntry } from '../../core/access.models';
import { FolderAccessReport } from '../../access/access.models';
import { group, user } from '../../../testing/access';
import { ApiMock, buttonNamed, mockApi, settle, testProviders } from '../../../testing/fakes';
import { WizardHarness } from '../../../testing/wizard-harness';
import { ShareFolderDraft } from './share-folder-draft';
import { SHARE_FOLDER_WIZARD } from './share-folder.wizard';

let api: ApiMock;

function entry(name: string, values: Partial<FolderAccessEntry>): FolderAccessEntry {
    return {
        groupId: null,
        userId: null,
        name,
        level: null,
        explicit: false,
        sees: true,
        changesFiles: true,
        ...values,
    };
}

const report: FolderAccessReport = {
    path: 'Photos/Private',
    exists: true,
    groups: [entry('Kids', { groupId: 5, level: 'read', explicit: true, changesFiles: false })],
    accounts: [
        entry('Ann', { userId: 1 }),
        entry('Tom', { userId: 2, sees: false, changesFiles: false }),
        entry('Lea', { userId: 3, changesFiles: false }),
    ],
    visitors: null,
};

async function open(): Promise<WizardHarness> {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    api.spaceFolders.mockImplementation((path) =>
        Promise.resolve(
            path === null
                ? { path: '', parent: null, folders: [{ name: 'Photos', path: 'Photos' }] }
                : { path, parent: '', folders: [{ name: 'Private', path: 'Photos/Private' }] },
        ),
    );
    api.groups.mockResolvedValue([group(5, 'Kids'), group(6, 'Parents')]);
    api.users.mockResolvedValue([user(1, 'ann', { displayName: 'Ann' }), user(2, 'tom')]);
    api.folderAccess.mockResolvedValue(report);
    api.setFolderRule.mockResolvedValue([]);
    return WizardHarness.open(SHARE_FOLDER_WIZARD);
}

/** Opens `name` in the folder picker. */
async function pick(wizard: WizardHarness, name: string): Promise<void> {
    buttonNamed(wizard.element.querySelector('.folder-picker')!, name)!.click();
    await settle();
}

/** Clicks the radio button labelled `label`. */
async function choose(wizard: WizardHarness, label: string): Promise<void> {
    const labels = [...wizard.element.querySelectorAll<HTMLLabelElement>('label.check')];
    const found = labels.find((element) => element.textContent!.trim().startsWith(label));
    found!.querySelector('input')!.click();
    await settle();
}

describe('the share-a-folder wizard', () => {
    it('picks a folder below the top, checked with the server without setting anything', async () => {
        const wizard = await open();
        expect(wizard.button('Next')!.disabled).toBe(true);

        await pick(wizard, 'Photos');
        await pick(wizard, 'Private');
        await wizard.next();

        expect(api.setFolderRule).toHaveBeenCalledExactlyOnceWith(
            { path: 'Photos/Private', groupId: null, userId: null, level: null },
            null,
            true,
        );
        expect(wizard.title()).toBe('Who');
    });

    it('lists the groups and the accounts, and refuses one the server no longer has on its step', async () => {
        const wizard = await open();
        await wizard.fill({ path: 'Photos' });
        await wizard.next();
        expect(wizard.element.querySelector('.wizard-fields')!.textContent).toContain('Parents');
        await choose(wizard, 'tom');
        expect(wizard.draft<ShareFolderDraft>()).toMatchObject({ who: 'u2', whoName: 'tom' });
        api.setFolderRule.mockRejectedValueOnce(
            new ApiError(400, 'Refused', { userId: ["The account isn't in this space."] }),
        );

        await wizard.next();

        expect(wizard.title()).toBe('Who');
        expect(wizard.errors()).toEqual(["The account isn't in this space."]);
    });

    it("says what the group has there today, and warns that Hidden doesn't hide from an account's groups", async () => {
        const wizard = await open();
        await wizard.fill({ path: 'Photos/Private', who: 'g5', whoName: 'Kids' });
        await wizard.next();
        await wizard.next();

        expect(wizard.title()).toBe('Level');
        expect(wizard.element.querySelector('.share-folder-now')!.textContent).toBe(
            'Today “Kids” has Read only from a rule on this folder.',
        );
        await wizard.press('Back');
        await choose(wizard, 'tom');
        await wizard.next();
        await choose(wizard, 'Hidden');
        expect(wizard.element.querySelector('.wizard-fields')!.textContent).toContain('none of its groups sees');
    });

    it('sets the rule on Finish only, then shows who has access', async () => {
        const wizard = await open();
        await wizard.fill({ path: 'Photos/Private', who: 'g5', whoName: 'Kids' });
        await wizard.next();
        await wizard.next();
        await choose(wizard, 'Read & write');
        await wizard.next();
        const review = [...wizard.element.querySelectorAll('.wizard-review-part dd')].map((dd) => dd.textContent);
        expect(review).toEqual(['Photos/Private', 'Kids', 'Read & write']);
        api.setFolderRule.mockClear();

        await wizard.next();

        expect(api.setFolderRule).toHaveBeenCalledExactlyOnceWith({
            path: 'Photos/Private',
            groupId: 5,
            userId: null,
            level: 'write',
        });
        expect(wizard.title()).toBe('Done');
        expect(wizard.element.querySelector('[role="status"]')!.textContent).toBe(
            '“Kids” now has Read & write on “Photos/Private” and everything below it.',
        );
        const lines = [...wizard.element.querySelectorAll('.wizard-end-details dd')].map((dd) => dd.textContent);
        expect(lines).toEqual([
            'Sees it and may change its files',
            "Doesn't see it",
            'Sees it, may not change its files',
        ]);
        expect(wizard.element.querySelector('.wizard-links a')!.getAttribute('href')).toBe(
            '/admin?section=access&page=folders',
        );
    });
});
