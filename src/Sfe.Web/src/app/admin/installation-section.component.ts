import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { InstallationFormComponent } from '../account/installation-form.component';
import { Api, ApiError } from '../core/api.service';
import { InstallationView } from '../core/models';
import { ToastService } from '../core/toast.service';
import { valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';

/**
 * System › Installation on the administration page (`server.settings`): the first space's library folder, who can open
 * SFE and ffmpeg, as on the setup page. These decide where data lives and which program runs, so the page asks for the
 * setup code first, as the update page does: being an administrator is not enough.
 */
@Component({
    selector: 'sfe-installation-section',
    imports: [InstallationFormComponent, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'installation-section' },
    template: `
        <section class="settings-group">
            <header class="settings-group-head">
                <h3>Installation</h3>
                <p class="field-hint">
                    The folder of the first space, who can open SFE and where ffmpeg is. Only whoever runs the server may change them: enter the
                    setup code first (shown when SFE started the first time, and kept in data/setup-code.txt in SFE's folder, unless the
                    server's configuration sets one).
                </p>
            </header>
            @if (!token()) {
                <form class="installation-unlock" novalidate (submit)="unlock($event)">
                    <div class="field">
                        <label class="field-label" for="installation-code">Setup code</label>
                        <input id="installation-code" type="password" autocomplete="off" [value]="code()" (input)="code.set(valueOf($event))">
                        @if (codeError(); as error) {
                            <span class="field-error">{{ error }}</span>
                        }
                    </div>
                    <button type="submit" class="button primary" title="Check the setup code and show the installation settings" [disabled]="busy() || !code().trim()">
                        <svg sfeIcon="check"></svg>Unlock
                    </button>
                </form>
            } @else {
                <form class="settings-form" novalidate (submit)="save($event)">
                    <p class="field-hint installation-warning">
                        Another library folder makes the first space show other files: the next scan treats the files of the old folder
                        as gone (tagged ones are kept as moved files for a while).
                    </p>
                    <sfe-installation-form [token]="token()!" [initial]="settings()!" />
                    @if (restartRequired()) {
                        <p class="installation-restart" role="status">The new network setting applies once SFE starts again: restart it (or its service).</p>
                    }
                    <div class="settings-action inline">
                        <button type="submit" class="button primary" title="Save the installation settings" [disabled]="busy()">Save</button>
                    </div>
                </form>
            }
        </section>
    `,
})
export class InstallationSectionComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly form = viewChild(InstallationFormComponent);

    protected readonly code = signal('');
    protected readonly codeError = signal<string | null>(null);
    protected readonly token = signal<string | null>(null);
    protected readonly settings = signal<InstallationView | null>(null);
    protected readonly restartRequired = signal(false);
    protected readonly busy = signal(false);

    protected readonly valueOf = valueOf;

    protected async unlock(event: Event): Promise<void> {
        event.preventDefault();
        this.busy.set(true);
        this.codeError.set(null);
        try {
            const grant = await this.api.checkInstallation(this.code().trim());
            this.settings.set(grant.settings);
            this.restartRequired.set(grant.settings.restartRequired);
            this.token.set(grant.token);
        } catch (error) {
            this.codeError.set(error instanceof ApiError && error.errors?.['code'] ? error.errors['code'].join(' ')
                : error instanceof ApiError && error.status === 429 ? 'Too many attempts: wait a minute, then try again.'
                : (error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }

    protected async save(event: Event): Promise<void> {
        event.preventDefault();
        const form = this.form();
        if (!form)
            return;
        this.busy.set(true);
        try {
            const saved = await form.save();
            if (saved) {
                this.restartRequired.set(saved.restartRequired);
                this.toasts.show('Installation settings saved.');
            }
        } finally {
            this.busy.set(false);
        }
    }
}
