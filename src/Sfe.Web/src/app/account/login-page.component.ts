import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SessionInfo } from '../core/access.models';
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
    templateUrl: './login-page.component.html',
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
        inject(Title).setTitle($localize`Sign in – SFE`);
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
            this.session.set({
                user: null,
                tenant: null,
                tenants: [],
                guestTenants: [],
                setupRequired: false,
                setupAvailable: false,
                canBrowse: false,
            });
            this.error.set($localize`Could not load the sign-in page: ${(error as Error).message}:reason:`);
        }
    }

    protected async signIn(event: Event): Promise<void> {
        event.preventDefault();
        if (!this.userName().trim() || !this.password()) return;
        this.busy.set(true);
        this.error.set(null);
        try {
            const session = await this.api.signIn(this.userName().trim(), this.password(), this.remember());
            if (!session.canBrowse) {
                this.error.set($localize`Your account isn't in any space yet: ask an administrator to add it to one.`);
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
        if (!this.code().trim() || !this.password() || this.password() !== this.confirm()) return;
        this.busy.set(true);
        this.error.set(null);
        this.errors.set({});
        try {
            await this.api.completeRecovery(
                this.userName().trim(),
                this.code().trim(),
                this.password(),
                this.remember(),
            );
            this.done();
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.errors.set(
                    Object.fromEntries(
                        Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')]),
                    ),
                );
            else this.error.set((error as Error).message);
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
        void this.router.navigateByUrl(this.returnUrl && this.returnUrl.startsWith('/') ? this.returnUrl : '/', {
            replaceUrl: true,
        });
    }
}
