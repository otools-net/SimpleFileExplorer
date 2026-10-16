import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SessionInfo } from '../core/models';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';

/**
 * The sign-in page (`login?return=`): a name and password, and the libraries open to visitors without an account.
 * Opened by the Api when a request needs a session; back to `return` once signed in. A server administrator locked
 * out (or without the password) recovers the account there: the server shows a one-time code where it runs, and the
 * code with a new password unlocks the account and signs it in.
 */
@Component({
    selector: 'sfe-login-page',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-page' },
    template: `
        <section class="account-card">
            <header class="account-head">
                <span class="brand"><svg sfeIcon="folder"></svg><span>SFE</span></span>
                <h1>{{ recovering() ? 'Recover the account' : 'Sign in' }}</h1>
            </header>
            @if (!session()) {
                <div class="tag-loading"><span class="spinner"></span></div>
            } @else if (recovering()) {
                <form class="account-form" novalidate (submit)="recover($event)">
                    <p class="field-hint">
                        For a server administrator locked out, or who forgot the password: the server shows a one-time code where
                        it runs, for whoever runs it. Other accounts: ask an administrator.
                    </p>
                    <div class="field">
                        <label class="field-label" for="recover-name">Name</label>
                        <input id="recover-name" type="text" autocomplete="username" autocapitalize="none" spellcheck="false" required
                               [value]="userName()" (input)="userName.set(valueOf($event))">
                    </div>
                    @if (codeMinutes() === null) {
                        @if (error(); as error) {
                            <p class="form-error" role="alert">{{ error }}</p>
                        }
                        <button type="button" class="button primary account-submit" title="Show a recovery code for this account where the server runs"
                                [disabled]="busy() || !userName().trim()" (click)="requestCode()">Show a recovery code on the server</button>
                    } @else {
                        <p class="field-hint" role="status">
                            If this is a server administrator's account, its code is now shown in SFE's console window (for a
                            service: in <code>recovery-code.txt</code>, beside <code>setup-code.txt</code> in SFE's data folder). It
                            works for {{ codeMinutes() }} minutes, once.
                        </p>
                        <div class="field">
                            <label class="field-label" for="recover-code">Recovery code</label>
                            <input id="recover-code" type="text" autocomplete="one-time-code" autocapitalize="characters" spellcheck="false" required
                                   [value]="code()" (input)="code.set(valueOf($event))">
                            @if (errors()['code']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <div class="field">
                            <label class="field-label" for="recover-password">New password</label>
                            <input id="recover-password" type="password" autocomplete="new-password" required
                                   [value]="password()" (input)="password.set(valueOf($event))">
                            @if (errors()['password']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <div class="field">
                            <label class="field-label" for="recover-confirm">New password again</label>
                            <input id="recover-confirm" type="password" autocomplete="new-password" required
                                   [value]="confirm()" (input)="confirm.set(valueOf($event))">
                            @if (mismatch()) {
                                <span class="field-error">The two passwords differ.</span>
                            }
                        </div>
                        @if (error(); as error) {
                            <p class="form-error" role="alert">{{ error }}</p>
                        }
                        <button type="submit" class="button primary account-submit" title="Set the new password, unlock the account and sign in"
                                [disabled]="busy() || !userName().trim() || !code().trim() || !password() || password() !== confirm()">Set the password and sign in</button>
                    }
                    <button type="button" class="button account-submit" title="Back to signing in" (click)="leaveRecovery()">Back</button>
                </form>
            } @else {
                <form class="account-form" novalidate (submit)="signIn($event)">
                    <div class="field">
                        <label class="field-label" for="login-name">Name</label>
                        <input id="login-name" type="text" autocomplete="username" autocapitalize="none" spellcheck="false" required
                               [value]="userName()" (input)="userName.set(valueOf($event))">
                    </div>
                    <div class="field">
                        <label class="field-label" for="login-password">Password</label>
                        <input id="login-password" type="password" autocomplete="current-password" required
                               [value]="password()" (input)="password.set(valueOf($event))">
                    </div>
                    <label class="check">
                        <input type="checkbox" [checked]="remember()" (change)="remember.set(checkedOf($event))">
                        <span>Keep me signed in on this device<span class="field-hint">Leave it off on a shared computer.</span></span>
                    </label>
                    @if (error(); as error) {
                        <p class="form-error" role="alert">{{ error }}</p>
                    }
                    <button type="submit" class="button primary account-submit" title="Sign in with this name and password"
                            [disabled]="busy() || !userName().trim() || !password()">Sign in</button>
                </form>
                <p class="field-hint account-foot">
                    Locked out, or forgot the password?
                    <a href="" title="Recover a server administrator's account with a code the server shows where it runs" (click)="startRecovery($event)">Recover the account</a>
                </p>
                @if (session()!.guestTenants.length) {
                    <section class="account-guest">
                        <h2>Or browse without an account</h2>
                        <div class="account-guest-list">
                            @for (tenant of session()!.guestTenants; track tenant.slug) {
                                <button type="button" class="button" [title]="'Browse ' + tenant.name + ' as a guest: viewing only'" (click)="browseAsGuest(tenant.slug)">
                                    <svg sfeIcon="space"></svg>{{ tenant.name }}
                                </button>
                            }
                        </div>
                    </section>
                }
                @if (session()!.setupAvailable) {
                    <p class="field-hint account-foot">
                        The server is in recovery mode. <a routerLink="/setup" title="Reset the password of an administrator, or create one">Recover an administrator</a>
                    </p>
                }
            }
        </section>
    `,
})
export class LoginPageComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);
    private readonly returnUrl = inject(ActivatedRoute).snapshot.queryParamMap.get('return');

    protected readonly session = signal<SessionInfo | null>(null);
    protected readonly userName = signal('');
    protected readonly password = signal('');
    protected readonly remember = signal(false);
    protected readonly busy = signal(false);
    protected readonly error = signal<string | null>(null);

    /** Recovering an administrator's account: with a code shown where the server runs, a new password. */
    protected readonly recovering = signal(false);
    /** How long the code works, once asked for; null before. */
    protected readonly codeMinutes = signal<number | null>(null);
    protected readonly code = signal('');
    protected readonly confirm = signal('');
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    constructor() {
        inject(Title).setTitle('Sign in – SFE');
    }

    async ngOnInit(): Promise<void> {
        try {
            const session = await this.api.session();
            if (session.setupRequired) {
                void this.router.navigate(['/setup'], { replaceUrl: true });
                return;
            }
            if (session.user && session.canBrowse) {
                this.done();
                return;
            }
            this.session.set(session);
        } catch (error) {
            this.session.set({ user: null, tenant: null, tenants: [], guestTenants: [], setupRequired: false, setupAvailable: false, canBrowse: false });
            this.error.set(`The server is not reachable: ${(error as Error).message}`);
        }
    }

    protected async signIn(event: Event): Promise<void> {
        event.preventDefault();
        if (!this.userName().trim() || !this.password())
            return;
        this.busy.set(true);
        this.error.set(null);
        try {
            const session = await this.api.signIn(this.userName().trim(), this.password(), this.remember());
            if (!session.canBrowse) {
                this.error.set('This account is in no space yet: ask an administrator to add it to one.');
                return;
            }
            this.done();
        } catch (error) {
            this.error.set((error as Error).message);
            this.password.set('');
        } finally {
            this.busy.set(false);
        }
    }

    protected startRecovery(event: Event): void {
        event.preventDefault();
        this.recovering.set(true);
        this.password.set('');
        this.error.set(null);
    }

    protected leaveRecovery(): void {
        this.recovering.set(false);
        this.codeMinutes.set(null);
        this.code.set('');
        this.password.set('');
        this.confirm.set('');
        this.errors.set({});
        this.error.set(null);
    }

    protected async requestCode(): Promise<void> {
        this.busy.set(true);
        this.error.set(null);
        try {
            this.codeMinutes.set((await this.api.requestRecovery(this.userName().trim())).minutes);
        } catch (error) {
            this.error.set((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }

    protected async recover(event: Event): Promise<void> {
        event.preventDefault();
        if (!this.code().trim() || !this.password() || this.password() !== this.confirm())
            return;
        this.busy.set(true);
        this.error.set(null);
        this.errors.set({});
        try {
            await this.api.completeRecovery(this.userName().trim(), this.code().trim(), this.password(), this.remember());
            this.done();
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.errors.set(Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')])));
            else
                this.error.set((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }

    protected async browseAsGuest(tenant: string): Promise<void> {
        try {
            await this.api.browseAsGuest(tenant);
            this.done();
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }

    /** Signed in (or browsing as a guest): where the page was asked from, with the new session's configuration. */
    private done(): void {
        this.config.clear();
        void this.router.navigateByUrl(this.returnUrl && this.returnUrl.startsWith('/') ? this.returnUrl : '/', { replaceUrl: true });
    }
}
