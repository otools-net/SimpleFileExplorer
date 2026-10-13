import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';
import { QuickLinksService } from './quick-links.service';

/**
 * The button adding a folder or file to the quick links, or removing it when it is one (filled pin). Hidden without
 * the right. Give it more classes where it sits (`small ghost`).
 */
@Component({
    selector: 'button[sfePin]',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        type: 'button',
        class: 'icon-button pin-button',
        '[class.pinned]': 'pinned()',
        '[hidden]': '!links.enabled()',
        '[title]': 'title()',
        '[attr.aria-label]': 'title()',
        '[attr.aria-pressed]': 'pinned()',
        '(click)': 'toggle($event)',
    },
    template: `<svg sfeIcon="pin"></svg>`,
})
export class PinButtonComponent {
    /** The folder or file, relative to the library root. */
    readonly sfePin = input.required<string>();
    readonly name = input.required<string>();
    protected readonly links = inject(QuickLinksService);
    protected readonly pinned = computed(() => this.links.paths().has(this.sfePin()));
    protected readonly title = computed(() => this.pinned()
        ? `Remove “${this.name()}” from the quick links`
        : `Add “${this.name()}” to the quick links (or drag it onto the sidebar)`);

    protected toggle(event: MouseEvent): void {
        event.preventDefault();
        event.stopPropagation();
        void this.links.toggle(this.sfePin());
    }
}
