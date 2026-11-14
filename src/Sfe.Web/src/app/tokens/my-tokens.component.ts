import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { AccessTokenInfo } from '../core/access-token.models';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { TokenCreateComponent } from './token-create.component';
import { tokensConfig, tokenState } from './token-parts';
import { TokenRowComponent } from './token-row.component';

/**
 * My account › Access tokens: the account's personal access tokens, a new one (shown once), revoking one or all.
 * While the server has tokens off, none can be made and the list says those it shows don't work.
 */
@Component({
    selector: 'sfe-my-tokens',
    imports: [IconComponent, TokenCreateComponent, TokenRowComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'token-page' },
    templateUrl: './my-tokens.component.html',
})
export class MyTokensComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly creator = viewChild.required(TokenCreateComponent);

    protected readonly tokens = signal<AccessTokenInfo[]>([]);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);

    /** Tokens may be made and used on this server. */
    protected readonly enabled = computed(() => tokensConfig(this.config.config()) !== null);
    /** Tokens not revoked yet, which Revoke all ends. */
    protected readonly revocable = computed(() => this.tokens().filter((token) => tokenState(token) !== 'revoked'));

    constructor() {
        void this.load();
    }

    protected async load(): Promise<void> {
        try {
            this.tokens.set(await this.api.myTokens());
            this.error.set(null);
        } catch (error) {
            this.error.set(`Could not load your tokens: ${(error as Error).message}`);
        } finally {
            this.loaded.set(true);
        }
    }

    protected async revoke(token: AccessTokenInfo): Promise<void> {
        if (!confirm(`Revoke “${token.name}”? The programs using it are refused from now on.`)) return;
        try {
            await this.api.revokeToken(token.id);
            this.toasts.show(`“${token.name}” revoked.`);
        } catch (error) {
            this.toasts.error(`Could not revoke “${token.name}”: ${(error as Error).message}`);
        }
        await this.load();
    }

    protected async revokeAll(): Promise<void> {
        if (!confirm('Revoke all your tokens? Every program using one is refused from now on.')) return;
        try {
            const { revoked } = await this.api.revokeAllTokens();
            this.toasts.show(`${plural(revoked, 'token')} revoked.`);
        } catch (error) {
            this.toasts.error(`Could not revoke your tokens: ${(error as Error).message}`);
        }
        await this.load();
    }
}
