import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { InstallationView } from '../core/access.models';
import { SetupInfo } from './account.models';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { InstallationFormComponent } from './installation-form.component';
import { generatedCodeWhere, SetupCodeStepsComponent } from './setup-code.component';
import { runRequest } from '../wizards/wizard-run';

/** The setup's steps: the code, then the library folder and network (skipped in recovery), then the administrator. */
type SetupStep = 'code' | 'library' | 'account' | 'restart';

/**
 * The first administrator (`setup`). Nothing else works until one exists. The page needs the setup code: the one the
 * server made itself (shown in its console) or one set in its environment; without either, it says how to set one for
 * the way the server runs. Then it asks for the mode (Local or External), the library folder, who may open SFE and
 * ffmpeg (the installation page, saved before the administrator exists), then for the administrator. A new install
 * opened in a browser on the server's own computer skips the code (`codeRequired` false). With `Sfe:Setup:Recover`, the
 * same page resets the password of a local administrator (or adds one), without the library step.
 */
@Component({
    selector: 'sfe-setup-page',
    imports: [IconComponent, SetupCodeStepsComponent, InstallationFormComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-page' },
    templateUrl: './setup-page.component.html',
})
export class SetupPageComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);
    private readonly installationForm = viewChild(InstallationFormComponent);

    protected readonly info = signal<SetupInfo | null>(null);
    protected readonly loadError = signal<string | null>(null);
    protected readonly step = signal<SetupStep>('code');
    protected readonly token = signal<string | null>(null);
    protected readonly installation = signal<InstallationView | null>(null);
    private readonly restartRequired = signal(false);
    protected readonly code = signal('');
    protected readonly userName = signal('');
    protected readonly displayName = signal('');
    protected readonly password = signal('');
    protected readonly confirm = signal('');
    protected readonly libraryName = signal('');
    protected readonly fastSignIn = signal(false);
    protected readonly busy = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly errors = signal<Record<string, string>>({});

    protected readonly mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());
    protected readonly codeWhere = computed(() =>
        this.info() ? generatedCodeWhere(this.info()!.hosting, this.info()!.codeFile ?? null) : '',
    );
    /** A new install opened on the server's own computer: no code step. */
    protected readonly codeFree = computed(() => this.info()?.codeRequired === false && !this.info()!.recover);
    /**
     * The server opens a browser when it starts (portable packages): it can sign the administrator in there, when
     * Local.
     */
    protected readonly offerFastSignIn = computed(
        () =>
            !this.info()?.recover &&
            (this.info()?.hosting === 'portable' || this.info()?.hosting === 'appImage') &&
            this.installation()?.modeInEffect !== 'external',
    );
    protected readonly stepCount = computed(() => (this.info()?.recover || this.codeFree() ? 2 : 3));
    protected readonly stepNumber = computed(() => {
        if (!this.info()?.codeConfigured && !this.codeFree()) return null;
        switch (this.step()) {
            case 'code':
                return 1;
            case 'library':
                return this.codeFree() ? 1 : 2;
            case 'account':
                return this.stepCount();
            default:
                return null;
        }
    });

    protected readonly backToCode = $localize`Go back to the setup code`;
    protected readonly backToLibrary = $localize`Go back to the library folder`;
    protected readonly submitTitle = computed(() =>
        this.info()?.recover
            ? $localize`Give the administrator the new password and sign in`
            : $localize`Create the administrator and sign in`,
    );

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    constructor() {
        inject(Title).setTitle($localize`Setup – SFE`);
    }

    ngOnInit(): Promise<void> {
        return this.load();
    }

    protected async load(): Promise<void> {
        try {
            const info = await this.api.setupInfo();
            this.loadError.set(null);
            if (!info.available) {
                void this.router.navigate(['/'], { replaceUrl: true });
                return;
            }
            this.info.set(info);
            if (!this.libraryName()) this.libraryName.set(info.libraryName);
        } catch (error) {
            this.loadError.set((error as Error).message);
            return;
        }
        if (this.codeFree() && this.step() === 'code') await this.openWithoutCode();
    }

    /** Step 1 without the code: the installation settings open at once. */
    protected async openWithoutCode(): Promise<void> {
        await this.run(async () => {
            const grant = await this.api.checkInstallation('');
            this.token.set(grant.token);
            this.installation.set(grant.settings);
            this.step.set('library');
        });
    }

    /** Step 1: the code opens the installation settings (and is sent again with the administrator). */
    protected async checkCode(event: Event): Promise<void> {
        event.preventDefault();
        await this.run(async () => {
            const grant = await this.api.checkInstallation(this.code().trim());
            this.token.set(grant.token);
            this.installation.set(grant.settings);
            this.step.set(this.info()?.recover ? 'account' : 'library');
        });
    }

    /** Step 2: the library folder, network and ffmpeg, saved at once (the library starts again on its new folder). */
    protected async saveLibrary(event: Event): Promise<void> {
        event.preventDefault();
        const form = this.installationForm();
        if (!form) return;
        this.busy.set(true);
        try {
            const saved = await form.save();
            if (saved) {
                this.installation.set(saved.settings);
                this.restartRequired.set(saved.restartRequired);
                this.step.set('account');
            }
        } finally {
            this.busy.set(false);
        }
    }

    /** Step 3: the administrator, signed in at once. */
    protected async submit(event: Event): Promise<void> {
        event.preventDefault();
        if (this.mismatch()) return;
        await this.run(async () => {
            await this.api.completeSetup({
                code: this.codeFree() ? '' : this.code().trim(),
                userName: this.userName().trim(),
                password: this.password(),
                displayName: this.displayName().trim() || null,
                libraryName: this.info()?.recover ? null : this.libraryName().trim() || null,
                fastSignIn: this.offerFastSignIn() && this.fastSignIn(),
            });
            this.config.clear();
            if (this.restartRequired()) this.step.set('restart');
            else this.finish();
        });
    }

    protected finish(): void {
        void this.router.navigate(['/'], { replaceUrl: true });
    }

    /** Runs a request of the page: its errors are shown by their fields, or above the button. */
    private async run(action: () => Promise<void>): Promise<void> {
        await runRequest({ busy: this.busy, error: this.error, errors: this.errors }, action);
    }
}
