import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
    accessToken as token,
    ApiMock,
    buttonNamed,
    fakeConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
} from '../../testing/fakes';
import { locateSettings, visibleSections } from '../admin/admin-sections';
import { Rights } from '../core/rights';
import { TABS } from '../settings/settings-schema';
import { AllTokensComponent } from './all-tokens.component';
import { expiryChoices, expiryOf, tokensConfig, tokenState } from './token-parts';

describe('AllTokensComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<AllTokensComponent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const rows = () => [...el().querySelectorAll<HTMLElement>('.token-row')];

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig());
        api.allTokens.mockResolvedValue([
            token({ lastUsed: '2026-11-10T08:00:00Z', lastUsedFrom: '192.0.2.7' }),
            token({ id: 2, userId: 4, userName: 'bob', name: 'Phone', expires: '2020-01-01T00:00:00Z' }),
        ]);
        fixture = TestBed.createComponent(AllTokensComponent);
        await settle();
    });

    afterEach(() => vi.restoreAllMocks());

    it('lists every token with its account, library and last use', () => {
        const [first, second] = rows();
        expect(first.textContent).toContain('admin');
        expect(first.textContent).toContain('Every space');
        expect(first.textContent).toContain('from 192.0.2.7');
        expect(second.textContent).toContain('bob');
        expect(second.textContent).toContain('Expired');
    });

    it('revokes a token of another account after asking', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        api.revokeAnyToken.mockResolvedValue(undefined);

        buttonNamed(rows()[1], 'Revoke')!.click();
        await settle();

        expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('of “bob”'));
        expect(api.revokeAnyToken).toHaveBeenCalledWith(2);
        expect(api.allTokens).toHaveBeenCalledTimes(2);
    });
});

describe('access token pages and parts', () => {
    const subpages = (rights: string[]) =>
        visibleSections((wanted) => wanted.some((right) => rights.includes(right))).flatMap((section) =>
            section.subpages.map((page) => `${section.id}/${page.id}`),
        );

    it('shows My account › Access tokens and the administrators’ list to those with their rights', () => {
        expect(subpages([Rights.ownPassword])).not.toContain('account/tokens');
        expect(subpages([Rights.ownTokens])).toContain('account/tokens');
        expect(subpages([Rights.ownTokens])).not.toContain('access/tokens');
        expect(subpages([Rights.manageTokens])).toContain('access/tokens');
        expect(subpages([Rights.serverSettings])).toContain('access/tokenSettings');
    });

    it('puts the three settings on their own settings sub-page', () => {
        const tab = TABS.find((candidate) => candidate.id === 'accessTokens')!;
        expect(tab.groups.flatMap((group) => group.fields.map((field) => field.key))).toEqual([
            'security.accessTokens.enabled',
            'security.accessTokens.maxLifetimeDays',
            'security.accessTokens.maxPerAccount',
        ]);
        expect(locateSettings('accessTokens')?.subpage.id).toBe('tokenSettings');
    });

    it('offers lives within the longest allowed, and never expiring only without a limit', () => {
        expect(expiryChoices(30).map((choice) => choice.days)).toEqual([7, null]);
        expect(expiryChoices(30).at(-1)!.label).toBe('30 days (the longest allowed)');
        expect(expiryChoices(0).at(-1)).toEqual({ days: null, label: 'Never expires' });
        expect(expiryChoices(undefined).at(-1)).toEqual({ days: null, label: 'The longest allowed' });
        expect(expiryOf(null)).toBeNull();
        expect(expiryOf(7, new Date('2026-11-01T00:00:00Z'))).toBe('2026-11-08T00:00:00.000Z');
    });

    it('reads whether tokens work from the configuration, off when it says nothing', () => {
        expect(tokensConfig(fakeConfig({ accessTokens: true }))).toEqual({ enabled: true });
        expect(
            tokensConfig(fakeConfig({ accessTokens: { enabled: true, maxLifetimeDays: 30 } }))?.maxLifetimeDays,
        ).toBe(30);
        expect(tokensConfig(fakeConfig({ accessTokens: false }))).toBeNull();
        expect(tokensConfig(fakeConfig({ accessTokens: { enabled: false } }))).toBeNull();
        expect(tokensConfig(fakeConfig())).toBeNull();
    });

    it('tells revoked and expired tokens apart', () => {
        const now = Date.parse('2026-11-14T00:00:00Z');
        expect(tokenState(token(), now)).toBe('active');
        expect(tokenState(token({ expires: null }), now)).toBe('active');
        expect(tokenState(token({ expires: '2026-11-13T00:00:00Z' }), now)).toBe('expired');
        expect(tokenState(token({ revoked: '2026-11-01T00:00:00Z' }), now)).toBe('revoked');
    });
});
