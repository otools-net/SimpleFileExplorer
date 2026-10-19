import { Component, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { ApiMock, buttonNamed, installationView, mockApi, settle, testProviders } from '../../testing/fakes';
import { ApiError } from '../core/api.service';
import { SessionInfo, SetupInfo } from '../core/models';
import { InstallationFormComponent } from './installation-form.component';
import { LoginPageComponent } from './login-page.component';
import { SETUP_CODE_STEPS } from './setup-code.component';
import { SetupPageComponent } from './setup-page.component';

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

    it('recovers an administrator with the code the server shows, then signs in', async () => {
        await create(session(), '/admin');
        api.requestRecovery.mockResolvedValue({ minutes: 15 });
        api.completeRecovery.mockResolvedValue(session({ user: { id: 1, userName: 'admin', displayName: null, kind: 'user', isServerAdmin: true, local: true }, canBrowse: true }));

        type(element, '#login-name', 'admin');
        element.querySelector<HTMLAnchorElement>('a[title^="Recover a server"]')!.click();
        await settle();
        expect((element.querySelector<HTMLInputElement>('#recover-name'))!.value).toBe('admin');
        buttonNamed(element, 'Show a recovery code on the server')!.click();
        await settle();

        expect(api.requestRecovery).toHaveBeenCalledWith('admin');
        expect(element.textContent).toContain('works for 15 minutes');
        type(element, '#recover-code', ' ab2c-de3f-gh4j-km5n ');
        type(element, '#recover-password', 'new-password-1');
        type(element, '#recover-confirm', 'new-password-2');
        await settle();
        expect(element.textContent).toContain('The two passwords differ.');
        expect(buttonNamed(element, 'Set the password and sign in')!.disabled).toBe(true);

        type(element, '#recover-confirm', 'new-password-1');
        await settle();
        buttonNamed(element, 'Set the password and sign in')!.click();
        await settle();

        expect(api.completeRecovery).toHaveBeenCalledWith('admin', 'ab2c-de3f-gh4j-km5n', 'new-password-1', false);
        expect(navigateByUrl).toHaveBeenCalledWith('/admin', { replaceUrl: true });
    });

    it('shows a wrong recovery code by its field and stays', async () => {
        await create(session());
        api.requestRecovery.mockResolvedValue({ minutes: 15 });
        api.completeRecovery.mockRejectedValue(new ApiError(400, 'One or more validation errors occurred.', { code: ['This recovery code is wrong or has expired: ask for a new one.'] }));

        element.querySelector<HTMLAnchorElement>('a[title^="Recover a server"]')!.click();
        await settle();
        type(element, '#recover-name', 'admin');
        await settle();
        buttonNamed(element, 'Show a recovery code on the server')!.click();
        await settle();
        type(element, '#recover-code', 'WRONG');
        type(element, '#recover-password', 'new-password-1');
        type(element, '#recover-confirm', 'new-password-1');
        await settle();
        buttonNamed(element, 'Set the password and sign in')!.click();
        await settle();

        expect(element.querySelector('.field-error')!.textContent).toContain('wrong or has expired');
        expect(navigateByUrl).not.toHaveBeenCalled();

        buttonNamed(element, 'Back')!.click();
        await settle();
        expect(element.querySelector('#login-password')).not.toBeNull();
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
        api.checkInstallation.mockResolvedValue({ token: 'the-token', settings: installationView() });
        api.serverFolders.mockImplementation(async (_token, path) => path
            ? { path, parent: null, folders: [] }
            : { path: null, parent: null, folders: [{ name: 'D:', path: 'D:\\' }], places: [{ name: 'Home', path: 'C:\\Users\\me' }] });
        TestBed.configureTestingModule({ providers: testProviders(api) });
        navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const fixture = TestBed.createComponent(SetupPageComponent);
        element = fixture.nativeElement;
        await settle();
    }

    /** Step 1: the code, then Next. */
    async function enterCode(code = 'the-setup-code-0123'): Promise<void> {
        type(element, '#setup-code', code);
        await settle();
        buttonNamed(element, 'Next')!.click();
        await settle();
    }

    /** Step 3: the administrator's fields. */
    async function fillAccount(): Promise<void> {
        type(element, '#setup-name', ' admin ');
        type(element, '#setup-password', 'secret-password');
        type(element, '#setup-confirm', 'secret-password');
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

    it('says where to find a code the server made itself', async () => {
        await create(setup({ codeGenerated: true, codeFile: 'data/setup-code.txt', hosting: 'portable' }));

        const hint = element.querySelector('#setup-code-hint')!.textContent!;
        expect(hint).toContain('SFE made this code when it started');
        expect(hint).toContain('in the window where SFE runs');
        expect(hint).toContain('data/setup-code.txt in SFE\'s folder');
        expect(element.querySelector('.setup-progress')!.textContent).toBe('Step 1 of 3');
    });

    it('checks the code, picks the library folder, then creates the administrator and opens the library', async () => {
        await create(setup());
        api.saveInstallation.mockImplementation(async (_token, input) =>
            ({ settings: installationView({ root: { value: input.root!, locked: false, lockedBy: null } }), restartRequired: false }));
        api.completeSetup.mockResolvedValue({} as SessionInfo);

        await enterCode(' the-setup-code-0123 ');
        expect(api.checkInstallation).toHaveBeenCalledWith('the-setup-code-0123');
        expect(element.querySelector('sfe-installation-form')).not.toBeNull();
        buttonNamed(element, 'D:')!.click();
        await settle();
        buttonNamed(element, 'Next')!.click();
        await settle();

        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { root: 'D:\\' });
        await fillAccount();
        type(element, '#setup-library', 'Family');
        await settle();
        buttonNamed(element, 'Create the administrator')!.click();
        await settle();

        expect(api.completeSetup).toHaveBeenCalledWith({
            code: 'the-setup-code-0123', userName: 'admin', password: 'secret-password', displayName: null, libraryName: 'Family', fastSignIn: false,
        });
        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });

    it('asks no code of a new install opened on the server\'s own computer', async () => {
        await create(setup({ codeRequired: false, codeConfigured: false }));
        api.saveInstallation.mockResolvedValue({ settings: installationView(), restartRequired: false });
        api.completeSetup.mockResolvedValue({} as SessionInfo);

        expect(element.querySelector('#setup-code')).toBeNull();
        expect(api.checkInstallation).toHaveBeenCalledWith('');
        expect(element.querySelector('sfe-installation-form')).not.toBeNull();
        expect(element.querySelector('.setup-progress')!.textContent).toBe('Step 1 of 2');
        expect(buttonNamed(element, 'Back')).toBeFalsy();
        buttonNamed(element, 'Next')!.click();
        await settle();
        expect(element.querySelector('.setup-progress')!.textContent).toBe('Step 2 of 2');
        await fillAccount();
        buttonNamed(element, 'Create the administrator')!.click();
        await settle();

        expect(api.completeSetup).toHaveBeenCalledWith(expect.objectContaining({ code: '', userName: 'admin' }));
    });

    it('offers fast sign-in when the server opens a browser at start', async () => {
        await create(setup({ codeRequired: false, hosting: 'portable' }));
        api.saveInstallation.mockResolvedValue({ settings: installationView(), restartRequired: false });
        api.completeSetup.mockResolvedValue({} as SessionInfo);
        buttonNamed(element, 'Next')!.click();
        await settle();
        await fillAccount();

        const box = [...element.querySelectorAll<HTMLLabelElement>('label.check')].find(label => label.textContent!.includes('Sign me in when SFE starts'));
        expect(box).toBeDefined();
        box!.querySelector('input')!.click();
        await settle();
        buttonNamed(element, 'Create the administrator')!.click();
        await settle();

        expect(api.completeSetup).toHaveBeenCalledWith(expect.objectContaining({ fastSignIn: true }));
    });

    it('offers no fast sign-in for a server started otherwise', async () => {
        await create(setup({ hosting: 'windowsService' }));
        await enterCode();
        buttonNamed(element, 'Next')!.click();
        await settle();

        expect(element.querySelector('#setup-name')).not.toBeNull();
        expect(element.textContent).not.toContain('Sign me in when SFE starts');
    });

    it('stays on the code while it is wrong', async () => {
        await create(setup());
        api.checkInstallation.mockRejectedValue(new ApiError(400, 'x', { code: ['This is not the server\'s setup code.'] }));

        await enterCode('wrong-code-wrong');

        expect(element.querySelector('.field-error')!.textContent).toContain('setup code');
        expect(element.querySelector('#setup-code')).not.toBeNull();
        expect(element.querySelector('sfe-installation-form')).toBeNull();
    });

    it('shows the errors by their fields, and holds back differing passwords', async () => {
        await create(setup());
        await enterCode();
        buttonNamed(element, 'Next')!.click();
        await settle();

        expect(api.saveInstallation).not.toHaveBeenCalled();
        type(element, '#setup-password', 'secret-password');
        type(element, '#setup-confirm', 'other');
        await settle();
        expect(element.textContent).toContain('The two passwords differ.');
        expect(buttonNamed(element, 'Create the administrator')!.disabled).toBe(true);

        api.completeSetup.mockRejectedValue(new ApiError(400, 'x', { password: ['At least 8 characters.'] }));
        type(element, '#setup-confirm', 'secret-password');
        type(element, '#setup-name', 'admin');
        await settle();
        buttonNamed(element, 'Create the administrator')!.click();
        await settle();

        expect(element.querySelector('.field-error')!.textContent).toContain('At least 8');
        expect(navigate).not.toHaveBeenCalled();
    });

    it('asks to restart SFE when the network setting changed', async () => {
        await create(setup());
        api.saveInstallation.mockResolvedValue({ settings: installationView({ network: true, restartRequired: true }), restartRequired: true });
        api.completeSetup.mockResolvedValue({} as SessionInfo);

        await enterCode();
        element.querySelectorAll<HTMLInputElement>('input[name=installation-network]')[1].click();
        await settle();
        buttonNamed(element, 'Next')!.click();
        await settle();
        await fillAccount();
        buttonNamed(element, 'Create the administrator')!.click();
        await settle();

        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { network: true, port: 5080 });
        expect(element.textContent).toContain('applies once SFE starts again');
        expect(navigate).not.toHaveBeenCalled();
        buttonNamed(element, 'Open the library')!.click();
        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });

    it('skips the library in recovery mode', async () => {
        await create(setup({ required: false, recover: true }));
        api.completeSetup.mockResolvedValue({} as SessionInfo);

        await enterCode();

        expect(element.querySelector('sfe-installation-form')).toBeNull();
        expect(element.querySelector('#setup-library')).toBeNull();
        expect(element.querySelector('.setup-progress')!.textContent).toBe('Step 2 of 2');
        await fillAccount();
        buttonNamed(element, 'Restore the administrator')!.click();
        await settle();
        expect(api.completeSetup).toHaveBeenCalledWith(expect.objectContaining({ userName: 'admin', libraryName: null }));
    });

    it('leaves when there is nothing to set up', async () => {
        await create(setup({ required: false, available: false }));

        expect(navigate).toHaveBeenCalledWith(['/'], { replaceUrl: true });
    });
});

