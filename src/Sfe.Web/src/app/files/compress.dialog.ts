import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { LibraryItem } from '../core/models';
import { baseName } from '../core/paths';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf, valueOf } from '../shared/events';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileTransfers } from './file-transfers';

/** The levels offered (the server takes 0 to 9). */
export const COMPRESSION_LEVELS: readonly { level: number; label: string; hint: string }[] = [
    { level: 0, label: 'Store', hint: 'no compression: the fastest, the largest' },
    { level: 1, label: 'Fastest', hint: 'quick, a little larger' },
    { level: 6, label: 'Normal', hint: 'the usual balance' },
    { level: 9, label: 'Maximum', hint: 'the smallest, several times slower' },
];

/**
 * Compress…: the zip's name, how hard to compress (Store, Fastest, Normal, Maximum; files already compressed stored),
 * a password (AES-256: the names stay readable); the zip is made in the folder listed, in the background
 * (agents/docs/features/compression.md).
 */
@Component({
    selector: 'sfe-compress-dialog',
    imports: [BusyDirective, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Compress" variant="transfer-dialog">
            <p>{{ what() }} into a zip in “{{ targetName() }}”.</p>
            <label class="field">
                <span>Name</span>
                <input type="text" data-zip-name [value]="name()" (input)="name.set(value($event))" autocomplete="off" spellcheck="false">
            </label>
            <label class="field">
                <span>Compression</span>
                <select data-level (change)="level.set(+value($event))">
                    @for (choice of levels; track choice.level) {
                        <option [value]="choice.level" [selected]="choice.level === level()">{{ choice.label }}: {{ choice.hint }}</option>
                    }
                </select>
            </label>
            <label class="check">
                <input type="checkbox" data-skip [checked]="skipCompressed()" (change)="skipCompressed.set(checked($event))">
                <span>Skip files already compressed<span class="field-hint">Pictures, videos, music and archives are stored as they are: compressing them again gains almost nothing.</span></span>
            </label>
            <label class="field">
                <span>Password <span class="field-hint">optional</span></span>
                <input [type]="showPassword() ? 'text' : 'password'" data-zip-password autocomplete="new-password" [value]="password()" (input)="password.set(value($event))">
            </label>
            @if (password()) {
                <label class="field">
                    <span>Password again</span>
                    <input [type]="showPassword() ? 'text' : 'password'" data-zip-confirm autocomplete="new-password" [value]="confirmation()" (input)="confirmation.set(value($event))">
                </label>
                <label class="check">
                    <input type="checkbox" [checked]="showPassword()" (change)="showPassword.set(checked($event))">
                    <span>Show the passwords</span>
                </label>
                <ul class="field-hint compress-notes" data-password-notes>
                    <li>File names stay visible: only the content is encrypted (AES-256).</li>
                    @if (password().length < 8) {
                        <li>A short password is easy to guess: 12 characters or more is better.</li>
                    }
                    @if (!ascii()) {
                        <li>Letters other than A–Z, digits and common signs may not work in some programs (7-Zip refuses them).</li>
                    }
                    @if (plainHttp) {
                        <li>This connection isn't encrypted: the password crosses the network as it is.</li>
                    }
                    <li>Windows' own zip folders may not open it: 7-Zip, WinRAR or a recent Windows 11 do.</li>
                </ul>
            }
            @if (problem(); as problem) {
                <p class="field-hint" role="alert" data-compress-problem>{{ problem }}</p>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without making a zip (Esc)" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="button" class="button primary" data-compress [sfeBusy]="busy()" [disabled]="busy() || problem() !== null"
                    [title]="'Make the zip in “' + targetName() + '”: it goes on in the background'" (click)="confirm()">Compress</button>
        </sfe-sheet>
    `,
})
export class CompressDialog {
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly transfers = inject(FileTransfers);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly levels = COMPRESSION_LEVELS;
    private readonly items = signal<LibraryItem[]>([]);
    protected readonly target = signal('');
    protected readonly name = signal('');
    protected readonly level = signal(6);
    protected readonly skipCompressed = signal(true);
    protected readonly password = signal('');
    protected readonly confirmation = signal('');
    protected readonly showPassword = signal(false);
    protected readonly busy = signal(false);
    /** The page came over plain HTTP from another computer: what is typed crosses the network as is. */
    protected readonly plainHttp = location.protocol === 'http:' && !['localhost', '127.0.0.1', '[::1]'].includes(location.hostname);

    protected readonly targetName = computed(() => baseName(this.target()) || this.config.config()?.rootName || 'the library');
    protected readonly what = computed(() => {
        const items = this.items();
        return items.length === 1 ? `Put “${items[0].name}”` : `Put ${plural(items.length, 'item')}`;
    });
    protected readonly ascii = computed(() => [...this.password()].every(c => c >= ' ' && c <= '~'));
    /** Why it can't start yet; null when it can. */
    protected readonly problem = computed(() => {
        const name = this.name().trim();
        if (!name)
            return 'Give the zip a name.';
        if (/[\\/:*?"<>|]/.test(name))
            return 'A name can\'t hold \\ / : * ? " < > |.';
        if (this.password() && this.password() !== this.confirmation())
            return 'The two passwords differ.';
        return null;
    });

    constructor() {
        registerDialog(dialogs => dialogs.compress, this);
    }

    /** Compress… of `items`: the zip goes into the folder listed (in search results, the first item's). */
    open(items: LibraryItem[]): void {
        if (!items.length)
            return;
        this.items.set(items);
        this.target.set(this.transfers.compressTarget(items));
        this.name.set(defaultName(items, this.target(), this.config.config()?.rootName ?? 'Library'));
        this.level.set(6);
        this.skipCompressed.set(true);
        this.password.set('');
        this.confirmation.set('');
        this.showPassword.set(false);
        this.sheet().open();
    }

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    protected value(event: Event): string {
        return valueOf(event);
    }

    protected async confirm(): Promise<void> {
        if (this.problem())
            return;
        const started = await whileBusy(this.busy, () => this.transfers.compress({
            paths: this.items().map(item => item.path),
            target: this.target(),
            name: this.name().trim(),
            level: this.level(),
            skipCompressed: this.skipCompressed(),
            password: this.password() || null,
        }));
        // The password isn't kept: the job has it while it runs.
        this.password.set('');
        this.confirmation.set('');
        if (started) {
            this.store.clearSelection();
            this.sheet().close();
        }
    }
}

/** The zip's name before it is changed: its only item's (a file without its extension), else the folder's. */
export function defaultName(items: LibraryItem[], target: string, rootName: string): string {
    if (items.length === 1) {
        const name = items[0].name;
        const dot = name.lastIndexOf('.');
        return items[0].kind === 'directory' || dot <= 0 ? name : name.slice(0, dot);
    }
    return baseName(target) || rootName;
}
