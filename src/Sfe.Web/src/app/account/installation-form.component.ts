import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { FolderPicker, InstallationInput, InstallationSaved, InstallationView, ServerMode } from '../core/models';
import { valueOf } from '../shared/events';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { IconComponent } from '../shared/icon.component';

/**
 * The installation's settings a person setting SFE up needs, without editing a file: how SFE is used (Local or
 * External), the first space's library folder, who can open SFE (this computer, or the network) and ffmpeg. Shown by the setup page and by Administration › System ›
 * Installation, both after the setup code was checked (`token`); the page calls `save()`. A value the server's
 * environment sets (variable, command line) is shown, not editable.
 */
@Component({
    selector: 'sfe-installation-form',
    imports: [FolderPickerComponent, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'installation-form' },
    template: `
        @let view = settings();
        @if (view.mode; as mode) {
            <fieldset class="field installation-mode">
                <legend class="field-label">How SFE is used</legend>
                @if (mode.locked) {
                    <p class="installation-locked"><code>{{ mode.value }}</code><span class="field-hint">Set by {{ mode.lockedBy }}.</span></p>
                } @else {
                    <label class="check">
                        <input type="radio" name="installation-mode" value="local" [checked]="chosenMode() === 'local'" [disabled]="!localAllowed()"
                               (change)="chosenMode.set('local')">
                        <span>Local
                            <span class="field-hint">SFE on your own computer: only this computer opens it, and only administrators sign in.</span>
                        </span>
                    </label>
                    <label class="check">
                        <input type="radio" name="installation-mode" value="external" [checked]="chosenMode() === 'external'" (change)="chosenMode.set('external')">
                        <span>External
                            <span class="field-hint">
                                Other people open SFE from their devices, over the network or the internet. The library folders and ffmpeg are then set in
                                the server's configuration (environment variables or a settings file), not on this page.
                            </span>
                        </span>
                    </label>
                    @if (localNote(); as note) {
                        <span class="field-hint installation-mode-note">{{ note }}</span>
                    }
                }
                @if (errors()['mode']; as error) {
                    <span class="field-error">{{ error }}</span>
                }
            </fieldset>
        }

        <div class="field">
            <span class="field-label">Library folder</span>
            @if (view.root.locked) {
                <p class="installation-locked"><code>{{ view.root.value || 'not set' }}</code><span class="field-hint">Set by {{ view.root.lockedBy }}.</span></p>
            } @else if (external()) {
                <p class="installation-locked"><code>{{ view.root.value || 'not set' }}</code>
                    <span class="field-hint">An External server takes it from its configuration (Sfe__Root in its environment, or appsettings.json).</span></p>
            } @else {
                <sfe-folder-picker [source]="folders" [initial]="root()" placeholder="Pick the folder of your photos, videos and music"
                                   (picked)="root.set($event)" (failed)="problem.set('Could not list the folders: ' + $event)" />
                <span class="field-hint">The folder whose files SFE shows, with everything below it. Click a folder to choose it; click again inside it to go deeper.</span>
                @if (!view.rootExists && root() === view.root.value) {
                    <span class="field-error">This folder does not exist on the server now: pick another.</span>
                }
            }
            @if (errors()['root']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </div>

        <fieldset class="field installation-network">
            <legend class="field-label">Who can open SFE</legend>
            @if (view.urls.locked) {
                <p class="installation-locked"><code>{{ view.urls.value }}</code><span class="field-hint">Set by {{ view.urls.lockedBy }}.</span></p>
            } @else {
                @if (view.mode && chosenMode() === 'local') {
                    <p class="installation-local">Only this computer, as SFE is Local<span class="field-hint">Open it at http://localhost:{{ port() }}.</span></p>
                } @else {
                    <label class="check">
                        <input type="radio" name="installation-network" [checked]="!network()" (change)="network.set(false)">
                        <span>Only this computer<span class="field-hint">Open it at http://localhost:{{ port() }}.</span></span>
                    </label>
                    <label class="check">
                        <input type="radio" name="installation-network" [checked]="network()" (change)="network.set(true)">
                        <span>Also the other devices of my network
                            <span class="field-hint">Phones, TVs and other computers at home open http://<em>this computer's name or address</em>:{{ port() }}. Everyone still signs in.</span>
                        </span>
                    </label>
                }
                <div class="field installation-port">
                    <label class="field-label" for="installation-port">Port</label>
                    <input id="installation-port" type="number" min="1" max="65535" [value]="port()" (input)="port.set(+valueOf($event))">
                </div>
            }
            @if (errors()['port']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </fieldset>

        <div class="field installation-ffmpeg">
            <span class="field-label">ffmpeg <span class="field-hint">(optional)</span></span>
            <p class="system-ffmpeg" [attr.data-found]="view.ffmpegFound ? 'yes' : 'no'">
                @if (view.ffmpegFound; as found) {
                    <span class="system-badge found"><svg sfeIcon="check"></svg>Found</span>
                    <code>{{ found }}</code>
                    @if (view.ffmpegVersion) {
                        <span class="muted">version {{ view.ffmpegVersion }}</span>
                    }
                } @else {
                    <span class="system-badge missing"><svg sfeIcon="close"></svg>Not found</span>
                }
            </p>
            <span class="field-hint">
                ffmpeg makes the video thumbnails, trickplay, durations and converted videos. Without it, everything else works. To add it,
                download it from ffmpeg.org, unzip it, then pick its folder here.
            </span>
            @if (view.ffmpegPath.locked) {
                <p class="installation-locked"><code>{{ view.ffmpegPath.value || 'ffmpeg' }}</code><span class="field-hint">Set by {{ view.ffmpegPath.lockedBy }}.</span></p>
            } @else if (external()) {
                <p class="installation-locked"><code>{{ view.ffmpegPath.value || 'ffmpeg' }}</code>
                    <span class="field-hint">An External server takes it from its configuration (Sfe__Thumbnails__FfmpegPath in its environment, or appsettings.json).</span></p>
            } @else {
                <div class="installation-ffmpeg-path">
                    <input id="installation-ffmpeg" type="text" spellcheck="false" placeholder="ffmpeg (looked for on the PATH)" aria-label="ffmpeg's program or folder"
                           [value]="ffmpeg()" (input)="ffmpeg.set(valueOf($event))">
                    <button type="button" class="button small" title="Pick the folder ffmpeg is in" (click)="pickFfmpeg.set(!pickFfmpeg())">
                        <svg sfeIcon="folder"></svg>Browse
                    </button>
                </div>
                @if (pickFfmpeg()) {
                    <sfe-folder-picker [source]="folders" placeholder="Pick the folder ffmpeg is in (or its bin folder)"
                                       (picked)="ffmpeg.set($event)" (failed)="problem.set('Could not list the folders: ' + $event)" />
                }
            }
            @if (errors()['ffmpegPath']; as error) {
                <span class="field-error">{{ error }}</span>
            }
        </div>

        @if (problem(); as problem) {
            <p class="form-error" role="alert">{{ problem }}</p>
        }
    `,
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
    /** External is chosen (saved or not yet): the library folder and ffmpeg come from the server's configuration only. */
    protected readonly external = computed(() => !!this.settings().mode && this.chosenMode() === 'external');
    /** Local is chosen on the server's own computer only, and an External server goes back to it only in its configuration. */
    protected readonly localAllowed = computed(() => {
        const view = this.settings();
        return !!view.onThisComputer && (this.setup() || view.modeInEffect !== 'external' || view.mode?.value == null);
    });
    protected readonly localNote = computed(() => {
        const view = this.settings();
        if (this.localAllowed())
            return view.mode?.value == null && !this.setup() ? 'Not chosen yet: until it is, SFE works as Local.' : null;
        return view.onThisComputer
            ? 'SFE is External: it goes back to Local only in the server\'s configuration (Sfe:Mode).'
            : 'Local is chosen from a browser on the server itself, at http://localhost.';
    });

    protected readonly root = signal<string | null>(null);
    protected readonly network = signal(false);
    protected readonly port = signal(5080);
    protected readonly ffmpeg = signal('');
    protected readonly pickFfmpeg = signal(false);
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);

    protected readonly valueOf = valueOf;

    /** The folder picker's source: the server's disks, through the token. */
    protected readonly folders = (path: string | null): Promise<FolderPicker> => this.api.serverFolders(this.token(), path);

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
                this.errors.set(Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')])));
            else
                this.problem.set((error as Error).message);
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
        this.chosenMode.set(view.mode?.value == null ? (view.onThisComputer ? 'local' : 'external') : view.modeInEffect ?? 'local');
    }
}
