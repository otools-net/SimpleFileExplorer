import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { baseName, parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf, valueOf } from '../shared/events';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileTransfers } from './file-transfers';
import { folderBrowser, FolderBrowserComponent } from './folder-browser.component';

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
    /** The folder to start in (else the archive's), and whether into a new folder (else yes): an extraction asked again. */
    target?: string;
    newFolder?: boolean;
}

/**
 * Extract…: the folder picked by clicking through the library (starting with the archive's), into a new folder named
 * after the archive or not, with the archive's password when it has one; then the job runs in the background, in the
 * operations panel (agents/docs/features/archive-extraction.md).
 */
@Component({
    selector: 'sfe-extract-dialog',
    imports: [BusyDirective, FolderBrowserComponent, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Extract" variant="transfer-dialog">
            <p>{{ what() }} into:</p>
            <sfe-folder-browser [browser]="browser" />
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
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly transfers = inject(FileTransfers);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly choice = signal<ExtractChoice | null>(null);
    /** The folder chosen. */
    protected readonly browser = folderBrowser();
    private readonly target = this.browser.target;
    protected readonly busy = signal(false);
    protected readonly newFolder = signal(true);
    protected readonly password = signal('');
    protected readonly showPassword = signal(false);

    protected readonly stem = computed(() => stemOf(this.choice()?.name ?? ''));
    protected readonly what = computed(() => {
        const choice = this.choice();
        return choice?.what ? `Extract ${choice.what} of “${choice.name}”` : `Extract “${choice?.name ?? ''}”`;
    });
    /** Why the folder chosen can't be used; null when it can. */
    protected readonly refusal = computed(() => {
        if (!this.browser.listing())
            return 'Pick a folder.';
        return this.browser.allows(Rights.extractFiles) ? null : 'You may not extract archives into this folder: open another.';
    });
    protected readonly targetName = computed(() => baseName(this.target()) || this.config.config()?.rootName || 'the library');

    constructor() {
        registerDialog(dialogs => dialogs.extract, this);
    }

    /** Extract…: the folder is picked, starting with the archive's. */
    open(choice: ExtractChoice): void {
        this.choice.set(choice);
        this.newFolder.set(choice.newFolder ?? true);
        this.password.set('');
        this.showPassword.set(false);
        this.browser.reset();
        void this.browser.browse(choice.target ?? parentPath(choice.path));
        this.sheet().open();
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
