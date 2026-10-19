import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { InstallationView, SetupInfo } from '../core/models';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { InstallationFormComponent } from './installation-form.component';
import { generatedCodeWhere, SetupCodeStepsComponent } from './setup-code.component';

/** The setup's steps: the code, then the library folder and network (skipped in recovery), then the administrator. */
type SetupStep = 'code' | 'library' | 'account' | 'restart';

/**
 * The first administrator (`setup`). Nothing else works until one exists. The page needs the setup code: the one the
 * server made itself (shown in its console) or one set in its environment; without either, it says how to set one for
 * the way the server runs. Then it asks for the mode (Local or External), the library folder, who may open SFE and ffmpeg (the installation page,
 * saved before the administrator exists), then for the administrator. A new install opened in a browser on the
 * server's own computer skips the code (`codeRequired` false). With `Sfe:Setup:Recover`, the same page resets the
 * password of a local administrator (or adds one), without the library step.
 */
@Component({
    selector: 'sfe-setup-page',
    imports: [IconComponent, SetupCodeStepsComponent, InstallationFormComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-page' },
    template: `
        <section class="account-card wide">
            <header class="account-head">
                <span class="brand"><svg sfeIcon="folder"></svg><span>SFE</span></span>
                <h1>{{ info()?.recover ? 'Recover an administrator' : 'Set up SFE' }}</h1>
                @if (stepNumber(); as number) {
                    <p class="setup-progress">Step {{ number }} of {{ stepCount() }}</p>
                }
            </header>
            @if (loadError(); as error) {
                <p class="form-error" role="alert">The server is not reachable: {{ error }}</p>
                <button type="button" class="button" title="Ask the server again" (click)="load()">Try again</button>
            } @else if (!info()) {
                <div class="tag-loading"><span class="spinner"></span></div>
            } @else if (!info()!.codeConfigured && !codeFree()) {
                <div class="account-steps">
                    <p>
                        SFE has no administrator yet. So that only the person running the server can create one, the server first
                        needs a <strong>setup code</strong> of at least {{ info()!.minimumCodeLength }} characters, given in its
                        environment; this page then asks for it.
                    </p>
                    <sfe-setup-code-steps [hosting]="info()!.hosting" [minimumCodeLength]="info()!.minimumCodeLength" [codeTooShort]="info()!.codeTooShort" />
                    <p class="field-hint">Pick a long code nobody can guess. Afterwards it is only asked for again to confirm the update after an upgrade.</p>
                    <button type="button" class="button primary" title="Ask the server whether it has a setup code now" (click)="load()">Check again</button>
                </div>
            } @else {
                @switch (step()) {
                    @case ('code') {
                        @if (codeFree()) {
                            @if (error(); as error) {
                                <p class="form-error" role="alert">{{ error }}</p>
                                <button type="button" class="button" title="Ask the server again" (click)="openWithoutCode()">Try again</button>
                            } @else {
                                <div class="tag-loading"><span class="spinner"></span></div>
                            }
                        } @else {
                            <form class="account-form" novalidate (submit)="checkCode($event)">
                                @if (info()!.recover) {
                                    <p>
                                        The server is in recovery mode: enter the setup code, then the name of a local administrator to give it a new
                                        password, or a new name to add an administrator. Turn recovery off (Sfe:Setup:Recover) afterwards.
                                    </p>
                                } @else {
                                    <p>
                                        Welcome. SFE needs three things: the setup code that proves you run this server, the folder of your library,
                                        and an administrator account.
                                    </p>
                                }
                                <div class="field">
                                    <label class="field-label" for="setup-code">Setup code</label>
                                    <input id="setup-code" type="text" autocomplete="off" autocapitalize="characters" spellcheck="false" required
                                           [value]="code()" (input)="code.set(valueOf($event))" aria-describedby="setup-code-hint">
                                    <span class="field-hint" id="setup-code-hint">
                                        @if (info()!.codeGenerated) {
                                            SFE made this code when it started: {{ codeWhere() }}.
                                        } @else {
                                            The code given in the server's environment (Sfe:Setup:Code).
                                        }
                                    </span>
                                    @if (errors()['code']; as error) {
                                        <span class="field-error">{{ error }}</span>
                                    }
                                </div>
                                @if (error(); as error) {
                                    <p class="form-error" role="alert">{{ error }}</p>
                                }
                                <button type="submit" class="button primary account-submit" title="Check the setup code and go on" [disabled]="busy() || !code().trim()">Next</button>
                            </form>
                        }
                    }
                    @case ('library') {
                        <form class="account-form" novalidate (submit)="saveLibrary($event)">
                            @if (codeFree()) {
                                <p>Welcome. SFE needs two things: the folder of your library, and an administrator account.</p>
                            }
                            <p>
                                How will SFE be used, where are your photos, videos and music, and who may open SFE? All of it can be changed later in
                                Administration › System › Installation, except going back from External to Local.
                            </p>
                            <sfe-installation-form [token]="token()!" [initial]="installation()!" [setup]="true" />
                            <div class="setup-actions">
                                @if (!codeFree()) {
                                    <button type="button" class="button" title="Back to the setup code" (click)="step.set('code')">Back</button>
                                }
                                <button type="submit" class="button primary" title="Save these and go on to the administrator" [disabled]="busy()">Next</button>
                            </div>
                        </form>
                    }
                    @case ('account') {
                        <form class="account-form" novalidate (submit)="submit($event)">
                            @if (info()!.recover) {
                                <p>Enter the name of a local administrator to give it a new password, or a new name to add an administrator.</p>
                            } @else {
                                <p>
                                    Create the administrator of this server. Your library, its index, tags and settings become the first
                                    <strong>space</strong>, which you can name here. More spaces, and accounts for the others, come later in Administration.
                                </p>
                            }
                            <div class="field">
                                <label class="field-label" for="setup-name">Administrator's name</label>
                                <input id="setup-name" type="text" autocomplete="username" autocapitalize="none" spellcheck="false" required
                                       [value]="userName()" (input)="userName.set(valueOf($event))">
                                @if (errors()['userName']; as error) {
                                    <span class="field-error">{{ error }}</span>
                                }
                            </div>
                            <div class="field">
                                <label class="field-label" for="setup-display">Display name <span class="field-hint">(optional)</span></label>
                                <input id="setup-display" type="text" autocomplete="name" [value]="displayName()" (input)="displayName.set(valueOf($event))">
                            </div>
                            <div class="field">
                                <label class="field-label" for="setup-password">Password</label>
                                <input id="setup-password" type="password" autocomplete="new-password" required [value]="password()" (input)="password.set(valueOf($event))"
                                       aria-describedby="setup-password-hint">
                                <span class="field-hint" id="setup-password-hint">At least {{ info()!.minimumPasswordLength }} characters.</span>
                                @if (errors()['password']; as error) {
                                    <span class="field-error">{{ error }}</span>
                                }
                            </div>
                            <div class="field">
                                <label class="field-label" for="setup-confirm">Password again</label>
                                <input id="setup-confirm" type="password" autocomplete="new-password" required [value]="confirm()" (input)="confirm.set(valueOf($event))">
                                @if (mismatch()) {
                                    <span class="field-error">The two passwords differ.</span>
                                }
                            </div>
                            @if (!info()!.recover) {
                                <div class="field">
                                    <label class="field-label" for="setup-library">Name of the first space</label>
                                    <input id="setup-library" type="text" [value]="libraryName()" (input)="libraryName.set(valueOf($event))" aria-describedby="setup-library-hint">
                                    <span class="field-hint" id="setup-library-hint">A space holds one library and who may see it, e.g. Family. You can rename it later.</span>
                                    @if (errors()['libraryName']; as error) {
                                        <span class="field-error">{{ error }}</span>
                                    }
                                </div>
                            }
                            @if (offerFastSignIn()) {
                                <label class="check">
                                    <input type="checkbox" [checked]="fastSignIn()" (change)="fastSignIn.set(checkedOf($event))">
                                    <span>
                                        Sign me in when SFE starts on this computer
                                        <span class="field-hint">
                                            The browser SFE opens when it starts signs you in without your password. Only for SFE used on this
                                            computer alone; switch it off in Administration › System › Start-up.
                                        </span>
                                    </span>
                                </label>
                            }
                            @if (errors()['code']; as error) {
                                <p class="form-error" role="alert">{{ error }}</p>
                            }
                            @if (error(); as error) {
                                <p class="form-error" role="alert">{{ error }}</p>
                            }
                            <div class="setup-actions">
                                <button type="button" class="button" [title]="info()!.recover ? 'Back to the setup code' : 'Back to the library folder'"
                                        (click)="step.set(info()!.recover ? 'code' : 'library')">Back</button>
                                <button type="submit" class="button primary" title="Create the administrator and sign in"
                                        [disabled]="busy() || mismatch() || !userName().trim() || !password()">
                                    {{ info()!.recover ? 'Restore the administrator' : 'Create the administrator' }}
                                </button>
                            </div>
                        </form>
                    }
                    @case ('restart') {
                        <div class="account-steps">
                            <p><strong>SFE is set up.</strong> One more thing: the new network setting applies once SFE starts again.</p>
                            <p>Close SFE and start it again (or restart its service). Until then, it keeps answering at this address.</p>
                            <button type="button" class="button primary" title="Open the library" (click)="finish()">Open the library</button>
                        </div>
                    }
                }
            }
        </section>
    `,
})
export class SetupPageComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);
    private readonly installationForm = viewChild(InstallationFormComponent);

    protected readonly info = signal<SetupInfo | null>(null);
    protected readonly loadError = signal<string | null>(null);
    protected readonly step = signal<SetupStep>('code');
    protected readonly token = signal<string | null>(null);
    protected readonly installation = signal<InstallationView | null>(null);
    private readonly restartRequired = signal(false);
    protected readonly code = signal('');
    protected readonly userName = signal('');
    protected readonly displayName = signal('');
    protected readonly password = signal('');
    protected readonly confirm = signal('');
    protected readonly libraryName = signal('');
    protected readonly fastSignIn = signal(false);
    protected readonly busy = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly errors = signal<Record<string, string>>({});

    protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());
    protected readonly codeWhere = computed(() => this.info() ? generatedCodeWhere(this.info()!.hosting, this.info()!.codeFile ?? null) : '');
    /** A new install opened on the server's own computer: no code step. */
    protected readonly codeFree = computed(() => this.info()?.codeRequired === false && !this.info()!.recover);
    /** The server opens a browser when it starts (portable packages): it can sign the administrator in there, when Local. */
    protected readonly offerFastSignIn = computed(() => !this.info()?.recover && (this.info()?.hosting === 'portable' || this.info()?.hosting === 'appImage')
        && this.installation()?.modeInEffect !== 'external');
    protected readonly stepCount = computed(() => this.info()?.recover || this.codeFree() ? 2 : 3);
    protected readonly stepNumber = computed(() => {
        if (!this.info()?.codeConfigured && !this.codeFree())
            return null;
        switch (this.step()) {
            case 'code': return 1;
            case 'library': return this.codeFree() ? 1 : 2;
            case 'account': return this.stepCount();
            default: return null;
        }
    });

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    constructor() {
        inject(Title).setTitle('Setup – SFE');
    }

    ngOnInit(): Promise<void> {
        return this.load();
    }

    protected async load(): Promise<void> {
        try {
            const info = await this.api.setupInfo();
            this.loadError.set(null);
            if (!info.available) {
                void this.router.navigate(['/'], { replaceUrl: true });
                return;
            }
            this.info.set(info);
            if (!this.libraryName())
                this.libraryName.set(info.libraryName);
        } catch (error) {
            this.loadError.set((error as Error).message);
            return;
        }
        if (this.codeFree() && this.step() === 'code')
            await this.openWithoutCode();
    }

    /** Step 1 without the code: the installation settings open at once. */
    protected async openWithoutCode(): Promise<void> {
        await this.run(async () => {
            const grant = await this.api.checkInstallation('');
            this.token.set(grant.token);
            this.installation.set(grant.settings);
            this.step.set('library');
        });
    }

    /** Step 1: the code opens the installation settings (and is sent again with the administrator). */
    protected async checkCode(event: Event): Promise<void> {
        event.preventDefault();
        await this.run(async () => {
            const grant = await this.api.checkInstallation(this.code().trim());
            this.token.set(grant.token);
            this.installation.set(grant.settings);
            this.step.set(this.info()?.recover ? 'account' : 'library');
        });
    }

    /** Step 2: the library folder, network and ffmpeg, saved at once (the library starts again on its new folder). */
    protected async saveLibrary(event: Event): Promise<void> {
        event.preventDefault();
        const form = this.installationForm();
        if (!form)
            return;
        this.busy.set(true);
        try {
            const saved = await form.save();
            if (saved) {
                this.installation.set(saved.settings);
                this.restartRequired.set(saved.restartRequired);
                this.step.set('account');
            }
        } finally {
            this.busy.set(false);
        }
    }

    /** Step 3: the administrator, signed in at once. */
    protected async submit(event: Event): Promise<void> {
        event.preventDefault();
        if (this.mismatch())
            return;
        await this.run(async () => {
            await this.api.completeSetup({
                code: this.codeFree() ? '' : this.code().trim(),
                userName: this.userName().trim(),
                password: this.password(),
                displayName: this.displayName().trim() || null,
                libraryName: this.info()?.recover ? null : this.libraryName().trim() || null,
                fastSignIn: this.offerFastSignIn() && this.fastSignIn(),
            });
            this.config.clear();
            if (this.restartRequired())
                this.step.set('restart');
            else
                this.finish();
        });
    }

    protected finish(): void {
        void this.router.navigate(['/'], { replaceUrl: true });
    }

    /** Runs a request of the page: its errors are shown by their fields, or above the button. */
    private async run(action: () => Promise<void>): Promise<void> {
        this.busy.set(true);
        this.error.set(null);
        this.errors.set({});
        try {
            await action();
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.errors.set(Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')])));
            else if (error instanceof ApiError && error.status === 429)
                this.error.set('Too many attempts: wait a minute, then try again.');
            else
                this.error.set((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }
}
