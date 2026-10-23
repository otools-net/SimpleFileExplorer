import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { trackScrollbarWidth } from './core/scrollbar-width';
import { ContextMenuComponent } from './menus/context-menu';
import { IconSpriteComponent } from './shared/icon-sprite.component';
import { ToastsComponent } from './shared/toasts.component';

/** The pages (see app.routes.ts), with the icons they use, the toasts and the context menu (every right click). */
@Component({
    selector: 'sfe-root',
    imports: [RouterOutlet, IconSpriteComponent, ToastsComponent, ContextMenuComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-icon-sprite />
        <router-outlet />
        <sfe-toasts />
        <sfe-context-menu />
    `,
})
export class App {
    constructor() {
        inject(DestroyRef).onDestroy(trackScrollbarWidth());
    }
}
