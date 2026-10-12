import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { IconSpriteComponent } from './shared/icon-sprite.component';
import { ToastsComponent } from './shared/toasts.component';

/** The pages (see app.routes.ts), with the icons they use and the toasts. */
@Component({
    selector: 'sfe-root',
    imports: [RouterOutlet, IconSpriteComponent, ToastsComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-icon-sprite />
        <router-outlet />
        <sfe-toasts />
    `,
})
export class App {}
