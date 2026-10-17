import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { FileOperationResult, isFileConflict, LibraryItem } from '../core/models';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { QuickLinksService } from '../library/quick-links/quick-links.service';

type Mode = { kind: 'folder'; parent: string } | { kind: 'rename'; item: LibraryItem };

/**
 * Asks for a name: of a new folder in the folder listed (New folder, Ctrl+Shift+N), or the new name of a file or
 * folder (Rename, F2), with the name selected up to its extension as in Explorer. The server checks the name; its
 * answer shows under the field. Done, the listing takes the item in place, without loading again.
 */
@Component({
    selector: 'sfe-name-dialog',
    imports: [BusyDirective, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet [heading]="heading()" [subtitle]="subtitle()" variant="name-dialog">
            <form id="name-dialog-form" novalidate (submit)="submit($event)">
                <label class="field">
                    <span class="field-label">Name</span>
                    <input #input type="text" name="name" maxlength="255" autocomplete="off" spellcheck="false"
                           [value]="name()" (input)="changed($event)">
                    @if (hidden()) {
                        <span class="field-hint">A name starting with a dot is a hidden item: listings show it only where hidden items are shown.</span>
                    }
                </label>
                @if (error()) {
                    <p class="form-error" role="alert">{{ error() }}</p>
                }
            </form>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without changing anything (Esc)" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="submit" form="name-dialog-form" class="button primary" [title]="actionTitle()"
                    [sfeBusy]="saving()" [disabled]="saving() || !canSave()">{{ actionLabel() }}</button>
        </sfe-sheet>
    `,
})
export class NameDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly quickLinks = inject(QuickLinksService);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

    protected readonly mode = signal<Mode | null>(null);
    protected readonly name = signal('');
    protected readonly error = signal<string | null>(null);
    protected readonly saving = signal(false);

    protected readonly heading = computed(() => this.mode()?.kind === 'rename' ? 'Rename' : 'New folder');
    protected readonly subtitle = computed(() => {
        const mode = this.mode();
        if (!mode)
            return null;
        return mode.kind === 'rename' ? mode.item.name : `In ${mode.parent ? `“${mode.parent}”` : this.config.config()?.rootName ?? 'the library'}`;
    });
    protected readonly actionLabel = computed(() => this.mode()?.kind === 'rename' ? 'Rename' : 'Create');
    protected readonly actionTitle = computed(() => this.mode()?.kind === 'rename' ? 'Give it this name (Enter)' : 'Create the folder (Enter)');
    protected readonly hidden = computed(() => this.name().trim().startsWith('.'));
    protected readonly canSave = computed(() => {
        const name = this.name();
        const mode = this.mode();
        return name.trim().length > 0 && !(mode?.kind === 'rename' && name === mode.item.name);
    });

    constructor() {
        registerDialog(dialogs => dialogs.name, this);
    }

    /** Asks for the name of a new folder in `parent` (the folder listed by default). */
    newFolder(parent = this.store.route().path): void {
        this.show({ kind: 'folder', parent }, 'New folder');
    }

    /** Asks for the new name of `item`. */
    rename(item: LibraryItem): void {
        this.show({ kind: 'rename', item }, item.name);
    }

    private show(mode: Mode, name: string): void {
        this.mode.set(mode);
        this.name.set(name);
        this.error.set(null);
        this.sheet().open();
        // The name selected up to its extension (a file's), so that typing replaces the name and keeps the type.
        const input = this.input().nativeElement;
        input.value = name;
        const dot = mode.kind === 'rename' && mode.item.kind !== 'directory' ? name.lastIndexOf('.') : -1;
        input.focus();
        input.setSelectionRange(0, dot > 0 ? dot : name.length);
    }

    protected changed(event: Event): void {
        this.name.set((event.target as HTMLInputElement).value);
        this.error.set(null);
    }

    protected async submit(event: Event): Promise<void> {
        event.preventDefault();
        const mode = this.mode();
        if (!mode || !this.canSave() || this.saving())
            return;
        const name = this.name();
        let result: FileOperationResult;
        try {
            result = await whileBusy(this.saving, () => mode.kind === 'rename'
                ? this.api.renameItem(mode.item.path, name)
                : this.api.createFolder(mode.parent, name));
        } catch (error) {
            this.error.set(error instanceof ApiError && error.status === 404
                ? 'It is not there any more: it may have been moved or deleted.'
                : (error as Error).message);
            return;
        }
        if (isFileConflict(result)) {
            this.error.set(result.detail ?? 'It could not be done.');
            return;
        }
        this.sheet().close();
        if (mode.kind === 'rename') {
            this.store.applyRenamed(mode.item.path, result);
            // The server moved the quick links to it (and below it) too.
            const old = mode.item.path;
            if (this.quickLinks.links().some(link => link.path === old || link.path.startsWith(`${old}/`)))
                void this.quickLinks.load();
        } else
            this.store.applyAdded(result);
    }
}
