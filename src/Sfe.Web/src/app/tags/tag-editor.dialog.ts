import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { TagDetails, TagInput, TagSummary } from '../core/models';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';
import { tagQuery, textOn } from './tags';

/** What closing the editor did: the tag saved, the id of the tag deleted, or null (cancelled). */
export type TagEditResult = TagDetails | { deleted: number } | null;

/** Creates or edits a tag: name, shorthand, aliases, parents, disambiguation, color, category and hidden. */
@Component({
    selector: 'sfe-tag-editor',
    imports: [IconComponent, SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet [heading]="tag() ? 'Edit tag' : 'New tag'" [subtitle]="subtitle()" (closed)="onClosed()">
            <form id="tag-editor-form" class="tag-form" novalidate (submit)="save($event)">
                <label class="field">
                    <span class="field-label">Name</span>
                    <input #nameInput type="text" name="name" required maxlength="200" autocomplete="off" [value]="name()" (input)="name.set(valueOf($event))">
                </label>
                <label class="field">
                    <span class="field-label">Shorthand</span>
                    <input type="text" name="shorthand" maxlength="200" autocomplete="off" [value]="shorthand()" (input)="shorthand.set(valueOf($event))">
                    <span class="field-hint">Searchable like the name, and used when this tag disambiguates another one.</span>
                </label>
                <label class="field">
                    <span class="field-label">Aliases</span>
                    <textarea name="aliases" rows="3" spellcheck="false" [value]="aliases()" (input)="aliases.set(valueOf($event))"></textarea>
                    <span class="field-hint">Other names this tag goes by, one per line.</span>
                </label>
                <div class="field">
                    <span class="field-label">Parent tags</span>
                    <div class="tag-list" [hidden]="!parents().length">
                        @for (parent of parents(); track parent.id) {
                            <sfe-tag-chip [tag]="parent" [removable]="true" (remove)="removeParent($event)" />
                        }
                    </div>
                    <sfe-tag-picker placeholder="Add a parent tag…" [exclude]="excludeFromParents" [pick]="addParent" />
                    <span class="field-hint">This tag “is a” parent tag: files with it are also found by searching for its parents.</span>
                </div>
                <label class="field">
                    <span class="field-label">Disambiguate with</span>
                    <select name="disambiguation" [disabled]="!parents().length" (change)="disambiguation.set(valueOf($event))">
                        <option value="" [selected]="!disambiguation()">None</option>
                        @for (parent of parents(); track parent.id) {
                            <option [value]="parent.id" [selected]="disambiguation() === '' + parent.id">{{ parent.displayName }}</option>
                        }
                    </select>
                    <span class="field-hint">Shows a parent after the name, e.g. “Freddy (FNAF)”.</span>
                </label>
                <div class="field">
                    <span class="field-label">Color</span>
                    <div class="swatches" role="radiogroup" aria-label="Color">
                        @for (swatch of swatches(); track swatch.slug) {
                            <label class="swatch" [title]="swatch.name" [style.--chip-bg]="swatch.hex" [style.--chip-text]="swatch.text">
                                <input type="radio" name="color" [value]="swatch.slug" [attr.aria-label]="swatch.name" [checked]="color() === swatch.slug" (change)="color.set(swatch.slug)">
                                @if (!swatch.slug) {
                                    <svg sfeIcon="close"></svg>
                                }
                            </label>
                        }
                    </div>
                </div>
                <label class="check">
                    <input type="checkbox" name="isCategory" [checked]="isCategory()" (change)="isCategory.set(checkedOf($event))">
                    <span><strong>Category</strong><span class="field-hint">Tags inheriting from this one are grouped under it.</span></span>
                </label>
                <label class="check">
                    <input type="checkbox" name="isHidden" [checked]="isHidden()" (change)="isHidden.set(checkedOf($event))">
                    <span><strong>Hidden</strong><span class="field-hint">Files with this tag are left out of search results unless the search asks for it.</span></span>
                </label>
                @if (error()) {
                    <p class="form-error" role="alert">{{ error() }}</p>
                }
            </form>
            @if (tag() && !tag()!.isBuiltIn) {
                <button sheetFooter type="button" class="button danger" title="Delete this tag and remove it from its files (asks first)" (click)="delete()">Delete</button>
            }
            @if (tag()) {
                <button sheetFooter type="button" class="button" title="Search for the files with this tag" (click)="find()"><svg sfeIcon="search"></svg>Find files</button>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without saving" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="submit" form="tag-editor-form" class="button primary" title="Save the tag" [disabled]="saving()">Save</button>
        </sfe-sheet>
    `,
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
    protected readonly subtitle = computed(() => {
        const tag = this.tag();
        return tag ? [tag.isBuiltIn ? 'Built-in tag' : null, plural(tag.fileCount, 'file')].filter(Boolean).join(' · ') : null;
    });
    protected readonly swatches = computed(() => [
        { slug: '', name: 'None', hex: null as string | null, text: null as string | null },
        ...(this.config.config()?.tagColors ?? []).map(color => ({ slug: color.slug, name: color.name, hex: color.hex as string | null, text: textOn(color.hex) as string | null })),
    ]);
    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    private result: TagEditResult = null;
    private resolveOpen: ((result: TagEditResult) => void) | null = null;

    protected readonly excludeFromParents = () => new Set([...(this.tag() ? [this.tag()!.id] : []), ...this.parents().map(p => p.id)]);
    protected readonly addParent = async (option: TagOption) => {
        if (option.tag)
            this.parents.update(parents => [...parents, option.tag!]);
    };

    constructor() {
        registerDialog(dialogs => dialogs.tagEditor, this);
    }

    /** Opens the editor for a tag id, or for a new tag; resolves when it closes, with what it did. */
    async open(id: number | null = null, defaults: { name?: string } = {}): Promise<TagEditResult> {
        this.result = null;
        let details: TagDetails | null = null;
        if (id !== null) {
            try {
                details = await this.api.tag(id);
            } catch (failure) {
                this.toasts.error(`Could not load the tag: ${(failure as Error).message}`);
                return null;
            }
        }
        this.fill(details, defaults);
        this.sheet().open();
        // After the fields are drawn with the tag's values.
        setTimeout(() => this.nameInput().nativeElement.focus());
        return new Promise(resolve => { this.resolveOpen = resolve; });
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
        this.parents.update(parents => parents.filter(p => p.id !== parent.id));
        if (this.disambiguation() === String(parent.id))
            this.disambiguation.set('');
    }

    protected async save(event: Event): Promise<void> {
        event.preventDefault();
        const tag = this.tag();
        const input: TagInput = {
            name: this.name().trim(),
            shorthand: this.shorthand().trim() || null,
            aliases: this.aliases().split('\n').map(a => a.trim()).filter(Boolean),
            parentIds: this.parents().map(p => p.id),
            disambiguationId: this.disambiguation() ? Number(this.disambiguation()) : null,
            color: this.color() || null,
            isCategory: this.isCategory(),
            isHidden: this.isHidden(),
        };
        if (!input.name) {
            this.error.set('A tag needs a name.');
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
        const uses = tag.fileCount ? ` It is removed from ${plural(tag.fileCount, 'file')}.` : '';
        if (!confirm(`Delete the tag “${tag.displayName}”?${uses}`))
            return;
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
        this.dialogs.closeAll();
        this.store.navigate({ q: tagQuery(tag) });
    }

    protected onClosed(): void {
        this.resolveOpen?.(this.result);
        this.resolveOpen = null;
    }
}
