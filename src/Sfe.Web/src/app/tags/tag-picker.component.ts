import { ChangeDetectionStrategy, Component, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { TagDetails } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { TagChipComponent } from './tag-chip.component';
import { describeTag } from './tags';

/** A suggestion: an existing tag, or a tag to create with the typed name. */
export type TagOption = { tag: TagDetails; create?: undefined } | { create: string; tag?: undefined };

let pointerPressed = false;
addEventListener('pointerdown', () => { pointerPressed = true; }, true);
addEventListener('pointerup', () => { pointerPressed = false; }, true);
addEventListener('pointercancel', () => { pointerPressed = false; }, true);

let nextId = 0;

/**
 * Autocomplete over the tags: typing lists the tags whose name, shorthand or alias contains the text (then the ones
 * inheriting from them), and optionally offers to create a tag with that name (`allowCreate`). `exclude()` gives
 * the ids not to offer; `pick(option)` runs when one is chosen, after which the box is emptied (a rejection is
 * shown as a toast).
 */
@Component({
    selector: 'sfe-tag-picker',
    imports: [IconComponent, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tag-picker' },
    template: `
        <div class="tag-input-wrap">
            <svg sfeIcon="tag"></svg>
            <input #input type="text" class="tag-input" [placeholder]="placeholder()" autocomplete="off" spellcheck="false"
                   role="combobox" aria-autocomplete="list" [attr.aria-controls]="listId" [attr.aria-expanded]="options().length > 0"
                   [attr.aria-activedescendant]="active() >= 0 ? listId + '-' + active() : null"
                   (input)="onInput()" (focus)="refresh()" (blur)="onBlur()" (keydown)="onKeydown($event)">
        </div>
        <ul class="tag-options" role="listbox" [id]="listId" [hidden]="!options().length">
            @for (option of options(); track $index; let index = $index) {
                <li role="option" [id]="listId + '-' + index" [class.create]="option.create" [attr.aria-selected]="index === active()"
                    (mousedown)="$event.preventDefault()" (click)="choose(option)">
                    @if (option.create) {
                        <svg sfeIcon="plus"></svg><span>Create tag <strong>“{{ option.create }}”</strong></span>
                    } @else {
                        <sfe-tag-chip [tag]="option.tag!" />
                        @if (describe(option.tag!); as detail) {
                            <span class="tag-option-detail">{{ detail }}</span>
                        }
                    }
                </li>
            }
        </ul>
    `,
})
export class TagPickerComponent {
    readonly placeholder = input('');
    readonly allowCreate = input(false);
    readonly exclude = input<() => ReadonlySet<number>>(() => new Set());
    readonly pick = input.required<(option: TagOption) => Promise<void>>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

    protected readonly listId = `tag-options-${++nextId}`;
    protected readonly options = signal<TagOption[]>([]);
    protected readonly active = signal(-1);
    protected readonly describe = describeTag;
    private timer = 0;
    private sequence = 0;
    private busy = false;

    focus(): void {
        this.input().nativeElement.focus();
    }

    protected onInput(): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => this.refresh(), 150);
    }

    async refresh(): Promise<void> {
        const input = this.input().nativeElement;
        const text = input.value.trim();
        const request = ++this.sequence;
        let results;
        try {
            results = await this.api.tags({ q: text, limit: 40 });
        } catch (error) {
            this.toasts.error(`Could not load tags: ${(error as Error).message}`);
            return;
        }
        if (request !== this.sequence || document.activeElement !== input)
            return;

        const skip = this.exclude()();
        const options: TagOption[] = [...results.matches, ...results.descendants].filter(tag => !skip.has(tag.id)).map(tag => ({ tag }));
        const exact = results.matches.some(tag => tag.name.toLocaleLowerCase() === text.toLocaleLowerCase());
        if (this.allowCreate() && text && !exact)
            options.push({ create: text });
        this.options.set(options);
        this.setActive(options.length ? 0 : -1);
    }

    protected async choose(option: TagOption): Promise<void> {
        if (this.busy)
            return;
        this.busy = true;
        try {
            await this.pick()(option);
            this.input().nativeElement.value = '';
            await this.refresh();
        } catch (error) {
            this.toasts.error((error as Error).message);
        } finally {
            this.busy = false;
        }
    }

    private close(): void {
        this.sequence++;
        this.options.set([]);
        this.active.set(-1);
    }

    protected onBlur(): void {
        // When a press elsewhere takes the focus, collapsing the list right away would move what is under the
        // pointer before the release, and the click would miss its button: wait until the click is over.
        if (pointerPressed)
            addEventListener('pointerup', () => setTimeout(() => {
                if (document.activeElement !== this.input().nativeElement)
                    this.close();
            }), { once: true, capture: true });
        else
            this.close();
    }

    protected onKeydown(event: KeyboardEvent): void {
        const options = this.options();
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            if (!options.length) {
                void this.refresh();
                return;
            }
            const step = event.key === 'ArrowDown' ? 1 : -1;
            this.setActive((this.active() + step + options.length) % options.length);
        } else if (event.key === 'Enter') {
            event.preventDefault();
            if (this.active() >= 0)
                void this.choose(options[this.active()]);
        } else if (event.key === 'Escape' && options.length) {
            // Close the suggestions, not the dialog.
            event.preventDefault();
            event.stopPropagation();
            this.close();
        }
    }

    private setActive(index: number): void {
        this.active.set(index);
        queueMicrotask(() => document.getElementById(`${this.listId}-${index}`)?.scrollIntoView({ block: 'nearest' }));
    }
}
