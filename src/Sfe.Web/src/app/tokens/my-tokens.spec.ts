import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
    accessToken as token,
    ApiMock,
    ALL_RIGHTS,
    buttonNamed,
    fakeConfig,
    loadConfig,
    mockApi,
    settle,
    testProviders,
    type,
} from '../../testing/fakes';
import { ApiError } from '../core/api.service';
import { AccessTokenInfo } from '../core/access-token.models';
import { ClientConfig } from '../core/config.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { MyTokensComponent } from './my-tokens.component';
import { TokenCreateComponent } from './token-create.component';

describe('MyTokensComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<MyTokensComponent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const rows = () => [...el().querySelectorAll<HTMLElement>('.token-row')];

    async function create(overrides: Partial<ClientConfig> = {}, tokens: AccessTokenInfo[] = []): Promise<void> {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        api.myTokens.mockResolvedValue(tokens);
        await loadConfig(api, fakeConfig({ rights: ALL_RIGHTS, accessTokens: true, ...overrides }));
        fixture = TestBed.createComponent(MyTokensComponent);
        await settle();
    }

    async function openCreator(): Promise<void> {
        buttonNamed(el(), 'New token')!.click();
        await settle();
    }

    /** Types the name of the new token (Create waits for one). */
    async function nameToken(name: string): Promise<void> {
        type(el().querySelector<HTMLInputElement>('#token-name')!, name);
        await settle();
    }

    afterEach(() => vi.restoreAllMocks());

    it('lists the tokens with their library, rights and dates', async () => {
        await create({ tenants: [{ id: 1, slug: 'library', name: 'Library' }] }, [
            token({ lastUsed: '2026-11-10T08:00:00Z' }),
            token({ id: 2, name: 'Photos sync', tenantId: 1, rights: ['library.browse', 'files.view'] }),
            token({ id: 3, name: 'Old', revoked: '2026-11-05T08:00:00Z' }),
        ]);

        const [first, second, third] = rows();
        expect(first.textContent).toContain('Backup script');
        expect(first.textContent).toContain('sfe_0123abcd…');
        expect(first.textContent).toContain('Every space');
        expect(first.textContent).toContain("All of the account's rights");
        expect(first.textContent).toContain('Last used');
        expect(second.textContent).toContain('Library · 2 rights');
        expect(second.textContent).toContain('Never used');
        expect(third.textContent).toContain('Revoked');
        expect(buttonNamed(third, 'Revoke')).toBeUndefined();
    });

    it('makes a token and shows it once, with Copy and the warning', async () => {
        await create();
        api.createToken.mockResolvedValue({ token: token({ id: 5 }), secret: 'sfe_0123abcd_SECRET' });
        await openCreator();

        await nameToken(' Backup script ');
        const expiry = el().querySelector<HTMLSelectElement>('#token-expiry')!;
        expiry.value = '30';
        expiry.dispatchEvent(new Event('change'));
        await settle();
        buttonNamed(el(), 'Create')!.click();
        await settle();

        const draft = api.createToken.mock.calls[0][0];
        expect(draft).toMatchObject({ name: 'Backup script', tenantId: null, rights: [] });
        const days = (Date.parse(draft.expires!) - Date.now()) / 86_400_000;
        expect(days).toBeGreaterThan(29.9);
        expect(days).toBeLessThan(30.1);
        expect(el().querySelector<HTMLInputElement>('#token-secret')!.value).toBe('sfe_0123abcd_SECRET');
        expect(el().textContent).toContain("you won't see it again");
        expect(api.myTokens).toHaveBeenCalledTimes(2);

        const copied = vi.fn(() => true);
        document.execCommand = copied;
        const shown = vi.spyOn(TestBed.inject(ToastService), 'show');
        buttonNamed(el(), 'Copy')!.click();
        await settle();
        expect(copied).toHaveBeenCalledWith('copy');
        expect(shown).toHaveBeenCalledWith('Token copied.');
    });

    it('forgets the token once the sheet is closed, and when the page is left', async () => {
        await create();
        api.createToken.mockResolvedValue({ token: token(), secret: 'sfe_0123abcd_SECRET' });
        await openCreator();
        await nameToken('Script');
        buttonNamed(el(), 'Create')!.click();
        await settle();
        expect(el().querySelector('#token-secret')).not.toBeNull();

        buttonNamed(el(), 'Done')!.click();
        await settle();
        expect(el().querySelector('#token-secret')).toBeNull();
        await openCreator();
        expect(el().querySelector('#token-secret')).toBeNull();
        expect(el().querySelector<HTMLInputElement>('#token-name')!.value).toBe('');

        await nameToken('Script');
        buttonNamed(el(), 'Create')!.click();
        await settle();
        const creator = fixture.debugElement.query(By.directive(TokenCreateComponent)).componentInstance;
        expect(creator['secret']()).toBe('sfe_0123abcd_SECRET');
        fixture.destroy();
        expect(creator['secret']()).toBeNull();
    });

    it('limits a token to a library and to rights chosen among its own', async () => {
        await create({ rights: [Rights.browse, Rights.viewFiles, Rights.ownTokens] });
        api.createToken.mockResolvedValue({ token: token(), secret: 'sfe_x' });
        await openCreator();
        await nameToken('Viewer');
        const library = el().querySelector<HTMLSelectElement>('#token-library')!;
        library.value = '1';
        library.dispatchEvent(new Event('change'));
        el().querySelector<HTMLInputElement>('.check input')!.click();
        await settle();

        const offered = [...el().querySelectorAll('.access-choices .check')].map((e) => e.textContent!.trim());
        expect(offered).toEqual([Rights.browse, Rights.viewFiles, Rights.ownTokens]);
        el().querySelectorAll<HTMLInputElement>('.access-choices input')[1].click();
        buttonNamed(el(), 'Create')!.click();
        await settle();

        expect(api.createToken.mock.calls[0][0]).toMatchObject({ tenantId: 1, rights: [Rights.viewFiles] });
    });

    it("shows the server's refusals by field", async () => {
        await create();
        api.createToken.mockRejectedValue(new ApiError(400, 'Invalid', { name: ['A token needs a name.'] }));
        await openCreator();
        await nameToken('x');
        buttonNamed(el(), 'Create')!.click();
        await settle();

        expect(el().querySelector('.field-error')!.textContent).toBe('A token needs a name.');
        expect(el().querySelector('#token-secret')).toBeNull();
    });

    it('revokes one token, and all of them, after asking', async () => {
        await create({}, [token(), token({ id: 2, name: 'Other' })]);
        const asked = vi.spyOn(window, 'confirm').mockReturnValue(false);
        buttonNamed(rows()[1], 'Revoke')!.click();
        await settle();
        expect(api.revokeToken).not.toHaveBeenCalled();

        asked.mockReturnValue(true);
        api.revokeToken.mockResolvedValue(undefined);
        buttonNamed(rows()[1], 'Revoke')!.click();
        await settle();
        expect(api.revokeToken).toHaveBeenCalledWith(2);

        api.revokeAllTokens.mockResolvedValue({ revoked: 2 });
        const shown = vi.spyOn(TestBed.inject(ToastService), 'show');
        buttonNamed(el(), 'Revoke all')!.click();
        await settle();
        expect(api.revokeAllTokens).toHaveBeenCalled();
        expect(shown).toHaveBeenCalledWith('2 tokens revoked.');
        expect(api.myTokens).toHaveBeenCalledTimes(3);
    });

    it('offers no new token while tokens are off on the server (or the server says nothing of them)', async () => {
        await create({ accessTokens: false }, [token()]);
        expect(buttonNamed(el(), 'New token')).toBeUndefined();
        expect(el().textContent).toContain('Access tokens are switched off on this server');
        expect(rows()).toHaveLength(1);

        TestBed.resetTestingModule();
        await create({ accessTokens: undefined });
        expect(buttonNamed(el(), 'New token')).toBeUndefined();
    });
});
