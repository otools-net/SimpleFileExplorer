import { ChangeDetectionStrategy, Component } from '@angular/core';

/** The icons of the app as SVG symbols, rendered once by the root component; IconComponent refers to them. */
@Component({
    selector: 'sfe-icon-sprite',
    host: { hidden: '' },
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './icon-sprite.component.html',
})
export class IconSpriteComponent {}
