import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SystemInfo } from './admin.models';
import { Rights } from '../core/rights';
import { IconComponent } from '../shared/icon.component';
import { InstallationSectionComponent } from './installation-section.component';

/**
 * "System" on the administration page. About (`tab` = `about`): the server's version, the library's folder and ffmpeg
 * (`server.settings`) and the API reference. Installation (`installation`): the library folder, network and ffmpeg,
 * behind the setup code (`server.settings`). Maintenance (`maintenance`): backups (`server.backup`). (The videos the
 * server converts are in Playback › Video streams.)
 */
@Component({
    selector: 'sfe-system-section',
    imports: [IconComponent, InstallationSectionComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel system-section' },
    templateUrl: './system-section.component.html',
})
export class SystemSectionComponent {
    private readonly api = inject(Api);
    private readonly configService = inject(ConfigService);
    protected readonly config = this.configService.config;
    protected readonly canBackup = computed(() => this.configService.can(Rights.backup));

    /** The sub-page: `about`, `installation` or `maintenance`. */
    readonly tab = input('about');

    /** Where the library is and the ffmpeg found: for server administrators (`server.settings`). */
    protected readonly info = signal<SystemInfo | null>(null);
    protected readonly infoError = signal<string | null>(null);

    constructor() {
        if (this.configService.can(Rights.serverSettings)) {
            this.api.systemInfo().then(
                (info) => this.info.set(info),
                (error: Error) => this.infoError.set(error.message),
            );
        }
    }
}
