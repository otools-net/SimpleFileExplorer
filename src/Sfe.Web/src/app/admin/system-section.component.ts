import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { CommandResult } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';

/**
 * "System" on the administration page: the server's version and API reference, and the server commands when they
 * are configured. (Each browser's own theme is in Interface › Appearance, next to the default one.)
 */
@Component({
    selector: 'sfe-system-section',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel system-section' },
    template: `
        <section class="settings-group">
            <header class="settings-group-head"><h3>Server</h3></header>
            <dl class="system-facts">
                <div><dt>Version</dt><dd>SFE {{ config()?.version }}</dd></div>
                @if (config()?.commit; as commit) {
                    <div><dt>Commit</dt><dd><code>{{ commit }}</code></dd></div>
                }
                <div><dt>Library</dt><dd>{{ config()?.rootName }}</dd></div>
            </dl>
            <div class="settings-action inline">
                <a class="button small" href="api/docs" target="_blank" rel="noopener" title="Documentation of the server API, in a new tab"><svg sfeIcon="docs"></svg>API reference</a>
            </div>
        </section>

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
    `,
})
export class SystemSectionComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    protected readonly config = inject(ConfigService).config;

    protected readonly hasCommands = computed(() => Boolean(this.config()?.features.shutdown || this.config()?.features.dismount));

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
