import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HostingKind } from '../core/models';

/** How to give the server its setup code, by the way it runs ([title, steps]). */
export const SETUP_CODE_STEPS: Record<HostingKind, [string, string]> = {
    windowsService: ['Windows service',
        'Add "Setup": { "Code": "…" } to the "Sfe" section of appsettings.json, next to SFE\'s program, or set the system environment variable Sfe__Setup__Code; then restart the SFE service.'],
    systemd: ['systemd service',
        'Add Environment=Sfe__Setup__Code=… to the [Service] section of SFE\'s unit (systemctl edit sfe), then restart it (systemctl restart sfe).'],
    iis: ['IIS',
        'Add <environmentVariable name="Sfe__Setup__Code" value="…" /> to the environmentVariables of the site\'s web.config (or set it in IIS Manager\'s configuration editor), then recycle the application pool.'],
    docker: ['Container',
        'Give the container the environment variable Sfe__Setup__Code (docker run -e, the environment of a compose file, or the container settings of your NAS), then restart it.'],
    appImage: ['AppImage',
        'Add "Setup": { "Code": "…" } to the "Sfe" section of ~/.config/sfe/appsettings.json, or start SFE with the environment variable Sfe__Setup__Code; then start it again.'],
    portable: ['Portable package',
        'Add "Setup": { "Code": "…" } to the "Sfe" section of appsettings.json, next to SFE\'s program, or start it with the environment variable Sfe__Setup__Code; then start it again.'],
    other: ['Configuration or environment variable',
        'Set the environment variable Sfe__Setup__Code (or Sfe:Setup:Code in the configuration files), then restart the server.'],
};

/**
 * The steps giving the server a setup code, for the way it runs first and the others folded: shown by the setup page
 * and the update page while the server has none. Laid out in the page's `.account-steps` (the host adds no box).
 */
@Component({
    selector: 'sfe-setup-code-steps',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'setup-code-steps' },
    template: `
        @if (codeTooShort()) {
            <p class="form-error">The setup code the server has is too short: it needs at least {{ minimumCodeLength() }} characters.</p>
        }
        <h2>{{ steps()[0] }}</h2>
        <p>{{ steps()[1] }}</p>
        <details>
            <summary>Other ways SFE runs</summary>
            <dl>
                @for (other of otherSteps(); track other[0]) {
                    <dt>{{ other[0] }}</dt>
                    <dd>{{ other[1] }}</dd>
                }
            </dl>
        </details>
    `,
})
export class SetupCodeStepsComponent {
    readonly hosting = input<HostingKind>('other');
    readonly minimumCodeLength = input(12);
    readonly codeTooShort = input(false);

    protected readonly steps = computed(() => SETUP_CODE_STEPS[this.hosting()]);
    protected readonly otherSteps = computed(() => Object.entries(SETUP_CODE_STEPS)
        .filter(([kind]) => kind !== this.hosting())
        .map(([, steps]) => steps));
}
