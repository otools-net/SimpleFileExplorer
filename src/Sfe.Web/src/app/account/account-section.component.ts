import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ToastService } from '../core/toast.service';
import { SettingsForm } from '../settings/settings-form.service';
import { SettingsPanelComponent } from '../settings/settings-panel.component';
import { TABS } from '../settings/settings-schema';
import { valueOf } from '../shared/events';

/**
 * Administration › My account: the signed-in user's own preferences (the settings each user may set over the
 * library's: theme, page sizes, the player...), kept by the server for every device, and the password of a local
 * account. The preferences have their own form, saved here.
 */
@Component({
    selector: 'sfe-account-section',
    imports: [SettingsPanelComponent],
    providers: [SettingsForm],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-section' },
    template: `
        @switch (tab()) {
            @case ('password') {
                @if (config.user()?.local === false) {
                    <p class="muted">The password of this account is kept by its directory: change it there.</p>
                } @else {
                    <form class="access-form account-password" novalidate (submit)="changePassword($event)">
                        <div class="field">
                            <label class="field-label" for="password-current">Current password</label>
                            <input id="password-current" type="password" autocomplete="current-password" [value]="current()" (input)="current.set(valueOf($event))">
                            @if (passwordErrors()['current']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <div class="field">
                            <label class="field-label" for="password-new">New password</label>
                            <input id="password-new" type="password" autocomplete="new-password" [value]="password()" (input)="password.set(valueOf($event))">
                            @if (passwordErrors()['password']; as error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </div>
                        <div class="field">
                            <label class="field-label" for="password-confirm">New password again</label>
                            <input id="password-confirm" type="password" autocomplete="new-password" [value]="confirm()" (input)="confirm.set(valueOf($event))">
                            @if (mismatch()) {
                                <span class="field-error">The two passwords differ.</span>
                            }
                        </div>
                        <p class="field-hint">Your other sessions (other browsers and devices) end, and the links you gave to external players stop working.</p>
                        <button type="submit" class="button primary" title="Change the password of your account"
                                [disabled]="busy() || mismatch() || !current() || !password()">Change the password</button>
                    </form>
                }
            }
            @default {
                <p class="settings-intro">
                    Your own choices, over the space's settings: kept by the server, they follow you on every device you sign in on.
                    A setting the space or the server locked cannot be changed here.
                </p>
                @for (shown of tabs(); track shown) {
                    <sfe-settings-panel [tab]="shown" (submitted)="save()" />
                }
                <div class="access-actions">
                    <button type="button" class="button primary" title="Save your preferences" [disabled]="form.saving() || !form.changed()" (click)="save()">Save</button>
                    @if (form.changed()) {
                        <button type="button" class="button" title="Undo the changes not saved yet" (click)="form.discard()">Discard</button>
                    }
                    <button type="button" class="button" [disabled]="!form.customized()" title="Forget your preferences and follow the space's settings (asks first)"
                            (click)="form.reset()">Use the space's settings</button>
                </div>
            }
        }
    `,
})
export class AccountSectionComponent {
    /** `preferences` or `password`. */
    readonly tab = input.required<string>();

    protected readonly form = inject(SettingsForm).forUser();
    protected readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    /** The tabs of settings-schema.ts holding preferences of users (all of them until the preferences are loaded). */
    protected readonly tabs = computed(() => TABS
        .filter(tab => !this.form.loaded() || tab.groups.some(group => !this.form.groupHidden(group)))
        .map(tab => tab.id));

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
            if (this.tab() !== 'password' && !untracked(this.form.loaded))
                untracked(() => void this.form.load());
        });
    }

    protected save(): void {
        void this.form.save();
    }

    protected async changePassword(event: Event): Promise<void> {
        event.preventDefault();
        if (this.mismatch())
            return;
        this.busy.set(true);
        this.passwordErrors.set({});
        try {
            await this.api.changePassword(this.current(), this.password());
            this.current.set('');
            this.password.set('');
            this.confirm.set('');
            this.toasts.show('Password changed.');
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.passwordErrors.set(Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')])));
            else
                this.toasts.error(`Could not change the password: ${(error as Error).message}`);
        } finally {
            this.busy.set(false);
        }
    }
}
