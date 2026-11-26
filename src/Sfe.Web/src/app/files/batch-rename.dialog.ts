import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { PlaceFiles } from '../core/place-files';
import { isFileConflict, LibraryItem } from '../core/models';
import { ToastService } from '../core/toast.service';
import { UndoHistory } from '../core/undo-history';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { BusyDirective } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { leftOut, namesNotBack, renamedToast, selectedCount, undoRenameHint } from './file-dialog-texts';

export type BatchRenameMode = 'replace' | 'number';

export interface BatchRenameRule {
    mode: BatchRenameMode;
    /** Find and replace: the text looked for, and what takes its place. */
    find: string;
    replace: string;
    matchCase: boolean;
    /** Numbered: the new name, `#` standing for the number (as many # as digits; none: added at the end). */
    pattern: string;
    start: number;
}

/** One line of the preview: the item, its new name, and why it can't be given (null when it can). */
export interface RenamePlan {
    item: LibraryItem;
    name: string;
    problem: string | null;
}

/** Characters no name may hold here (Windows refuses the others; the server says which). */
const FORBIDDEN = /[\\/]/;

/** A file's name without its extension, and its extension (with the dot); folders have none. */
function split(item: LibraryItem): [string, string] {
    const dot = item.kind === 'directory' ? -1 : item.name.lastIndexOf('.');
    return dot > 0 ? [item.name.slice(0, dot), item.name.slice(dot)] : [item.name, ''];
}

