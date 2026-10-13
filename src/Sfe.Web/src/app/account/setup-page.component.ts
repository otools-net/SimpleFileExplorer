import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SetupInfo } from '../core/models';
import { valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { SetupCodeStepsComponent } from './setup-code.component';

/**
 * The first administrator (`setup`). Nothing else works until one exists. The page needs the setup code of the
 * server's environment: without one it says how to set it for the way the server runs. With `Sfe:Setup:Recover`, the
 * same page resets the password of a local administrator (or adds one).
 */
@Component({
    selector: 'sfe-setup-page',
    imports: [IconComponent, SetupCodeStepsComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-page' },
    template: `
        <section class="account-card wide">
            <header class="account-head">
                <span class="brand"><svg sfeIcon="folder"></svg><span>SFE</span></span>
                <h1>{{ info()?.recover ? 'Recover an administrator' : 'Set up SFE' }}</h1>
            </header>
            @if (loadError(); as error) {
                <p class="form-error" role="alert">The server is not reachable: {{ error }}</p>
                <button type="button" class="button" title="Ask the server again" (click)="load()">Try again</button>
            } @else if (!info()) {
                <div class="tag-loading"><span class="spinner"></span></div>
            } @else if (!info()!.codeConfigured) {
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
                <form class="account-form" novalidate (submit)="submit($event)">
                    @if (info()!.recover) {
                        <p>
                            The server is in recovery mode: enter the name of a local administrator to give it a new password, or a new
                            name to add an administrator. Turn recovery off (Sfe:Setup:Recover) afterwards.
                        </p>
                    } @else if (info()!.required) {
                        <p>
                            Create the administrator of this server. Your library, its index, tags and settings are kept: they become
                            the first <strong>space</strong>, which you can name here. More spaces, and accounts for the others, come later in Administration.
                        </p>
                    }
                    <div class="field">
                        <label class="field-label" for="setup-code">Setup code</label>
                        <input id="setup-code" type="password" autocomplete="off" required [value]="code()" (input)="code.set(valueOf($event))" aria-describedby="setup-code-hint">
                        <span class="field-hint" id="setup-code-hint">The code given in the server's environment (Sfe:Setup:Code).</span>
                        @if (errors()['code']; as error) {
                            <span class="field-error">{{ error }}</span>
                        }
                    </div>
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
                    @if (error(); as error) {
                        <p class="form-error" role="alert">{{ error }}</p>
                    }
                    <button type="submit" class="button primary account-submit" title="Create the administrator and sign in"
                            [disabled]="busy() || mismatch() || !code() || !userName().trim() || !password()">
                        {{ info()!.recover ? 'Restore the administrator' : 'Create the administrator' }}
                    </button>
                </form>
            }
        </section>
    `,
})
export class SetupPageComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);

    protected readonly info = signal<SetupInfo | null>(null);
    protected readonly loadError = signal<string | null>(null);
    protected readonly code = signal('');
    protected readonly userName = signal('');
    protected readonly displayName = signal('');
    protected readonly password = signal('');
    protected readonly confirm = signal('');
    protected readonly libraryName = signal('');
    protected readonly busy = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly errors = signal<Record<string, string>>({});

    protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());

    protected readonly valueOf = valueOf;

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
        }
    }

    protected async submit(event: Event): Promise<void> {
        event.preventDefault();
        if (this.mismatch())
            return;
        this.busy.set(true);
        this.error.set(null);
        this.errors.set({});
        try {
            await this.api.completeSetup({
                code: this.code(),
                userName: this.userName().trim(),
                password: this.password(),
                displayName: this.displayName().trim() || null,
                libraryName: this.info()?.recover ? null : this.libraryName().trim() || null,
            });
            this.config.clear();
            void this.router.navigate(['/'], { replaceUrl: true });
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.errors.set(Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')])));
            else
                this.error.set((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }
}
