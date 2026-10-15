import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { generatedCodeWhere, SetupCodeStepsComponent } from '../account/setup-code.component';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatSize } from '../core/format';
import { BackupLink, MigrationInfo } from '../core/models';
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
    template: `
        <section class="account-card wide migration-card">
            <header class="account-head">
                <span class="brand"><svg sfeIcon="folder"></svg><span>SFE</span></span>
                <h1>{{ done() ? 'SFE is up to date' : 'Update SFE' }}</h1>
            </header>
            @if (loadError(); as error) {
                <p class="form-error" role="alert">The server is not reachable: {{ error }}</p>
                <button type="button" class="button" title="Ask the server again" (click)="load()">Try again</button>
            } @else if (info(); as info) {
                @switch (done() ? 'done' : info.status) {
                    @case ('done') {
                        <p>The databases and settings now belong to SFE {{ info.appVersion }}.</p>
                        <ul class="migration-log">
                            @for (line of info.log; track $index) {
                                <li>{{ line }}</li>
                            }
                        </ul>
                        <button type="button" class="button primary account-submit" title="Open the library" (click)="open()">Open SFE</button>
                    }
                    @case ('unavailable') {
                        <p>SFE cannot read its database yet, so it does not know whether an update is due. It keeps trying.</p>
                        @if (info.error) {
                            <p class="form-error">{{ info.error }}</p>
                        }
                        <button type="button" class="button account-submit" title="Ask the server again" (click)="load()">Check again</button>
                    }
                    @case ('newer') {
                        <p>
                            The databases belong to SFE <strong>{{ info.databaseVersion }}</strong>, a later version than this one
                            ({{ info.appVersion }}). This version cannot use them: install SFE {{ info.databaseVersion }} again, or restore
                            a backup made with this version.
                        </p>
                    }
                    @case ('running') {
                        <p class="migration-running"><span class="spinner"></span>Updating to SFE {{ info.appVersion }}… Keep this page open.</p>
                        <ul class="migration-log">
                            @for (line of info.log; track $index) {
                                <li>{{ line }}</li>
                            }
                        </ul>
                    }
                    @default {
                        <p>
                            This server now runs SFE <strong>{{ info.appVersion }}</strong>; its databases and settings belong to
                            @if (info.databaseVersion) {
                                SFE <strong>{{ info.databaseVersion }}</strong>.
                            } @else {
                                an earlier version (before 1.37.0).
                            }
                            They must be updated before anyone can use SFE again. Nothing has been changed yet.
                        </p>
                        @if (info.status === 'failed') {
                            <div class="form-error" role="alert">
                                <p>The update stopped: {{ info.error }}</p>
                                <p>Fix the problem (see the server's log), then start the update again.</p>
                            </div>
                        }
                        @if (info.changes.length) {
                            <section class="migration-changes" aria-label="What changed">
                                <h2>What changed</h2>
                                @for (entry of info.changes; track entry.version) {
                                    <article class="migration-version">
                                        <h3>SFE {{ entry.version }}</h3>
                                        @for (section of entry.sections; track section.title) {
                                            <h4>{{ section.title }}</h4>
                                            <ul>
                                                @for (item of section.items; track $index) {
                                                    <li>
                                                        @for (part of parts(item); track $index) {
                                                            @switch (part.kind) {
                                                                @case ('code') { <code>{{ part.text }}</code> }
                                                                @case ('strong') { <strong>{{ part.text }}</strong> }
                                                                @default { {{ part.text }} }
                                                            }
                                                        }
                                                    </li>
                                                }
                                            </ul>
                                        }
                                    </article>
                                }
                            </section>
                        }
                    }
                }
                @if (!done() && (info.status === 'required' || info.status === 'failed' || info.status === 'newer')) {
                    @if (!info.codeConfigured) {
                        <div class="account-steps">
                            <p>
                                So that only the person running the server can update it, the server first needs a <strong>setup code</strong>
                                of at least {{ info.minimumCodeLength }} characters, given in its environment; this page then asks for it.
                            </p>
                            <sfe-setup-code-steps [hosting]="info.hosting" [minimumCodeLength]="info.minimumCodeLength" [codeTooShort]="info.codeTooShort" />
                            <button type="button" class="button primary" title="Ask the server whether it has a setup code now" (click)="load()">Check again</button>
                        </div>
                    } @else {
                        <form class="account-form" novalidate (submit)="start($event)">
                            <div class="field">
                                <label class="field-label" for="migration-code">Setup code</label>
                                <input id="migration-code" type="password" autocomplete="off" required [value]="code()" (input)="code.set(valueOf($event))"
                                       aria-describedby="migration-code-hint">
                                <span class="field-hint" id="migration-code-hint">
                                    @if (info.codeGenerated) {
                                        The code SFE made at its setup: {{ codeWhere(info) }}.
                                    } @else {
                                        The code given in the server's environment (Sfe:Setup:Code).
                                    }
                                </span>
                                @if (codeError(); as error) {
                                    <span class="field-error">{{ error }}</span>
                                }
                            </div>
                            <div class="migration-backup">
                                <p class="field-hint">
                                    A backup holds the databases and the configuration files, to go back to SFE
                                    {{ info.databaseVersion ?? 'of before' }} if needed. It holds password hashes: keep it somewhere safe.
                                </p>
                                <button type="button" class="button" title="Download a zip file of the databases and configuration files"
                                        [disabled]="busy() || !code()" (click)="backup()">
                                    <svg sfeIcon="download"></svg>Download a backup
                                </button>
                                @if (backedUp(); as link) {
                                    <span class="field-hint" role="status">Downloading {{ link.fileName }} ({{ size(link.size) }}).</span>
                                }
                            </div>
                            @if (error(); as error) {
                                <p class="form-error" role="alert">{{ error }}</p>
                            }
                            @if (info.status !== 'newer') {
                                <button type="submit" class="button primary account-submit" title="Update the databases and settings to this version"
                                        [disabled]="busy() || !code()">
                                    {{ info.status === 'failed' ? 'Start the update again' : 'Start the update' }}
                                </button>
                            }
                        </form>
                    }
                }
            } @else {
                <div class="tag-loading"><span class="spinner"></span></div>
            }
        </section>
    `,
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
        inject(Title).setTitle('Update – SFE');
        inject(DestroyRef).onDestroy(() => this.stopPolling());
    }

    ngOnInit(): Promise<void> {
        return this.load();
    }

    protected async load(): Promise<void> {
        this.stopPolling();
        try {
            const info = await this.api.migrationInfo();
            this.loadError.set(null);
            this.info.set(info);
            if (info.status === 'none' && !this.started()) {
                void this.router.navigate(['/'], { replaceUrl: true });
                return;
            }
            if (info.status === 'running' || info.status === 'unavailable')
                this.timer = setTimeout(() => void this.load(), MIGRATION_POLL_MS);
            if (info.status === 'running')
                this.started.set(true);
        } catch (error) {
            this.loadError.set((error as Error).message);
        }
    }

    protected async start(event: Event): Promise<void> {
        event.preventDefault();
        const info = this.info();
        if (!info || !confirm(`Update the databases and settings to SFE ${info.appVersion}? Earlier versions cannot use them afterwards.`))
            return;
        await this.send(async code => {
            this.info.set(await this.api.startMigration(code));
            this.started.set(true);
            await this.load();
        });
    }

    protected async backup(): Promise<void> {
        await this.send(async code => {
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
            if (error instanceof ApiError && error.errors?.['code'])
                this.codeError.set(error.errors['code'].join(' '));
            else
                this.error.set((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }

    private stopPolling(): void {
        if (this.timer !== null)
            clearTimeout(this.timer);
        this.timer = null;
    }
}
