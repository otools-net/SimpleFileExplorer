import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { TagSummary } from '../core/models';
import { IconComponent } from '../shared/icon.component';
import { textOn } from './tags';

/**
 * A tag chip in the tag's color. With `clickable` the name is a button (`pick`); with `removable` a "×" button
 * (`remove`) follows it. `label` replaces the tag's name.
 */
@Component({
    selector: 'sfe-tag-chip',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'tag-chip',
        '[class.colored]': 'Boolean(tag().color)',
        '[class.hidden-tag]': 'tag().isHidden',
        '[attr.data-tag-id]': 'tag().id',
        '[style.--chip-bg]': 'color()?.hex ?? null',
        '[style.--chip-text]': 'color() ? textOn(color()!.hex) : null',
    },
    template: `
        @if (clickable()) {
            <button type="button" class="tag-name" [title]="title() || text()" (click)="pick.emit(tag())">{{ text() }}</button>
        } @else {
            <span class="tag-name" [title]="title() || text()">{{ text() }}</span>
        }
        @if (removable()) {
            <button type="button" class="tag-remove" [attr.aria-label]="'Remove ' + text()" [title]="'Remove ' + text()" (click)="remove.emit(tag())">
                <svg sfeIcon="close"></svg>
            </button>
        }
    `,
})
export class TagChipComponent {
    private readonly config = inject(ConfigService);

    readonly tag = input.required<TagSummary>();
    readonly label = input<string | null>(null);
    readonly title = input<string | null>(null);
    readonly clickable = input(false);
    readonly removable = input(false);
    readonly pick = output<TagSummary>();
    readonly remove = output<TagSummary>();

    protected readonly text = computed(() => this.label() ?? (this.tag().displayName || this.tag().name));
    protected readonly color = computed(() => {
        const slug = this.tag().color;
        return slug ? this.config.config()?.tagColors.find(color => color.slug === slug) ?? null : null;
    });
    protected readonly textOn = textOn;
    protected readonly Boolean = Boolean;
}
