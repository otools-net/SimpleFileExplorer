import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { ApiMock, buttonNamed, mockApi, settle, testProviders } from '../../testing/fakes';
import { ApiError } from '../core/api.service';
import { SessionInfo, SetupInfo } from '../core/models';
import { LoginPageComponent } from './login-page.component';
import { SETUP_CODE_STEPS, SetupPageComponent } from './setup-page.component';

function session(overrides: Partial<SessionInfo> = {}): SessionInfo {
    return { user: null, tenant: null, tenants: [], guestTenants: [], setupRequired: false, setupAvailable: false, canBrowse: false, ...overrides };
}

function setup(overrides: Partial<SetupInfo> = {}): SetupInfo {
    return {
        required: true, available: true, codeConfigured: true, codeTooShort: false, recover: false, minimumCodeLength: 12, minimumPasswordLength: 8,
        libraryName: 'Library', hosting: 'other', ...overrides,
    };
}

function type(element: HTMLElement, selector: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
}

describe('LoginPageComponent', () => {
    let api: ApiMock;
    let element: HTMLElement;
    let navigate: ReturnType<typeof vi.spyOn>;
    let navigateByUrl: ReturnType<typeof vi.spyOn>;

    async function create(info: SessionInfo, returnUrl: string | null = null): Promise<void> {
        api = mockApi();
        api.session.mockResolvedValue(info);
        TestBed.configureTestingModule({
            providers: testProviders(api, { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(returnUrl ? { return: returnUrl } : {}) } } }),
        });
        const router = TestBed.inject(Router);
        navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
        navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
        const fixture = TestBed.createComponent(LoginPageComponent);
        element = fixture.nativeElement;
        await settle();
    }

    afterEach(() => vi.restoreAllMocks());

    it('signs in, then goes back to the page that asked for it', async () => {
        await create(session(), '/admin?section=tags');
        api.signIn.mockResolvedValue(session({ user: { id: 1, userName: 'admin', displayName: null, kind: 'user', isServerAdmin: true, local: true }, canBrowse: true }));

        type(element, '#login-name', ' admin ');
        type(element, '#login-password', 'secret-password');
        element.querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
        await settle();
        buttonNamed(element, 'Sign in')!.click();
        await settle();

        expect(api.signIn).toHaveBeenCalledWith('admin', 'secret-password', true);
        expect(navigateByUrl).toHaveBeenCalledWith('/admin?section=tags', { replaceUrl: true });
    });

    it('shows why a sign-in failed and empties the password', async () => {
        await create(session());
        api.signIn.mockRejectedValue(new ApiError(400, 'Wrong name or password.'));

        type(element, '#login-name', 'admin');
        type(element, '#login-password', 'wrong');
        await settle();
        buttonNamed(element, 'Sign in')!.click();
        await settle();

        expect(element.querySelector('.form-error')!.textContent).toContain('Wrong name or password.');
        expect(element.querySelector<HTMLInputElement>('#login-password')!.value).toBe('');
        expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('offers the libraries open to guests, and browses one without an account', async () => {
        await create(session({ guestTenants: [{ id: 2, slug: 'films', name: 'Films' }] }));
        api.browseAsGuest.mockResolvedValue(session({ canBrowse: true }));

        buttonNamed(element, 'Films')!.click();
        await settle();

        expect(api.browseAsGuest).toHaveBeenCalledWith('films');
        expect(navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
    });

    it('sends to the setup page while the server has no administrator', async () => {
        await create(session({ setupRequired: true, setupAvailable: true }));

        expect(navigate).toHaveBeenCalledWith(['/setup'], { replaceUrl: true });
    });

    it('leaves at once when the session is signed in already', async () => {
        await create(session({ user: { id: 1, userName: 'admin', displayName: null, kind: 'user', isServerAdmin: false, local: true }, canBrowse: true }), '/player?path=a.mp4');

        expect(navigateByUrl).toHaveBeenCalledWith('/player?path=a.mp4', { replaceUrl: true });
    });

    it('never goes back to another site', async () => {
        await create(session(), 'http://evil/');
        api.signIn.mockResolvedValue(session({ user: { id: 1, userName: 'a', displayName: null, kind: 'user', isServerAdmin: false, local: true }, canBrowse: true }));

        type(element, '#login-name', 'a');
        type(element, '#login-password', 'secret-password');
        await settle();
        buttonNamed(element, 'Sign in')!.click();
        await settle();

        expect(navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
    });
});

describe('SetupPageComponent', () => {
    let api: ApiMock;
    let element: HTMLElement;
    let navigate: ReturnType<typeof vi.spyOn>;

    async function create(info: SetupInfo): Promise<void> {
        api = mockApi();
        api.setupInfo.mockResolvedValue(info);
        TestBed.configureTestingModule({ providers: testProviders(api) });
        navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const fixture = TestBed.createComponent(SetupPageComponent);
        element = fixture.nativeElement;
        await settle();
    }

    afterEach(() => vi.restoreAllMocks());

    it('explains how to give the server a setup code, for the way it runs first', async () => {
        await create(setup({ codeConfigured: false, codeTooShort: true, hosting: 'systemd' }));

        expect(element.querySelector('form')).toBeNull();
        expect(element.querySelector('.account-steps h2')!.textContent).toBe(SETUP_CODE_STEPS.systemd[0]);
        expect(element.querySelector('.account-steps')!.textContent).toContain('Sfe__Setup__Code');
        expect(element.querySelector('.form-error')!.textContent).toContain('too short');
        expect([...element.querySelectorAll('dt')].map(dt => dt.textContent)).not.toContain(SETUP_CODE_STEPS.systemd[0]);

        api.setupInfo.mockResolvedValue(setup());
        buttonNamed(element, 'Check again')!.click();
        await settle();
        expect(element.querySelector('form')).not.toBeNull();
    });

    it('creates the administrator with the code, then opens the library', async () => {
        await create(setup());
        api.completeSetup.mockResolvedValue({} as SessionInfo);

        type(element, '#setup-code', 'the-setup-code-0123');
        type(element, '#setup-name', ' admin ');
        type(element, '#setup-password', 'secret-password');
        type(element, '#setup-confirm', 'secret-password');
        type(element, '#setup-library', 'Family');
        await settle();
        element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
        await settle();

        expect(api.completeSetup).toHaveBeenCalledWith({
            code: 'the-setup-code-0123', userName: 'admin', password: 'secret-password', displayName: null, libraryName: 'Family',
        });
        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });

    it('shows the errors by their fields, and holds back differing passwords', async () => {
        await create(setup());

        type(element, '#setup-password', 'secret-password');
        type(element, '#setup-confirm', 'other');
        await settle();
        expect(element.textContent).toContain('The two passwords differ.');
        expect(element.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);

        api.completeSetup.mockRejectedValue(new ApiError(400, 'x', { code: ['This is not the server\'s setup code.'] }));
        type(element, '#setup-confirm', 'secret-password');
        type(element, '#setup-code', 'wrong-code-wrong');
        type(element, '#setup-name', 'admin');
        await settle();
        element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
        await settle();

        expect(element.querySelector('.field-error')!.textContent).toContain('setup code');
        expect(navigate).not.toHaveBeenCalled();
    });

    it('leaves when there is nothing to set up', async () => {
        await create(setup({ required: false, available: false }));

        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });
});
