import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { valueOf } from '../shared/events';
import { ProviderSetup, ProviderTest } from '../core/connection.models';

/**
 * Administration › Connections › a kind's setup: whether it is offered, the return address to register at the
 * provider, its app secret (typed here, never shown again: only whether one is set), and a test. The app key is a
 * setting of the Settings tab.
 */
@Component({
    selector: 'sfe-provider-setup',
    imports: [BusyDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'provider-setup' },
    templateUrl: './provider-setup.component.html',
})
export class ProviderSetupComponent {
    readonly kind = input.required<string>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    protected readonly setup = signal<ProviderSetup | null>(null);
    protected readonly secret = signal('');
    protected readonly secretError = signal<string | null>(null);
    protected readonly result = signal<ProviderTest | null>(null);
    protected readonly busy = signal(false);
    protected readonly value = valueOf;

    constructor() {
        effect(() => {
            const kind = this.kind();
            untracked(() => void this.load(kind));
        });
    }

    async load(kind = this.kind()): Promise<void> {
        try {
            this.setup.set(await this.api.connectionProvider(kind));
        } catch (error) {
            this.toasts.error($localize`Could not load the setup: ${(error as Error).message}:reason:`);
        }
    }

    protected async copyAddress(): Promise<void> {
        const address = this.setup()?.returnAddress;
        if (!address) return;
        await navigator.clipboard.writeText(address);
        this.toasts.show($localize`Return address copied.`);
    }

    protected async saveSecret(): Promise<void> {
        this.secretError.set(null);
        try {
            await whileBusy(this.busy, () => this.api.setProviderSecret(this.kind(), this.secret()));
            // Never kept on the page once sent.
            this.secret.set('');
            this.toasts.show($localize`App secret saved.`);
        } catch (error) {
            this.secretError.set(
                error instanceof ApiError && error.errors
                    ? Object.values(error.errors).flat().join(' ')
                    : (error as Error).message,
            );
        }
        await this.load();
    }

    protected async removeSecret(): Promise<void> {
        if (!confirm($localize`Remove the app secret? Sign-ins go on without it (PKCE).`)) return;
        await whileBusy(this.busy, () => this.api.removeProviderSecret(this.kind()));
        await this.load();
    }

    protected async test(): Promise<void> {
        this.result.set(null);
        try {
            this.result.set(await whileBusy(this.busy, () => this.api.testProvider(this.kind())));
        } catch (error) {
            this.result.set({ ok: false, message: (error as Error).message });
        }
    }
}