function escapeRegExp(text: string): string {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * The new names of `items` by `rule` (extensions kept), and what keeps each from being given: an empty or forbidden
 * name, two of them alike, or a name already in the folder (`taken`: the names listed there besides those renamed).
 */
export function planRenames(items: LibraryItem[], rule: BatchRenameRule, taken: Iterable<string>): RenamePlan[] {
    const names = items.map((item, index) => {
        const [stem, extension] = split(item);
        if (rule.mode === 'replace') {
            if (!rule.find) return item.name;
            return (
                stem.replace(new RegExp(escapeRegExp(rule.find), rule.matchCase ? 'g' : 'gi'), rule.replace) + extension
            );
        }
        const number = rule.start + index;
        const hashes = /#+/.exec(rule.pattern);
        const digits = String(number).padStart(hashes?.[0].length ?? 1, '0');
        const base = hashes ? rule.pattern.replace(/#+/, digits) : `${rule.pattern} ${digits}`;
        return base.trim() + extension;
    });
    const key = (name: string) => name.toLowerCase();
    const others = new Set([...taken].map(key));
    const counts = new Map<string, number>();
    for (const name of names) counts.set(key(name), (counts.get(key(name)) ?? 0) + 1);
    const current = new Set(items.map((item) => key(item.name)));
    return items.map((item, index) => {
        const name = names[index];
        let problem: string | null = null;
        // Only the extension left (".jpg") is no name either.
        if (!name.trim() || name.trim() === '.' || name.trim() === '..' || (split(item)[1] && name === split(item)[1]))
            problem = $localize`No name would be left.`;
        else if (FORBIDDEN.test(name)) problem = $localize`A name can't hold / or \\.`;
        else if (name !== item.name && (counts.get(key(name)) ?? 0) > 1)
            problem = $localize`Two items would get this name.`;
        else if (name !== item.name && others.has(key(name))) problem = $localize`This name is taken in this folder.`;
        else if (name !== item.name && key(name) !== key(item.name) && current.has(key(name)))
            problem = $localize`Another item renamed has this name now. Rename in two steps.`;
        return { item, name, problem };
    });
}

/**
 * Renames several files and folders of one folder at once: find and replace in their names, or numbered names, with a
 * preview of every new name before anything changes. Names taken, invalid or alike are shown and left out; the server
 * checks each again. Done, a toast offers to undo all of it.
 */
@Component({
    selector: 'sfe-batch-rename-dialog',
    imports: [BusyDirective, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './batch-rename.dialog.html',
})
export class BatchRenameDialog {
    private readonly files = inject(PlaceFiles);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly undos = inject(UndoHistory);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly items = signal<LibraryItem[]>([]);
    protected readonly mode = signal<BatchRenameMode>('replace');
    protected readonly find = signal('');
    protected readonly replace = signal('');
    protected readonly matchCase = signal(false);
    protected readonly pattern = signal('');
    protected readonly start = signal(1);
    protected readonly saving = signal(false);
    protected readonly failures = signal<string[]>([]);

    protected readonly subtitle = computed(() => selectedCount(this.items().length));
    protected readonly plans = computed(() => {
        const items = this.items();
        const renamed = new Set(items.map((item) => item.path));
        const taken = this.store
            .items()
            .filter((item) => !renamed.has(item.path))
            .map((item) => item.name);
        return planRenames(
            items,
            {
                mode: this.mode(),
                find: this.find(),
                replace: this.replace(),
                matchCase: this.matchCase(),
                pattern: this.pattern(),
                start: this.start(),
            },
            taken,
        );
    });
    /** What will be renamed. */
    protected readonly changes = computed(() =>
        this.plans().filter((line) => !line.problem && line.name !== line.item.name),
    );
    protected readonly summary = computed(() => leftOut(this.plans().filter((line) => line.problem).length));
    protected readonly applyLabel = computed(() => {
        const count = this.changes().length;
        return count ? $localize`Rename ${count}:count:` : $localize`:button:Rename`;
    });

    constructor() {
        registerDialog((dialogs) => dialogs.batchRename, this);
    }

    open(items: LibraryItem[]): void {
        this.items.set(items);
        this.failures.set([]);
        this.find.set('');
        this.replace.set('');
        // Numbered: the folder's name as a start ("Holidays 1", "Holidays 2"…).
        const listing = this.store.listing();
        this.pattern.set((listing && 'name' in listing ? listing.name : '') || 'Item');
        this.sheet().open();
    }

    protected value(event: Event): string {
        return (event.target as HTMLInputElement).value;
    }

    protected checked(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }

    protected number(event: Event): number {
        const value = Number((event.target as HTMLInputElement).value);
        return Number.isFinite(value) && value >= 0 ? Math.floor(value) : 0;
    }

    /** Renames them one after the other; what the server refuses is said, the rest stays done (and can be undone). */
    protected async apply(): Promise<void> {
        const changes = this.changes();
        if (!changes.length || this.saving()) return;
        this.saving.set(true);
        this.failures.set([]);
        const done: { from: LibraryItem; to: LibraryItem }[] = [];
        const failures: string[] = [];
        for (const line of changes) {
            try {
                const result = await this.files.rename(line.item.path, line.name);
                if (isFileConflict(result)) {
                    const reason = result.detail ?? $localize`It was not renamed.`;
                    failures.push($localize`“${line.item.name}:name:”: ${reason}:reason:`);
                    continue;
                }
                this.store.applyRenamed(line.item.path, result);
                done.push({ from: line.item, to: result });
            } catch (error) {
                failures.push($localize`“${line.item.name}:name:”: ${(error as Error).message}:reason:`);
            }
        }
        this.saving.set(false);
        if (done.length) {
            const text = renamedToast(done.length);
            this.toasts.success(text, {
                timeout: 10000,
                action: this.undos.add({
                    done: text,
                    hint: undoRenameHint(done.length),
                    run: () => this.undo(done),
                    place: this.undos.here(),
                }),
            });
        }
        if (failures.length) {
            // Those left are shown again, under their new names.
            this.items.update((items) => items.map((item) => done.find((d) => d.from.path === item.path)?.to ?? item));
            this.failures.set(failures);
        } else {
            this.sheet().close();
        }
    }

    /** Undo: the old names back, last renamed first. */
    private async undo(done: { from: LibraryItem; to: LibraryItem }[]): Promise<void> {
        let failed = 0;
        for (const { from, to } of [...done].reverse()) {
            try {
                const result = await this.files.rename(to.path, from.name);
                if (isFileConflict(result)) failed++;
                else this.store.applyRenamed(to.path, result);
            } catch {
                failed++;
            }
        }
        if (failed) this.toasts.error(namesNotBack(failed));
    }
}
