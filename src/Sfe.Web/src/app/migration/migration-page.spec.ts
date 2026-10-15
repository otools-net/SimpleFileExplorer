import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ApiMock, buttonNamed, mockApi, settle, testProviders } from '../../testing/fakes';
import { ApiError } from '../core/api.service';
import { MigrationInfo } from '../core/models';
import { changelogParts } from './changelog';
import { MIGRATION_POLL_MS, MigrationPageComponent } from './migration-page.component';

function migration(overrides: Partial<MigrationInfo> = {}): MigrationInfo {
    return {
        status: 'required', appVersion: '1.37.0', databaseVersion: '1.35.0', log: [], error: null, codeConfigured: true, codeTooShort: false,
        minimumCodeLength: 12, hosting: 'other',
        changes: [
            { version: '1.37.0', sections: [{ title: 'Added', items: ['An **update** page, with `code`.'] }] },
            { version: '1.36.0', sections: [{ title: 'Added', items: ['Search by `type:`.'] }, { title: 'Fixed', items: ['A bug.'] }] },
        ],
        ...overrides,
    };
}

describe('MigrationPageComponent', () => {
    let api: ApiMock;
    let element: HTMLElement;
    let navigate: ReturnType<typeof vi.spyOn>;

    async function create(info: MigrationInfo): Promise<void> {
        api = mockApi();
        api.migrationInfo.mockResolvedValue(info);
        TestBed.configureTestingModule({ providers: testProviders(api) });
        navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const fixture = TestBed.createComponent(MigrationPageComponent);
        element = fixture.nativeElement;
        await settle();
    }

    function typeCode(value: string): void {
        const input = element.querySelector<HTMLInputElement>('#migration-code')!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
    }

    afterEach(() => {
        TestBed.resetTestingModule();
        vi.restoreAllMocks();
    });

    it('lists the changes since the installed version before anything is changed', async () => {
        await create(migration());

        expect(element.textContent).toContain('SFE 1.35.0');
        expect([...element.querySelectorAll('.migration-version h3')].map(h => h.textContent)).toEqual(['SFE 1.37.0', 'SFE 1.36.0']);
        expect(element.querySelector('.migration-version li strong')!.textContent).toBe('update');
        expect(element.querySelector('.migration-version li code')!.textContent).toBe('code');
        expect(api.startMigration).not.toHaveBeenCalled();
        expect(buttonNamed(element, 'Start the update')!.disabled).toBe(true);
    });

    it('starts the update with the setup code after asking, follows it, then opens the library', async () => {
        await create(migration());
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
        api.startMigration.mockResolvedValue(migration({ status: 'running' }));
        api.migrationInfo.mockResolvedValue(migration({ status: 'running', log: ['Updating the accounts\' database…'] }));

        typeCode(' the-setup-code ');
        await settle();
        buttonNamed(element, 'Start the update')!.click();
        await settle();

        expect(confirm).toHaveBeenCalled();
        expect(api.startMigration).toHaveBeenCalledWith('the-setup-code');
        expect(element.querySelector('.migration-running')).not.toBeNull();
        expect(element.querySelector('.migration-log')!.textContent).toContain('Updating the accounts');

        api.migrationInfo.mockResolvedValue(migration({ status: 'none', databaseVersion: '1.37.0', changes: [], log: ['The install now belongs to SFE 1.37.0.'] }));
        await settle(MIGRATION_POLL_MS + 50);

        expect(element.querySelector('h1')!.textContent).toBe('SFE is up to date');
        expect(navigate).not.toHaveBeenCalled();
        buttonNamed(element, 'Open SFE')!.click();
        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });

    it('does nothing when the update is not confirmed', async () => {
        await create(migration());
        vi.spyOn(window, 'confirm').mockReturnValue(false);

        typeCode('the-setup-code');
        await settle();
        buttonNamed(element, 'Start the update')!.click();
        await settle();

        expect(api.startMigration).not.toHaveBeenCalled();
    });

    it('shows a wrong code under its field', async () => {
        await create(migration());
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        api.startMigration.mockRejectedValue(new ApiError(400, 'This is not the server\'s setup code.', { code: ['This is not the server\'s setup code.'] }));

        typeCode('wrong');
        await settle();
        buttonNamed(element, 'Start the update')!.click();
        await settle();

        expect(element.querySelector('.field-error')!.textContent).toContain('not the server\'s setup code');
    });

    it('downloads a backup with the setup code', async () => {
        await create(migration());
        const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => { });
        api.migrationBackup.mockResolvedValue({ url: 'api/migration/backup/abc', fileName: 'SFE-backup-1.35.0.zip', size: 2048 });

        typeCode('the-setup-code');
        await settle();
        buttonNamed(element, 'Download a backup')!.click();
        await settle();

        expect(api.migrationBackup).toHaveBeenCalledWith('the-setup-code');
        expect(click).toHaveBeenCalled();
        const anchor = click.mock.contexts[0] as HTMLAnchorElement;
        expect(anchor.getAttribute('href')).toBe('api/migration/backup/abc');
        expect(anchor.download).toBe('SFE-backup-1.35.0.zip');
        expect(element.querySelector('[role=status]')!.textContent).toContain('SFE-backup-1.35.0.zip');
        expect(api.startMigration).not.toHaveBeenCalled();
    });

    it('says how to give the server a setup code when it has none', async () => {
        await create(migration({ codeConfigured: false, hosting: 'docker' }));

        expect(element.querySelector('#migration-code')).toBeNull();
        expect(element.querySelector('.account-steps h2')!.textContent).toBe('Container');
        expect(buttonNamed(element, 'Check again')).toBeDefined();
    });

    it('says where to find the code the server made itself', async () => {
        await create(migration({ codeGenerated: true, codeFile: 'data/setup-code.txt', hosting: 'appImage' }));

        const hint = element.querySelector('#migration-code-hint')!.textContent!;
        expect(hint).toContain('The code SFE made at its setup');
        expect(hint).toContain('~/.config/sfe/data/setup-code.txt');
    });

    it('offers only a backup when the install belongs to a later version', async () => {
        await create(migration({ status: 'newer', databaseVersion: '2.0.0', changes: [] }));

        expect(element.textContent).toContain('a later version');
        expect(buttonNamed(element, 'Download a backup')).toBeDefined();
        expect(buttonNamed(element, 'Start the update')).toBeUndefined();
    });

    it('leaves for the library when no update is due', async () => {
        await create(migration({ status: 'none', changes: [] }));

        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });
});

describe('changelogParts', () => {
    it('splits code, bold and links out of a changelog item', () => {
        expect(changelogParts('Use `d:>10m` in **Search**, see [the docs](docs/x.md).')).toEqual([
            { text: 'Use ', kind: 'text' },
            { text: 'd:>10m', kind: 'code' },
            { text: ' in ', kind: 'text' },
            { text: 'Search', kind: 'strong' },
            { text: ', see ', kind: 'text' },
            { text: 'the docs', kind: 'text' },
            { text: '.', kind: 'text' },
        ]);
        expect(changelogParts('Plain.')).toEqual([{ text: 'Plain.', kind: 'text' }]);
    });
});
