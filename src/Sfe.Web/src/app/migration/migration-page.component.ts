import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { generatedCodeWhere, SetupCodeStepsComponent } from '../account/setup-code.component';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatSize } from '../core/format';
import { BackupLink, MigrationInfo } from './migration.models';
import { valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { changelogParts } from './changelog';

/** How often the page asks how the update is going. */
export const MIGRATION_POLL_MS = 1000;

/**
 * The update of an install to the running version (`migration`): after an upgrade, nothing else works until someone
 * with the setup code confirms it here. The page lists what changed since the installed version, offers a backup of
 * the databases and configuration files first, then follows the update. Nothing is changed before the confirmation.
 */
@Component({
    selector: 'sfe-migration-page',
    imports: [IconComponent, SetupCodeStepsComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-page' },
    templateUrl: './migration-page.component.html',
})
export class MigrationPageComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);

    protected readonly info = signal<MigrationInfo | null>(null);
    protected readonly loadError = signal<string | null>(null);
    protected readonly code = signal('');
    protected readonly codeError = signal<string | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly busy = signal(false);
    protected readonly backedUp = signal<BackupLink | null>(null);
    /** This page ran the update, which is now done: it says so rather than leaving at once. */
    protected readonly done = computed(() => this.started() && this.info()?.status === 'none');
    private readonly started = signal(false);
    private timer: ReturnType<typeof setTimeout> | null = null;

    protected readonly valueOf = valueOf;
    protected readonly codeWhere = (info: MigrationInfo) => generatedCodeWhere(info.hosting, info.codeFile ?? null);
    protected readonly parts = changelogParts;
    protected readonly size = formatSize;

    constructor() {
        inject(Title).setTitle($localize`Update – SFE`);
        inject(DestroyRef).onDestroy(() => this.stopPolling());
    }

    ngOnInit(): Promise<void> {
        return this.load();
    }

    protected async load(): Promise<void> {
        this.stopPolling();
        try {
            const info = await this.api.migrationInfo(this.code().trim());
            this.loadError.set(null);
            this.info.set(info);
            if (info.status === 'none' && !this.started()) {
                void this.router.navigate(['/'], { replaceUrl: true });
                return;
            }
            if (info.status === 'running' || info.status === 'unavailable')
                this.timer = setTimeout(() => void this.load(), MIGRATION_POLL_MS);
            if (info.status === 'running') this.started.set(true);
        } catch (error) {
            this.loadError.set((error as Error).message);
        }
    }

    protected async start(event: Event): Promise<void> {
        event.preventDefault();
        const info = this.info();
        if (
            !info ||
            !confirm(
                $localize`Update the databases and settings to SFE ${info.appVersion}:version:? \
Earlier versions cannot use them afterwards: this cannot be undone.`,
            )
        )
            return;
        await this.send(async (code) => {
            this.info.set(await this.api.startMigration(code));
            this.started.set(true);
            await this.load();
        });
    }

    protected async backup(): Promise<void> {
        await this.send(async (code) => {
            const link = await this.api.migrationBackup(code);
            this.backedUp.set(link);
            // The browser downloads it as any file, from a link that works once.
            const anchor = document.createElement('a');
            anchor.href = link.url;
            anchor.download = link.fileName;
            document.body.append(anchor);
            anchor.click();
            anchor.remove();
        });
    }

    protected open(): void {
        // The session and the settings of before the update are read again.
        this.config.clear();
        void this.router.navigate(['/'], { replaceUrl: true });
    }

    private async send(action: (code: string) => Promise<void>): Promise<void> {
        this.busy.set(true);
        this.error.set(null);
        this.codeError.set(null);
        try {
            await action(this.code().trim());
        } catch (error) {
            if (error instanceof ApiError && error.errors?.['code']) this.codeError.set(error.errors['code'].join(' '));
            else this.error.set((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }

    private stopPolling(): void {
        if (this.timer !== null) clearTimeout(this.timer);
        this.timer = null;
    }
}
