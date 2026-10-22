import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { joinParts } from '../../core/format';
import { ExclusionKind, FolderExclusions } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { SheetComponent } from '../../shared/sheet.component';
import { registerDialog } from '../dialogs.service';
import { ListingStore } from '../listing.store';

const KINDS: { kind: ExclusionKind; label: string; hint: string }[] = [
    {
        kind: 'scan', label: 'Skip in scans',
        hint: 'Scans of the folders above leave it out. What the index already has of it (files, tags) stays as it is, never refreshed: Remove from index forgets it.',
    },
    { kind: 'trickplay', label: 'No trickplay', hint: 'Its videos get no new timeline pictures; those already made are still shown.' },
    { kind: 'thumbnails', label: 'No thumbnails', hint: 'Its photos and videos get no new thumbnails; those already made are still shown.' },
];

const NONE: FolderExclusions = { scan: null, trickplay: null, thumbnails: null };

/**
 * "Scans and previews…" of a folder: keeps it, with its sub-folders, out of scans, trickplay or thumbnails, for the
 * whole space (`index.exclude`). A folder excluded through a folder above it shows that, checked and disabled.
 */
@Component({
    selector: 'sfe-folder-exclusions',
    imports: [BusyDirective, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Scans and previews" [subtitle]="name()" variant="folder-exclusions">
            <p class="field-hint">For everyone in this space, with its sub-folders. Excluded folders show a small note in the listing.</p>
            @for (entry of kinds; track entry.kind) {
                <label class="check">
                    <input type="checkbox" [attr.data-kind]="entry.kind" [checked]="checked(entry.kind)" [disabled]="inherited(entry.kind) || saving()"
                           (change)="set(entry.kind, checkedOf($event))">
                    <span>
                        <strong>{{ entry.label }}</strong>
                        <span class="field-hint">{{ inherited(entry.kind) ? 'Excluded through “' + state()[entry.kind] + '”: change it there.' : entry.hint }}</span>
                    </span>
                </label>
            }
            @if (error()) {
                <p class="form-error">{{ error() }}</p>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without changing anything (Esc)" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="button" class="button primary" title="Save for the whole space" [sfeBusy]="saving()"
                    [disabled]="saving() || !changed()" (click)="save()">Save</button>
        </sfe-sheet>
    `,
})
export class FolderExclusionsDialog {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly store = inject(ListingStore);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly kinds = KINDS;
    protected readonly path = signal('');
    /** What the folder is kept out of, as the listing said. */
    protected readonly state = signal<FolderExclusions>(NONE);
    /** The boxes ticked or unticked since opening. */
    private readonly edits = signal<Partial<Record<ExclusionKind, boolean>>>({});
    protected readonly saving = signal(false);
    protected readonly error = signal<string | null>(null);

    protected readonly name = computed(() => `“${this.path()}”`);
    protected readonly changed = computed(() => Object.entries(this.edits()).some(([kind, value]) => value !== this.listed(kind as ExclusionKind)));

    constructor() {
        registerDialog(dialogs => dialogs.folderExclusions, this);
    }

    /** Opens on a folder (not the library itself) with what it is kept out of now. */
    open(path: string, state: FolderExclusions | null | undefined): void {
        this.path.set(path);
        this.state.set(state ?? NONE);
        this.edits.set({});
        this.error.set(null);
        this.sheet().open();
    }

    /** Excluded through a folder above it: only that folder's own setting changes it. */
    protected inherited(kind: ExclusionKind): boolean {
        const from = this.state()[kind];
        return !!from && from !== this.path();
    }

    /** On the list itself. */
    private listed(kind: ExclusionKind): boolean {
        return this.state()[kind] === this.path();
    }

    protected checked(kind: ExclusionKind): boolean {
        return this.edits()[kind] ?? !!this.state()[kind];
    }

    protected set(kind: ExclusionKind, value: boolean): void {
        this.edits.update(edits => ({ ...edits, [kind]: value }));
    }

    protected async save(): Promise<void> {
        const changes = Object.fromEntries(Object.entries(this.edits()).filter(([kind, value]) => value !== this.listed(kind as ExclusionKind)));
        this.error.set(null);
        try {
            const state = await whileBusy(this.saving, () => this.api.setFolderExclusions(this.path(), changes));
            this.sheet().close();
            this.toasts.show(this.message(state));
            void this.store.refresh();
        } catch (error) {
            this.error.set(`Could not save: ${(error as Error).message}`);
        }
    }

    private message(state: FolderExclusions): string {
        const kept = joinParts([state.scan ? 'scans' : null, state.trickplay ? 'trickplay' : null, state.thumbnails ? 'thumbnails' : null], ', ');
        return kept ? `${this.name()} is kept out of ${kept}.` : `${this.name()} is no longer excluded.`;
    }

    protected checkedOf(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }
}
