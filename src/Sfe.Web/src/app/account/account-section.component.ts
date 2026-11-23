import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ToastService } from '../core/toast.service';
import { SettingsForm } from '../settings/settings-form.service';
import { SettingsPanelComponent } from '../settings/settings-panel.component';
import { TABS } from '../settings/settings-schema';
import { valueOf } from '../shared/events';
import { MyTokensComponent } from '../tokens/my-tokens.component';

/**
 * Administration › My account: the signed-in user's own preferences (the settings each user may set over the
 * library's: theme, page sizes, the player...), kept by the server for every device, the password of a local
 * account and the account's access tokens. The preferences have their own form, saved here.
 */
@Component({
    selector: 'sfe-account-section',
    imports: [SettingsPanelComponent, MyTokensComponent],
    providers: [SettingsForm],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-section' },
    templateUrl: './account-section.component.html',
})
export class AccountSectionComponent {
    /** `preferences`, `password` or `tokens`. */
    readonly tab = input.required<string>();

    protected readonly form = inject(SettingsForm).forUser();
    protected readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    /** The tabs of settings-schema.ts holding preferences of users (all of them until the preferences are loaded). */
    protected readonly tabs = computed(() =>
        TABS.filter((tab) => !this.form.loaded() || tab.groups.some((group) => !this.form.groupHidden(group))).map(
            (tab) => tab.id,
        ),
    );

    protected readonly current = signal('');
    protected readonly password = signal('');
    protected readonly confirm = signal('');
    protected readonly busy = signal(false);
    protected readonly passwordErrors = signal<Record<string, string>>({});
    protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());

    protected readonly valueOf = valueOf;

    constructor() {
        // The preferences load once shown (the section stays when moving to the password and back).
        effect(() => {
            const preferences = !['password', 'tokens'].includes(this.tab());
            if (preferences && !untracked(this.form.loaded)) untracked(() => void this.form.load());
        });
    }

    protected save(): void {
        void this.form.save();
    }

    protected async changePassword(event: Event): Promise<void> {
        event.preventDefault();
        if (this.mismatch()) return;
        this.busy.set(true);
        this.passwordErrors.set({});
        try {
            await this.api.changePassword(this.current(), this.password());
            this.current.set('');
            this.password.set('');
            this.confirm.set('');
            this.toasts.show($localize`Password changed.`);
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.passwordErrors.set(
                    Object.fromEntries(
                        Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')]),
                    ),
                );
            else this.toasts.error($localize`Could not change the password: ${(error as Error).message}:reason:`);
        } finally {
            this.busy.set(false);
        }
    }
}
