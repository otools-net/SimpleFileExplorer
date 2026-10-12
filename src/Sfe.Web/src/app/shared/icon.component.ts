import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * An icon of the sprite (IconSpriteComponent): `<svg sfeIcon="folder"></svg>`. Styled by `.icon` (stroke in the
 * current color); hidden from assistive technology, so buttons carry a label of their own.
 */
@Component({
    selector: 'svg[sfeIcon]',
    template: '<svg:use [attr.href]="\'#i-\' + sfeIcon()" />',
    host: { class: 'icon', 'aria-hidden': 'true' },
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IconComponent {
    readonly sfeIcon = input.required<string>();
}
