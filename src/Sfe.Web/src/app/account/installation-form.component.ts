import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { InstallationView, ServerMode } from '../core/access.models';
import { FolderPicker } from '../core/models';
import { InstallationInput, InstallationSaved } from './account.models';
import { valueOf } from '../shared/events';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { IconComponent } from '../shared/icon.component';

/**
 * The installation's settings a person setting SFE up needs, without editing a file: how SFE is used (Local or
 * External), the first space's library folder, who can open SFE (this computer, or the network) and ffmpeg. Shown by
 * the setup page and by Administration › System › Installation, both after the setup code was checked (`token`); the
 * page calls `save()`. A value the server's environment sets (variable, command line) is shown, not editable.
 */
@Component({
    selector: 'sfe-installation-form',
    imports: [FolderPickerComponent, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'installation-form' },
    templateUrl: './installation-form.component.html',
})
export class InstallationFormComponent implements OnInit {
    private readonly api = inject(Api);

    /** From the setup code's check (`checkInstallation`). */
    readonly token = input.required<string>();
    /** The settings as the server has them (answered by the check, then by each save). */
    readonly initial = input.required<InstallationView>();
    /** Shown by the setup page: the mode may still go back from External to Local. */
    readonly setup = input(false);

    protected readonly saved = signal<InstallationView | null>(null);
    protected readonly settings = computed(() => this.saved() ?? this.initial());

    protected readonly chosenMode = signal<ServerMode>('local');
    /**
     * External is chosen (saved or not yet): the library folder and ffmpeg come from the server's configuration only.
     */
    protected readonly external = computed(() => !!this.settings().mode && this.chosenMode() === 'external');
    /**
     * Local is chosen on the server's own computer only, and an External server goes back to it only in its
     * configuration.
     */
    protected readonly localAllowed = computed(() => {
        const view = this.settings();
        return !!view.onThisComputer && (this.setup() || view.modeInEffect !== 'external' || view.mode?.value == null);
    });
    protected readonly localNote = computed(() => {
        const view = this.settings();
        if (this.localAllowed())
            return view.mode?.value == null && !this.setup()
                ? $localize`Not chosen yet: until it is, SFE works as Local.`
                : null;
        return view.onThisComputer
            ? $localize`SFE is External: only the server's configuration (Sfe:Mode) can make it Local again.`
            : $localize`Local can only be chosen from a browser on the server itself, at http://localhost.`;
    });
    /** Shown for a value the server has none of. */
    protected readonly notSet = $localize`:no value:not set`;

    protected readonly root = signal<string | null>(null);
    protected readonly network = signal(false);
    protected readonly port = signal(5080);
    protected readonly ffmpeg = signal('');
    protected readonly pickFfmpeg = signal(false);
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);

    protected readonly valueOf = valueOf;

    /** The folder picker's source: the server's disks, through the token. */
    protected readonly folders = (path: string | null): Promise<FolderPicker> =>
        this.api.serverFolders(this.token(), path);

    protected listFailed(reason: string): void {
        this.problem.set($localize`Could not list the folders: ${reason}:reason:`);
    }

    ngOnInit(): void {
        this.reset(this.initial());
    }

    /** What differs from the server's values; a locked one is never sent. */
    readonly changes = computed<InstallationInput>(() => {
        const view = this.settings();
        const input: InstallationInput = {};
        if (!view.root.locked && !this.external() && this.root() && this.root() !== view.root.value)
            input.root = this.root();
        if (!view.urls.locked && (this.network() !== view.network || this.port() !== view.port)) {
            input.network = this.network();
            input.port = this.port();
        }
        if (!view.ffmpegPath.locked && !this.external() && this.ffmpeg().trim() !== (view.ffmpegPath.value ?? ''))
            input.ffmpegPath = this.ffmpeg().trim();
        // A mode not chosen yet is written as shown, so that the install says which one it is.
        if (view.mode && !view.mode.locked && (view.mode.value == null || this.chosenMode() !== view.modeInEffect))
            input.mode = this.chosenMode();
        return input;
    });

    /**
     * Saves what changed (nothing to send: the values as they are).
     * @returns The settings saved, or null when the server refused them (the errors are shown by their fields).
     */
    async save(): Promise<InstallationSaved | null> {
        this.errors.set({});
        this.problem.set(null);
        const changes = this.changes();
        if (!Object.keys(changes).length)
            return { settings: this.settings(), restartRequired: this.settings().restartRequired };
        try {
            const result = await this.api.saveInstallation(this.token(), changes);
            this.saved.set(result.settings);
            this.reset(result.settings);
            return result;
        } catch (error) {
            if (error instanceof ApiError && error.errors)
                this.errors.set(
                    Object.fromEntries(
                        Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')]),
                    ),
                );
            else this.problem.set((error as Error).message);
            return null;
        }
    }

    private reset(view: InstallationView): void {
        this.root.set(view.root.value);
        this.network.set(view.network);
        this.port.set(view.port);
        this.ffmpeg.set(view.ffmpegPath.value ?? '');
        this.pickFfmpeg.set(false);
        // Not chosen yet: Local for whoever is on the server's own computer, External for the others.
        this.chosenMode.set(
            view.mode?.value == null ? (view.onThisComputer ? 'local' : 'external') : (view.modeInEffect ?? 'local'),
        );
    }
}
