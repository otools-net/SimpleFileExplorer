import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { DirectoryListing } from '../core/models';
import { baseName, parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileTransfers } from './file-transfers';

/** Folders listed at most in a folder of the picker (open one to go further). */
const FOLDERS_SHOWN = 200;

/** What is extracted: the archive, and in it the entries chosen in the viewer (none: all of it). */
export interface ExtractChoice {
    path: string;
    name: string;
    folders?: number[];
    files?: number[];
    /** What the entries chosen are, in words ("“Day 1”", "3 items"). */
    what?: string;
    /** The viewer's unlock of the archive: no password asked then. */
    unlock?: string | null;
}

/**
 * Extract…: the folder picked by clicking through the library (starting with the archive's), into a new folder named
 * after the archive or not, with the archive's password when it has one; then the job runs in the background, in the
 * operations panel (agents/docs/features/archive-extraction.md).
 */
@Component({
    selector: 'sfe-extract-dialog',
    imports: [BusyDirective, IconComponent, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Extract" variant="transfer-dialog">
            <p>{{ what() }} into:</p>
            <nav class="transfer-crumbs" aria-label="The folder chosen">
                @for (crumb of crumbs(); track crumb.path; let last = $last) {
                    @if (last) {
                        <strong><svg sfeIcon="folder"></svg>{{ crumb.name }}</strong>
                    } @else {
                        <button type="button" class="link-button" [title]="'Go back to ' + crumb.name" (click)="browse(crumb.path)">{{ crumb.name }}</button>
                        <span aria-hidden="true">/</span>
                    }
                }
            </nav>
            <ul class="transfer-folders">
                @for (folder of folders(); track folder.path) {
                    <li>
                        <button type="button" class="transfer-folder" [title]="'Open ' + folder.name" (click)="browse(folder.path)">
                            <svg sfeIcon="folder"></svg><span>{{ folder.name }}</span>
                        </button>
                    </li>
                } @empty {
                    <li class="muted">{{ loading() ? 'Loading…' : 'No folders in here.' }}</li>
                }
            </ul>
            @if (refusal(); as refusal) {
                <p class="field-hint">{{ refusal }}</p>
            }
            <label class="check">
                <input type="checkbox" data-new-folder [checked]="newFolder()" (change)="newFolder.set(checked($event))">
                <span>Into a new folder “{{ stem() }}”<span class="field-hint">Or named after its one folder, when it holds only that. Unchecked: what it holds goes in with what is there, asking about names taken.</span></span>
            </label>
            @if (!choice()?.unlock) {
                <label class="field">
                    <span>Password <span class="field-hint">only if it is protected by one</span></span>
                    <input [type]="showPassword() ? 'text' : 'password'" data-extract-password autocomplete="off" [value]="password()" (input)="password.set(value($event))">
                </label>
                <label class="check">
                    <input type="checkbox" [checked]="showPassword()" (change)="showPassword.set(checked($event))">
                    <span>Show the password</span>
                </label>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without extracting anything (Esc)" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="button" class="button primary" data-extract [sfeBusy]="busy()" [title]="'Extract into “' + targetName() + '”: it goes on in the background'"
                    [disabled]="busy() || refusal() !== null" (click)="confirm()">Extract here</button>
        </sfe-sheet>
    `,
})
export class ExtractDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly transfers = inject(FileTransfers);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly choice = signal<ExtractChoice | null>(null);
    /** The folder chosen. */
    protected readonly target = signal('');
    private readonly listing = signal<DirectoryListing | null>(null);
    protected readonly loading = signal(false);
    protected readonly busy = signal(false);
    protected readonly newFolder = signal(true);
    protected readonly password = signal('');
    protected readonly showPassword = signal(false);

    protected readonly folders = computed(() => this.listing()?.items.filter(item => item.kind === 'directory') ?? []);
    protected readonly crumbs = computed(() => {
        const root = { path: '', name: this.config.config()?.rootName || 'Library' };
        const parts = this.target() ? this.target().split('/') : [];
        return [root, ...parts.map((name, index) => ({ path: parts.slice(0, index + 1).join('/'), name }))];
    });
    protected readonly stem = computed(() => stemOf(this.choice()?.name ?? ''));
    protected readonly what = computed(() => {
        const choice = this.choice();
        return choice?.what ? `Extract ${choice.what} of “${choice.name}”` : `Extract “${choice?.name ?? ''}”`;
    });
    /** Why the folder chosen can't be used; null when it can. */
    protected readonly refusal = computed(() => {
        const listing = this.listing();
        if (!listing)
            return 'Pick a folder.';
        const allowed = listing.access ? !listing.access.pathOnly && listing.access.rights.includes(Rights.extractFiles) : this.config.can(Rights.extractFiles);
        return allowed ? null : 'You may not extract archives into this folder: open another.';
    });
    protected readonly targetName = computed(() => baseName(this.target()) || this.config.config()?.rootName || 'the library');

    constructor() {
        registerDialog(dialogs => dialogs.extract, this);
    }

    /** Extract…: the folder is picked, starting with the archive's. */
    open(choice: ExtractChoice): void {
        this.choice.set(choice);
        this.newFolder.set(true);
        this.password.set('');
        this.showPassword.set(false);
        this.listing.set(null);
        void this.browse(parentPath(choice.path));
        this.sheet().open();
    }

    protected async browse(path: string): Promise<void> {
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

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    protected value(event: Event): string {
        return valueOf(event);
    }

    protected async confirm(): Promise<void> {
        const choice = this.choice();
        if (!choice)
            return;
        const started = await whileBusy(this.busy, () => this.transfers.extract({
            archive: choice.path,
            target: this.target(),
            newFolder: this.newFolder(),
            folders: choice.folders,
            files: choice.files,
            unlock: choice.unlock ?? null,
            password: choice.unlock ? null : this.password() || null,
        }));
        // The password isn't kept: the job has it while it runs.
        this.password.set('');
        if (started) {
            this.store.clearSelection();
            this.sheet().close();
        }
    }
}

/** The archive's name without what makes it an archive (the server's `FileJobs.StemOf`): `trip.tar.gz` → `trip`. */
export function stemOf(name: string): string {
    const lower = name.toLowerCase();
    for (const suffix of ['.tar.gz', '.tar.bz2', '.tar.xz'])
        if (lower.endsWith(suffix) && name.length > suffix.length)
            return name.slice(0, -suffix.length);
    const dot = name.lastIndexOf('.');
    return dot > 0 ? name.slice(0, dot) : name;
}
