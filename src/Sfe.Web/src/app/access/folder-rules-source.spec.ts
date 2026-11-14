import { TestBed } from '@angular/core/testing';
import { ApiMock, buttonNamed, fakeConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { Api } from '../core/api.service';
import { ConnectionRuleView } from '../core/connection.models';
import { FolderAccessComponent } from './folder-access.component';
import { ConnectionRules, RuleGroup, RuleMember } from './folder-rules-source';

describe('the folder access card on a connection', () => {
    const groups: RuleGroup[] = [
        { id: 3, name: 'Users' },
        { id: 4, name: 'Kids' },
    ];
    const members: RuleMember[] = [{ id: 5, userName: 'tom', displayName: null }];
    const rule = (id: number, path: string, overrides: Partial<ConnectionRuleView> = {}): ConnectionRuleView => ({
        id,
        path,
        groupId: 3,
        userId: null,
        name: 'Users',
        level: 'hidden',
        itemId: `id:${path}`,
        ...overrides,
    });

    let api: ApiMock;
    let element: HTMLElement;

    async function show(rules: ConnectionRuleView[]): Promise<void> {
        api = mockApi();
        api.connectionRules.mockResolvedValue(rules);
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig());
        const fixture = TestBed.createComponent(FolderAccessComponent);
        fixture.componentRef.setInput('source', new ConnectionRules(api as unknown as Api, 8, 'Shared'));
        fixture.componentRef.setInput('groups', groups);
        fixture.componentRef.setInput('members', members);
        element = fixture.nativeElement;
        await settle();
    }

    function choose(selector: string, value: string): void {
        const select = element.querySelector<HTMLSelectElement>(selector)!;
        select.value = value;
        select.dispatchEvent(new Event('change'));
    }

    afterEach(() => TestBed.resetTestingModule());

    it("lists the connection's rules, named after it at its root, without the library's reports", async () => {
        await show([rule(1, ''), rule(2, 'Photos', { level: 'read' })]);

        const rows = [...element.querySelectorAll<HTMLElement>('.folder-rules > .access-row')];
        expect(api.connectionRules).toHaveBeenCalledWith(8);
        expect(rows.map((row) => row.querySelector('strong')!.textContent)).toEqual(['Shared', 'Photos']);
        expect(element.textContent).not.toContain('Folder not found');
        expect(buttonNamed(element, 'Who has access')).toBeUndefined();
        expect(api.folderRules).not.toHaveBeenCalled();
    });

    it('sends every rule when one changes or is removed, keeping their folder ids', async () => {
        await show([rule(1, 'Private'), rule(2, 'Photos', { groupId: null, userId: 5, name: 'tom', level: 'read' })]);
        api.setConnectionRules.mockImplementation(async (_, rules) =>
            rules.map((r, i) => ({ ...r, id: i + 10, name: 'x' })),
        );

        choose('select[aria-label="Level of Users at Private"]', 'write');
        await settle();
        expect(api.setConnectionRules).toHaveBeenLastCalledWith(8, [
            { path: 'Photos', groupId: null, userId: 5, level: 'read', itemId: 'id:Photos' },
            { path: 'Private', groupId: 3, userId: null, level: 'write', itemId: 'id:Private' },
        ]);

        choose('select[aria-label="Level of x at Photos"]', '');
        await settle();
        expect(api.setConnectionRules).toHaveBeenLastCalledWith(8, [
            { path: 'Private', groupId: 3, userId: null, level: 'write', itemId: 'id:Private' },
        ]);
    });

    it("adds a rule on a folder picked among the connection's folders", async () => {
        await show([]);
        api.connectionFolders.mockImplementation(async (_, path) =>
            path
                ? { path, parent: '', folders: [] }
                : { path: '', parent: null, folders: [{ name: 'Kids', path: 'Kids' }] },
        );
        api.setConnectionRules.mockResolvedValue([rule(1, 'Kids', { groupId: 4, name: 'Kids' })]);

        buttonNamed(element, 'New rule')!.click();
        await settle();
        expect(element.querySelector('.folder-picker-path')!.textContent).toContain('Shared');
        buttonNamed(element, 'Kids')!.click();
        await settle();
        choose('#folder-rule-principal', 'g4');
        buttonNamed(element, 'Set the rule')!.click();
        await settle();

        expect(api.connectionFolders).toHaveBeenCalledWith(8, '');
        expect(api.connectionFolders).toHaveBeenCalledWith(8, 'Kids');
        expect(api.setConnectionRules).toHaveBeenCalledWith(8, [
            { path: 'Kids', groupId: 4, userId: null, level: 'hidden', itemId: null },
        ]);
        expect(element.querySelector('.folder-rules strong')!.textContent).toBe('Kids');
    });
});
