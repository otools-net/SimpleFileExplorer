import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { LibraryItem } from '../core/models';
import { FileTags, TagSummary } from '../core/tag.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';

/**
 * The tags of a file, grouped by category: add (or create), remove, or edit them, each with its right (only seen
 * without them).
 */
@Component({
    selector: 'sfe-file-tags',
    imports: [SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './file-tags.dialog.html',
})
export class FileTagsDialog {
    private readonly api = inject(Api);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    protected readonly dialogs = inject(Dialogs);
    private readonly config = inject(ConfigService);
    protected readonly canAssign = computed(() => this.config.rights().has(Rights.assignTags));
    protected readonly canManage = computed(() => this.config.rights().has(Rights.manageTags));
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly picker = viewChild.required<TagPickerComponent>('picker');

    private path = '';
    /** The connection the file is in (`config.place()` when opened); null for the library. */
    private connection: number | null = null;
    protected readonly name = signal('');
    protected readonly editTitle = $localize`Edit this tag.`;
    protected readonly tagsHeading = $localize`:heading of the tags without a category:Tags`;
    /** Null while loading. */
    protected readonly current = signal<FileTags | null>(null);

    protected readonly excluded = () => new Set(this.current()?.tags.map((tag) => tag.id));
    protected readonly add = async (option: TagOption) => {
        const tag = option.create ? await this.api.createTag({ name: option.create }) : option.tag!;
        if (option.create) this.toasts.show(`Tag “${tag.name}” created.`);
        await this.change({ add: [tag.id] });
    };

    constructor() {
        registerDialog((dialogs) => dialogs.fileTags, this);
    }

    async open(item: LibraryItem): Promise<void> {
        this.path = item.path;
        this.connection = this.config.place()?.id ?? null;
        this.name.set(item.name);
        this.current.set(null);
        this.sheet().open();
        if (this.canAssign()) this.picker().focus();
        await this.reload();
    }

    protected remove(tag: TagSummary): void {
        this.change({ remove: [tag.id] }).catch((error: Error) => this.toasts.error(error.message));
    }

    protected async edit(tag: TagSummary): Promise<void> {
        await this.dialogs.tagEditor()?.open(tag.id);
        if (this.sheet().isOpen()) await this.reload();
    }

    private async change(delta: { add?: number[]; remove?: number[] }): Promise<void> {
        const result = await this.api.changeFileTags({ paths: [this.path], ...delta }, this.connection);
        this.current.set(result.files[0]);
        this.store.applyFileTags(result.files);
    }

    private async reload(): Promise<void> {
        try {
            this.current.set(await this.api.fileTags(this.path, this.connection));
        } catch (error) {
            this.toasts.error(`Could not load the tags: ${(error as Error).message}`);
        }
    }
}
