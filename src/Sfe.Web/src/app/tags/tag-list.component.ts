import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { plural } from '../core/format';
import { TagDetails } from '../core/models';
import { Dialogs } from '../library/dialogs.service';
import { IconComponent } from '../shared/icon.component';
import { TagChipComponent } from './tag-chip.component';

/**
 * All the tags, found by name, shorthand or alias; a click edits one, and new ones are created from here. Shown in
 * the Tags section of the administration page and in the tag manager dialog; `load()` fills it.
 */
@Component({
    selector: 'sfe-tag-list',
    imports: [IconComponent, NgTemplateOutlet, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tag-manager' },
    template: `
        <div class="tag-manager-bar">
            <div class="tag-input-wrap">
                <svg sfeIcon="search"></svg>
                <input #search type="search" class="tag-input" placeholder="Find a tag by name, shorthand or alias…" autocomplete="off" aria-label="Find a tag" (input)="onInput()">
            </div>
            <button type="button" class="button primary" title="Create a tag (named after the search, if any)" (click)="create()"><svg sfeIcon="plus"></svg>New tag</button>
        </div>
        <div class="tag-table">
            @if (error(); as error) {
                <p class="muted">Could not load tags: {{ error }}</p>
            } @else if (results(); as results) {
                @for (tag of results.matches; track tag.id) {
                    <ng-container *ngTemplateOutlet="row; context: { $implicit: tag }" />
                }
                @if (results.descendants.length) {
                    <h3 class="tag-table-heading">Inheriting from these</h3>
                    @for (tag of results.descendants; track tag.id) {
                        <ng-container *ngTemplateOutlet="row; context: { $implicit: tag }" />
                    }
                }
                @if (!results.matches.length && !results.descendants.length) {
                    <p class="muted">No tag matches.</p>
                }
            }
        </div>
        <ng-template #row let-tag>
            <button type="button" class="tag-row" [title]="'Edit ' + tag.displayName" (click)="edit(tag)">
                <sfe-tag-chip [tag]="tag" />
                <span class="tag-row-detail">{{ details(tag) }}</span>
                <span class="tag-row-count">{{ files(tag) }}</span>
            </button>
        </ng-template>
    `,
})
export class TagListComponent {
    private readonly api = inject(Api);
    private readonly dialogs = inject(Dialogs);
    private readonly search = viewChild.required<ElementRef<HTMLInputElement>>('search');

    protected readonly results = signal<{ matches: TagDetails[]; descendants: TagDetails[] } | null>(null);
    protected readonly error = signal<string | null>(null);
    private timer = 0;
    private sequence = 0;

    constructor() {
        inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    }

    focus(): void {
        this.search().nativeElement.focus();
    }

    async load(): Promise<void> {
        const request = ++this.sequence;
        try {
            const results = await this.api.tags({ q: this.search().nativeElement.value.trim(), limit: 1000 });
            if (request !== this.sequence)
                return;
            this.results.set(results);
            this.error.set(null);
        } catch (error) {
            if (request === this.sequence)
                this.error.set((error as Error).message);
        }
    }

    protected onInput(): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => this.load(), 150);
    }

    protected details(tag: TagDetails): string {
        return [
            tag.shorthand ? `Shorthand: ${tag.shorthand}` : null,
            tag.aliases.length ? `Aliases: ${tag.aliases.join(', ')}` : null,
            tag.parents.length ? `Parents: ${tag.parents.map(p => p.displayName).join(', ')}` : null,
            [tag.isCategory ? 'category' : null, tag.isHidden ? 'hidden' : null].filter(Boolean).join(', ') || null,
        ].filter(Boolean).join(' · ');
    }

    protected files(tag: TagDetails): string {
        return plural(tag.fileCount, 'file');
    }

    protected async edit(tag: TagDetails): Promise<void> {
        if (await this.dialogs.tagEditor()?.open(tag.id))
            void this.load();
    }

    protected async create(): Promise<void> {
        if (await this.dialogs.tagEditor()?.open(null, { name: this.search().nativeElement.value.trim() }))
            void this.load();
    }
}