describe('InstallationFormComponent', () => {
    @Component({
        imports: [InstallationFormComponent],
        template: `<sfe-installation-form token="the-token" [initial]="view" />`,
    })
    class Host {
        view = installationView();
        readonly form = viewChild.required(InstallationFormComponent);
    }

    let api: ApiMock;

    async function create(view = installationView()): Promise<{ element: HTMLElement; form: InstallationFormComponent }> {
        api = mockApi();
        api.serverFolders.mockImplementation(async (_token, path) => path
            ? { path, parent: 'D:\\', folders: [{ name: 'Photos', path: 'D:\\Photos' }] }
            : { path: null, parent: null, folders: [{ name: 'D:', path: 'D:\\' }] });
        TestBed.configureTestingModule({ providers: testProviders(api) });
        const fixture = TestBed.createComponent(Host);
        fixture.componentInstance.view = view;
        await settle();
        return { element: fixture.nativeElement, form: fixture.componentInstance.form() };
    }

    afterEach(() => vi.restoreAllMocks());

    it('browses the server\'s folders with the token and sends only what changed', async () => {
        const { element, form } = await create();
        api.saveInstallation.mockResolvedValue({ settings: installationView(), restartRequired: false });

        expect(api.serverFolders).toHaveBeenCalledWith('the-token', null);
        buttonNamed(element, 'D:')!.click();
        await settle();
        buttonNamed(element, 'Photos')!.click();
        await settle();
        type(element, '#installation-ffmpeg', ' C:\\Tools\\ffmpeg ');
        await settle();
        await form.save();

        expect(element.querySelector('.folder-picker-path')!.textContent).toContain('D:\\Photos');
        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { root: 'D:\\Photos', ffmpegPath: 'C:\\Tools\\ffmpeg' });
    });

    it('shows what the server\'s environment sets, without a way to change it', async () => {
        const locked = { value: 'http://*:8080', locked: true, lockedBy: 'an environment variable of the server' };
        const { element, form } = await create(installationView({ urls: locked, port: 8080, network: true, ffmpegFound: null, ffmpegVersion: null }));

        expect(element.querySelector('input[name=installation-network]')).toBeNull();
        expect(element.querySelector('.installation-network')!.textContent).toContain('Set by an environment variable of the server.');
        expect(element.querySelector('.system-badge')!.textContent).toContain('Not found');
        expect(await form.save()).toEqual({ settings: expect.anything(), restartRequired: false });
        expect(api.saveInstallation).not.toHaveBeenCalled();
    });

    it('shows the server\'s errors by their fields', async () => {
        const { element, form } = await create();
        api.saveInstallation.mockRejectedValue(new ApiError(400, 'x', { port: ['A port is a number from 1 to 65535.'] }));

        type(element, '#installation-port', '7000');
        await settle();
        const result = await form.save();
        await settle();

        expect(result).toBeNull();
        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { network: false, port: 7000 });
        expect(element.querySelector('.installation-network .field-error')!.textContent).toContain('1 to 65535');
    });

    const notChosen = { value: null, locked: false, lockedBy: null };
    const modeRadio = (element: HTMLElement, mode: string) => element.querySelector<HTMLInputElement>(`input[name=installation-mode][value=${mode}]`)!;

    it('writes a mode not chosen yet: Local on the server\'s own computer', async () => {
        const { element, form } = await create(installationView({ mode: notChosen }));
        api.saveInstallation.mockResolvedValue({ settings: installationView(), restartRequired: false });

        await form.save();

        expect(modeRadio(element, 'local').checked).toBe(true);
        expect(element.querySelector('.installation-mode-note')!.textContent).toContain('Not chosen yet');
        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { mode: 'local' });
    });

    it('offers only External to a visitor on another computer', async () => {
        const { element, form } = await create(installationView({ mode: notChosen, onThisComputer: false }));
        api.saveInstallation.mockResolvedValue({ settings: installationView(), restartRequired: false });

        await form.save();

        expect(modeRadio(element, 'local').disabled).toBe(true);
        expect(modeRadio(element, 'external').checked).toBe(true);
        expect(element.querySelector('.installation-mode-note')!.textContent).toContain('on the server itself');
        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { mode: 'external' });
    });

    it('switches from Local to External, and not back', async () => {
        const local = await create();
        api.saveInstallation.mockResolvedValue({ settings: installationView(), restartRequired: false });
        modeRadio(local.element, 'external').click();
        await settle();
        await local.form.save();
        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { mode: 'external' });

        TestBed.resetTestingModule();
        const external = await create(installationView({ mode: { value: 'External', locked: false, lockedBy: null }, modeInEffect: 'external' }));
        expect(modeRadio(external.element, 'local').disabled).toBe(true);
        expect(external.element.querySelector('.installation-mode-note')!.textContent).toContain('only in the server\'s configuration');
    });

    it('shows a mode set by the server\'s environment, without a way to change it', async () => {
        const { element, form } = await create(installationView({
            mode: { value: 'External', locked: true, lockedBy: 'an environment variable of the server' }, modeInEffect: 'external',
        }));

        expect(element.querySelector('input[name=installation-mode]')).toBeNull();
        expect(element.querySelector('.installation-mode')!.textContent).toContain('Set by an environment variable of the server.');
        await form.save();
        expect(api.saveInstallation).not.toHaveBeenCalled();
    });
});
