import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { DirectoryListing } from '../core/models';
import { IconComponent } from '../shared/icon.component';

/** Folders listed at most in a folder of the picker (open one to go further). */
export const FOLDERS_SHOWN = 200;

/**
 * The folder picked by clicking through the library, in the dialogs that put something into a folder (Move to…,
 * Copy to…, Extract…, Compress…): the folder chosen, the way to it, its subfolders, and whether a right applies there.
 * The dialog holds it (`folderBrowser()`), <sfe-folder-browser> shows it.
 */
export class FolderBrowser {
    /** The folder chosen. */
    readonly target = signal('');
    readonly listing = signal<DirectoryListing | null>(null);
    readonly loading = signal(false);

    readonly folders = computed(() => this.listing()?.items.filter(item => item.kind === 'directory') ?? []);
    readonly truncated = computed(() => (this.listing()?.directoryCount ?? 0) > this.folders().length);
    readonly crumbs = computed(() => {
        const root = { path: '', name: this.config.config()?.rootName || 'Library' };
        const parts = this.target() ? this.target().split('/') : [];
        return [root, ...parts.map((name, index) => ({ path: parts.slice(0, index + 1).join('/'), name }))];
    });

    constructor(private readonly api: Api, private readonly config: ConfigService) { }

    /** `right` may be used in the folder shown (its folder rules, else the person's rights); false before it is read. */
    allows(right: string): boolean {
        const listing = this.listing();
        if (!listing)
            return false;
        return listing.access ? !listing.access.pathOnly && listing.access.rights.includes(right) : this.config.can(right);
    }

    /** Forgets the folder read: until the next `browse`, nothing is allowed. */
    reset(target = ''): void {
        this.target.set(target);
        this.listing.set(null);
    }

    async browse(path: string): Promise<void> {
        this.target.set(path);
        this.loading.set(true);
        try {
            const listing = await this.api.browse({ path, offset: 0, limit: FOLDERS_SHOWN, sort: 'name', desc: false });
            if (this.target() === path)
                this.listing.set(listing);
        } catch {
            // A folder gone meanwhile: back to the top.
            if (path)
                await this.browse('');
        } finally {
            this.loading.set(false);
        }
    }
}

/** A folder browser for the dialog creating it (in its injection context). */
export function folderBrowser(): FolderBrowser {
    return new FolderBrowser(inject(Api), inject(ConfigService));
}

/** The way to the folder chosen (each step goes back to it) and its subfolders (each opens). */
@Component({
    selector: 'sfe-folder-browser',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <nav class="transfer-crumbs" aria-label="The folder chosen">
            @for (crumb of browser().crumbs(); track crumb.path; let last = $last) {
                @if (last) {
                    <strong><svg sfeIcon="folder"></svg>{{ crumb.name }}</strong>
                } @else {
                    <button type="button" class="link-button" [title]="'Go back to ' + crumb.name" (click)="browser().browse(crumb.path)">{{ crumb.name }}</button>
                    <span aria-hidden="true">/</span>
                }
            }
        </nav>
        <ul class="transfer-folders">
            @for (folder of browser().folders(); track folder.path) {
                <li>
                    @let closed = closedBecause()(folder.path);
                    <button type="button" class="transfer-folder" [disabled]="closed !== null"
                            [title]="closed ?? 'Open ' + folder.name" (click)="browser().browse(folder.path)">
                        <svg sfeIcon="folder"></svg><span>{{ folder.name }}</span>
                    </button>
                </li>
            } @empty {
                <li class="muted">{{ browser().loading() ? 'Loading…' : 'No folders in here.' }}</li>
            }
            @if (browser().truncated()) {
                <li class="muted">Only the first {{ shown }} folders are listed.</li>
            }
        </ul>
    `,
})
export class FolderBrowserComponent {
    readonly browser = input.required<FolderBrowser>();
    /** Why a folder can't be opened (a folder moved can't take itself); null when it can. */
    readonly closedBecause = input<(path: string) => string | null>(() => null);
    protected readonly shown = FOLDERS_SHOWN;
}
