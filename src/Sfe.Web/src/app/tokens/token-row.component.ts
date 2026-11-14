import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { AccessTokenInfo } from '../core/access-token.models';
import { ConfigService } from '../core/config.service';
import { rightsSummary, tokenDates, tokenLibrary, tokenState } from './token-parts';

/** One access token in a list: its name and first characters, where it works, its dates; Revoke while it works. */
@Component({
    selector: 'li[sfeTokenRow]',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'access-row token-row', '[class.ended]': "state() !== 'active'" },
    template: `
        <div class="access-row-main">
            <strong>{{ token().name }}</strong>
            <code class="token-prefix" title="The first characters of the token">{{ token().prefix }}…</code>
            @if (state() !== 'active') {
                <span class="access-badge danger">{{ state() === 'revoked' ? 'Revoked' : 'Expired' }}</span>
            }
        </div>
        <div class="access-row-detail">
            <span>
                @if (showAccount()) {
                    <strong>{{ token().userName }}</strong> ·
                }
                {{ library() }} · <span [title]="rightList()">{{ rights() }}</span>
            </span>
            <span class="muted">{{ dates() }}{{ from() }}</span>
        </div>
        <div class="access-row-actions">
            @if (state() !== 'revoked') {
                <button
                    type="button"
                    class="button small"
                    [title]="'Revoke ' + token().name + ': the programs using it are refused from now on'"
                    (click)="revoke.emit(token())"
                >Revoke</button>
            }
        </div>
    `,
})
export class TokenRowComponent {
    private readonly config = inject(ConfigService);

    readonly token = input.required<AccessTokenInfo>();
    /** Shows the account the token belongs to (the administrators' list). */
    readonly showAccount = input(false);
    readonly revoke = output<AccessTokenInfo>();

    protected readonly state = computed(() => tokenState(this.token()));
    protected readonly library = computed(() => tokenLibrary(this.token(), this.config.config()?.tenants ?? []));
    protected readonly rights = computed(() => rightsSummary(this.token()));
    protected readonly rightList = computed(() => this.token().rights.join(', ') || null);
    protected readonly dates = computed(() => tokenDates(this.token()));
    protected readonly from = computed(() => {
        const address = this.token().lastUsedFrom;
        return this.showAccount() && address ? ` from ${address}` : '';
    });
}
