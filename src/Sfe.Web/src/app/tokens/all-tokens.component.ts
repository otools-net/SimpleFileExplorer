import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { AccessTokenInfo } from '../core/access-token.models';
import { ToastService } from '../core/toast.service';
import { valueOf } from '../shared/events';
import { TokenRowComponent } from './token-row.component';

/**
 * Administration › Spaces & accounts › Access tokens: the tokens of every account the session administers (all of
 * them for server administrators), with their account, library and last use; revoking one.
 */
@Component({
    selector: 'sfe-all-tokens',
    imports: [TokenRowComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'token-page' },
    template: `
        @if (tokens().length > 8) {
            <div class="access-actions">
                <input
                    type="search"
                    class="access-filter"
                    placeholder="Find a token"
                    aria-label="Find a token"
                    title="Show only the tokens whose name or account contains this"
                    [value]="filter()"
                    (input)="filter.set(valueOf($event))"
                >
            </div>
        }
        @if (error(); as error) {
            <p class="form-error">{{ error }}</p>
        } @else if (!loaded()) {
            <div class="tag-loading"><span class="spinner"></span></div>
        } @else {
            <ul class="access-list">
                @for (token of shown(); track token.id) {
                    <li sfeTokenRow [token]="token" [showAccount]="true" (revoke)="revoke($event)"></li>
                } @empty {
                    <li class="access-empty muted">{{ filter() ? 'No token matches.' : 'No account has a token.' }}</li>
                }
            </ul>
        }
    `,
})
export class AllTokensComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    protected readonly tokens = signal<AccessTokenInfo[]>([]);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly filter = signal('');

    protected readonly shown = computed(() => {
        const words = this.filter().trim().toLowerCase();
        return this.tokens().filter((token) => `${token.name} ${token.userName}`.toLowerCase().includes(words));
    });

    protected readonly valueOf = valueOf;

    constructor() {
        void this.load();
    }

    protected async revoke(token: AccessTokenInfo): Promise<void> {
        const question = `Revoke “${token.name}” of ${token.userName}? The programs using it are refused from now on.`;
        if (!confirm(question)) return;
        try {
            await this.api.revokeAnyToken(token.id);
            this.toasts.show(`“${token.name}” revoked.`);
        } catch (error) {
            this.toasts.error(`Could not revoke “${token.name}”: ${(error as Error).message}`);
        }
        await this.load();
    }

    private async load(): Promise<void> {
        try {
            this.tokens.set(await this.api.allTokens());
            this.error.set(null);
        } catch (error) {
            this.error.set(`Could not load the tokens: ${(error as Error).message}`);
        } finally {
            this.loaded.set(true);
        }
    }
}
