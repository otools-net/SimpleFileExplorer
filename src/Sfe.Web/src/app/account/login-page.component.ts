import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SessionInfo } from '../core/models';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';

/**
 * The sign-in page (`login?return=`): a name and password, and the libraries open to visitors without an account.
 * Opened by the Api when a request needs a session; back to `return` once signed in.
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
                <h1>Sign in</h1>
            </header>
            @if (!session()) {
                <div class="tag-loading"><span class="spinner"></span></div>
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
