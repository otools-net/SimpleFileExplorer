import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { TagDetails, TagSummary } from '../core/tag.models';
import { TagInput } from './tag.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';
import { libraryFiles, removedFrom, tagQuery, textOn } from './tags';

/** What closing the editor did: the tag saved, the id of the tag deleted, or null (cancelled). */
export type TagEditResult = TagDetails | { deleted: number } | null;

/** Creates or edits a tag: name, shorthand, aliases, parents, disambiguation, color, category and hidden. */
@Component({
    selector: 'sfe-tag-editor',
    imports: [IconComponent, SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './tag-editor.dialog.html',
})
export class TagEditorDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly dialogs = inject(Dialogs);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly nameInput = viewChild.required<ElementRef<HTMLInputElement>>('nameInput');

    /** The tag being edited, null for a new tag. */
    protected readonly tag = signal<TagDetails | null>(null);
    protected readonly name = signal('');
    protected readonly shorthand = signal('');
    protected readonly aliases = signal('');
    protected readonly parents = signal<TagSummary[]>([]);
    /** The id of the parent shown after the name, as text ('' = none). */
    protected readonly disambiguation = signal('');
    protected readonly color = signal('');
    protected readonly isCategory = signal(false);
    protected readonly isHidden = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly saving = signal(false);
    protected readonly heading = computed(() => (this.tag() ? $localize`Edit tag` : $localize`New tag`));
    protected readonly subtitle = computed(() => {
        const tag = this.tag();
        return tag
            ? [tag.isBuiltIn ? $localize`Built-in tag` : null, libraryFiles(tag.fileCount)].filter(Boolean).join(' · ')
            : null;
    });
    protected readonly swatches = computed(() => [
        { slug: '', name: $localize`:no colour:None`, hex: null as string | null, text: null as string | null },
        ...(this.config.config()?.tagColors ?? []).map((color) => ({
            slug: color.slug,
            name: color.name,
            hex: color.hex as string | null,
            text: textOn(color.hex) as string | null,
        })),
    ]);
    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    private result: TagEditResult = null;
    private resolveOpen: ((result: TagEditResult) => void) | null = null;

    protected readonly excludeFromParents = () =>
        new Set([...(this.tag() ? [this.tag()!.id] : []), ...this.parents().map((p) => p.id)]);
    protected readonly addParent = async (option: TagOption) => {
        if (option.tag) this.parents.update((parents) => [...parents, option.tag!]);
    };

    constructor() {
        registerDialog((dialogs) => dialogs.tagEditor, this);
    }

    /** Opens the editor for a tag id, or for a new tag; resolves when it closes, with what it did. */
    async open(id: number | null = null, defaults: { name?: string } = {}): Promise<TagEditResult> {
        this.result = null;
        let details: TagDetails | null = null;
        if (id !== null) {
            try {
                details = await this.api.tag(id);
            } catch (failure) {
                this.toasts.error($localize`Could not load the tag: ${(failure as Error).message}:reason:`);
                return null;
            }
        }
        this.fill(details, defaults);
        this.sheet().open();
        // After the fields are drawn with the tag's values.
        setTimeout(() => this.nameInput().nativeElement.focus());
        return new Promise((resolve) => {
            this.resolveOpen = resolve;
        });
    }

    private fill(details: TagDetails | null, defaults: { name?: string }): void {
        this.tag.set(details);
        this.parents.set(details ? [...details.parents] : []);
        this.name.set(details?.name ?? defaults.name ?? '');
        this.shorthand.set(details?.shorthand ?? '');
        this.aliases.set(details?.aliases.join('\n') ?? '');
        this.disambiguation.set(details?.disambiguationId != null ? String(details.disambiguationId) : '');
        this.color.set(details?.color ?? '');
        this.isCategory.set(details?.isCategory ?? false);
        this.isHidden.set(details?.isHidden ?? false);
        this.error.set(null);
    }

    protected removeParent(parent: TagSummary): void {
        this.parents.update((parents) => parents.filter((p) => p.id !== parent.id));
        if (this.disambiguation() === String(parent.id)) this.disambiguation.set('');
    }

    protected async save(event: Event): Promise<void> {
        event.preventDefault();
        const tag = this.tag();
        const input: TagInput = {
            name: this.name().trim(),
            shorthand: this.shorthand().trim() || null,
            aliases: this.aliases()
                .split('\n')
                .map((a) => a.trim())
                .filter(Boolean),
            parentIds: this.parents().map((p) => p.id),
            disambiguationId: this.disambiguation() ? Number(this.disambiguation()) : null,
            color: this.color() || null,
            isCategory: this.isCategory(),
            isHidden: this.isHidden(),
        };
        if (!input.name) {
            this.error.set($localize`A tag needs a name.`);
            this.nameInput().nativeElement.focus();
            return;
        }
        this.saving.set(true);
        try {
            const saved = tag ? await this.api.updateTag(tag.id, input) : await this.api.createTag(input);
            this.result = saved;
            this.store.applyTagChanged(saved);
            this.sheet().close();
        } catch (failure) {
            this.error.set((failure as Error).message);
        } finally {
            this.saving.set(false);
        }
    }

    protected async delete(): Promise<void> {
        const tag = this.tag()!;
        const uses = removedFrom(tag.fileCount, this.config.can(Rights.useConnections));
        const question = uses
            ? $localize`Delete the tag “${tag.displayName}:tag:”? ${uses}:uses: This cannot be undone.`
            : $localize`Delete the tag “${tag.displayName}:tag:”? This cannot be undone.`;
        if (!confirm(question)) return;
        try {
            await this.api.deleteTag(tag.id);
            this.store.applyTagDeleted(tag.id);
            this.result = { deleted: tag.id };
            this.sheet().close();
        } catch (failure) {
            this.error.set((failure as Error).message);
        }
    }

    protected find(): void {
        const tag = this.tag()!;
        if (!this.dialogs.closeAll()) return;
        this.store.navigate({ q: tagQuery(tag) });
    }

    protected onClosed(): void {
        this.resolveOpen?.(this.result);
        this.resolveOpen = null;
    }
}
