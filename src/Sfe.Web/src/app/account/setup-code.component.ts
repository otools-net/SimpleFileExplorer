import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HostingKind } from './account.models';

/** How to give the server its setup code, by the way it runs ([title, steps]). */
export const SETUP_CODE_STEPS: Record<HostingKind, [string, string]> = {
    windowsService: [
        $localize`Windows service`,
        $localize`Add "Setup": { "Code": "…" } to the "Sfe" section of appsettings.json, next to SFE's program, \
or set the system environment variable Sfe__Setup__Code. Then restart the SFE service.`,
    ],
    systemd: [
        $localize`systemd service`,
        $localize`Add Environment=Sfe__Setup__Code=… to the [Service] section of SFE's unit (systemctl edit sfe), \
then restart it (systemctl restart sfe).`,
    ],
    iis: [
        $localize`IIS`,
        $localize`Add <environmentVariable name="Sfe__Setup__Code" value="…" /> to the environmentVariables of the \
site's web.config (or set it in IIS Manager's configuration editor), then recycle the application pool.`,
    ],
    docker: [
        $localize`Container`,
        $localize`Give the container the environment variable Sfe__Setup__Code (docker run -e, the environment of a \
compose file, or the container settings of your NAS), then restart it.`,
    ],
    appImage: [
        $localize`AppImage`,
        $localize`Add "Setup": { "Code": "…" } to the "Sfe" section of ~/.config/sfe/appsettings.json, or start SFE \
with the environment variable Sfe__Setup__Code. Then start it again.`,
    ],
    portable: [
        $localize`Portable package`,
        $localize`Add "Setup": { "Code": "…" } to the "Sfe" section of appsettings.json, next to SFE's program, or \
start it with the environment variable Sfe__Setup__Code. Then start it again.`,
    ],
    other: [
        $localize`Configuration or environment variable`,
        $localize`Set the environment variable Sfe__Setup__Code (or Sfe:Setup:Code in the configuration files), \
then restart the server.`,
    ],
};

/** Where the server's log goes, by the way it runs: a code the server made itself is shown there. */
const LOG_PLACES: Record<HostingKind, string> = {
    windowsService: $localize`It's shown in the Windows event log (Applications).`,
    systemd: $localize`It's shown in SFE's log (journalctl -u sfe).`,
    iis: $localize`It's shown in the site's log.`,
    docker: $localize`It's shown in the container's log (docker logs).`,
    appImage: $localize`It's shown in the window where SFE runs.`,
    portable: $localize`It's shown in the window where SFE runs.`,
    other: $localize`It's shown in the window or the log where SFE runs.`,
};

/**
 * Where to find the setup code the server made itself (two sentences): its log, and its file (`codeFile` as
 * configured; a relative one is in SFE's folder, ~/.config/sfe for the AppImage).
 */
export function generatedCodeWhere(hosting: HostingKind, codeFile: string | null): string {
    const file = codeFile ?? 'data/setup-code.txt';
    const relative = !/^([a-zA-Z]:|[\\/~])/.test(file);
    const kept = !relative
        ? $localize`It's also kept in ${file}:file:.`
        : hosting === 'appImage'
          ? $localize`It's also kept in ~/.config/sfe/${file}:file:.`
          : $localize`It's also kept in ${file}:file: in SFE's folder.`;
    return `${LOG_PLACES[hosting]} ${kept}`;
}

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
            <p class="form-error" i18n>The server's setup code is too short: it needs at least
            {{ minimumCodeLength() }} characters.</p>
        }
        <h2>{{ steps()[0] }}</h2>
        <p>{{ steps()[1] }}</p>
        <details>
            <summary i18n>Other ways SFE runs</summary>
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
    protected readonly otherSteps = computed(() =>
        Object.entries(SETUP_CODE_STEPS)
            .filter(([kind]) => kind !== this.hosting())
            .map(([, steps]) => steps),
    );
}
