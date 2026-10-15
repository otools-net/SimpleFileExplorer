import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { CommandResult, SystemInfo } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { InstallationSectionComponent } from './installation-section.component';
import { VideoStreamsComponent } from './video-streams.component';

/**
 * "System" on the administration page. About (`tab` = `about`): the server's version, the library's folder and ffmpeg
 * (`server.settings`), the API reference, backups (`server.backup`), and the server commands when they are configured.
 * Installation (`installation`): the library folder, network and ffmpeg, behind the setup code (`server.settings`).
 * Video streams (`streams`): the videos the server converts now (`server.streams`). (Each browser's own theme is in Interface › Appearance, next to the default one.)
 */
@Component({
    selector: 'sfe-system-section',
    imports: [IconComponent, InstallationSectionComponent, VideoStreamsComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel system-section' },
    template: `
        @if (tab() === 'streams') {
            <sfe-video-streams />
        } @else if (tab() === 'installation') {
            <sfe-installation-section />
        } @else {
            <section class="settings-group">
                <header class="settings-group-head"><h3>Server</h3></header>
                <dl class="system-facts">
                    <div><dt>Version</dt><dd>SFE {{ config()?.version }}</dd></div>
                    @if (config()?.commit; as commit) {
                        <div><dt>Commit</dt><dd><code>{{ commit }}</code></dd></div>
                    }
                    <div><dt>Library</dt><dd>{{ config()?.rootName }}</dd></div>
                    @if (info(); as info) {
                        <div><dt>Library folder</dt><dd><code class="system-root">{{ info.root }}</code></dd></div>
                        <div>
                            <dt>ffmpeg</dt>
                            <dd class="system-ffmpeg" [attr.data-found]="info.ffmpeg.path ? 'yes' : 'no'">
                                @if (info.ffmpeg.path; as path) {
                                    <span class="system-badge found"><svg sfeIcon="check"></svg>Found</span>
                                    <code>{{ path }}</code>
                                    @if (info.ffmpeg.version) {
                                        <span class="muted">version {{ info.ffmpeg.version }}</span>
                                    }
                                } @else {
                                    <span class="system-badge missing"><svg sfeIcon="close"></svg>Not found</span>
                                    <span class="field-hint">
                                        Looked for <code>{{ info.ffmpeg.configured }}</code>: video thumbnails, trickplay, video
                                        resolutions and durations, and converted videos are off. Pick its folder in
                                        System › Installation.
                                    </span>
                                }
                            </dd>
                        </div>
                    } @else if (infoError()) {
                        <div><dt>Library folder</dt><dd class="muted">Could not be read: {{ infoError() }}</dd></div>
                    }
                </dl>
                <div class="settings-action inline">
                    <a class="button small" href="api/docs" target="_blank" rel="noopener" title="Documentation of the server API, in a new tab"><svg sfeIcon="docs"></svg>API reference</a>
                </div>
            </section>

            @if (canBackup()) {
                <section class="settings-group">
                    <header class="settings-group-head">
                        <h3>Backup</h3>
                        <p class="field-hint">
                            A zip file of the databases (accounts, spaces, and every library's index and tags) and of the configuration
                            files. Thumbnails and trickplay are left out: they are made again. It holds password hashes and connection
                            strings: keep it somewhere safe.
                        </p>
                    </header>
                    <div class="settings-action inline">
                        <a class="button small" href="api/system/backup" download title="Download a backup of the databases and configuration files (zip)"><svg sfeIcon="download"></svg>Download a backup</a>
                    </div>
                </section>
            }

            <section class="settings-group">
                <header class="settings-group-head">
                    <h3>Server commands</h3>
                    <p class="field-hint">Commands of the server's computer, set in its configuration file. Each asks first.</p>
                </header>
                @if (hasCommands()) {
                    <div class="settings-action inline">
                        @if (config()?.features?.dismount) {
                            <button type="button" class="button small" title="Run the server command that dismounts the drives (asks first)" (click)="dismount()"><svg sfeIcon="eject"></svg>Dismount drives</button>
                        }
                        @if (config()?.features?.shutdown) {
                            <button type="button" class="button small danger" title="Run the server command that shuts it down (asks first)" (click)="shutdown()"><svg sfeIcon="power"></svg>Shut down server</button>
                        }
                    </div>
                } @else {
                    <p class="muted">None configured.</p>
                }
            </section>
        }
    `,
})
export class SystemSectionComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly configService = inject(ConfigService);
    protected readonly config = this.configService.config;
    protected readonly canBackup = computed(() => this.configService.can(Rights.backup));

    /** The sub-page: `about`, `installation` or `streams`. */
    readonly tab = input('about');

    /** Where the library is and the ffmpeg found: for server administrators (`server.settings`). */
    protected readonly info = signal<SystemInfo | null>(null);
    protected readonly infoError = signal<string | null>(null);

    protected readonly hasCommands = computed(() => Boolean(this.config()?.features.shutdown || this.config()?.features.dismount));

    constructor() {
        if (this.configService.can(Rights.serverSettings)) {
            this.api.systemInfo().then(
                info => this.info.set(info),
                (error: Error) => this.infoError.set(error.message));
        }
    }

    protected shutdown(): void {
        if (confirm('Shut down the server?'))
            void this.runCommand(this.api.shutdown, 'Shutdown');
    }

    protected dismount(): void {
        if (confirm('Dismount the drives?'))
            void this.runCommand(this.api.dismount, 'Dismount');
    }

    private async runCommand(command: () => Promise<CommandResult>, label: string): Promise<void> {
        try {
            const result = await command();
            this.toasts.show(result?.output ? `${label}: ${result.output}` : `${label} requested.`);
        } catch (error) {
            this.toasts.error(`${label} failed: ${(error as Error).message}`);
        }
    }
}
