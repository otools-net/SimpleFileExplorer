import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { InstallationFormComponent } from '../account/installation-form.component';
import { Api, ApiError } from '../core/api.service';
import { InstallationView } from '../core/access.models';
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
    templateUrl: './installation-section.component.html',
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
            this.codeError.set(
                error instanceof ApiError && error.errors?.['code']
                    ? error.errors['code'].join(' ')
                    : error instanceof ApiError && error.status === 429
                      ? $localize`Too many attempts: wait a minute, then try again.`
                      : (error as Error).message,
            );
        } finally {
            this.busy.set(false);
        }
    }

    protected async save(event: Event): Promise<void> {
        event.preventDefault();
        const form = this.form();
        if (!form) return;
        this.busy.set(true);
        try {
            const saved = await form.save();
            if (saved) {
                this.restartRequired.set(saved.restartRequired);
                this.toasts.success($localize`Installation settings saved.`);
            }
        } finally {
            this.busy.set(false);
        }
    }
}
