import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { trackScrollbarWidth } from './core/scrollbar-width';
import { UndoHistory } from './core/undo-history';
import { ContextMenuComponent } from './menus/context-menu';
import { IconSpriteComponent } from './shared/icon-sprite.component';
import { ToastsComponent } from './shared/toasts.component';

/** Where Ctrl+Z is the field's or the dialog's own: text typed, a dialog open (the viewer and editors are dialogs). */
const OWN_UNDO =
    'input:not([type="checkbox"]), select, textarea, dialog, [contenteditable]:not([contenteditable="false"])';

/**
 * The pages (see app.routes.ts), with the icons they use, the toasts and the context menu (every right click).
 * Ctrl+Z (⌘Z) undoes the tab's latest action that can be (`UndoHistory`), on every page.
 */
@Component({
    selector: 'sfe-root',
    imports: [RouterOutlet, IconSpriteComponent, ToastsComponent, ContextMenuComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { '(document:keydown)': 'onKeydown($event)' },
    template: `
        <sfe-icon-sprite />
        <router-outlet />
        <sfe-toasts />
        <sfe-context-menu />
    `,
})
export class App {
    private readonly undos = inject(UndoHistory);

    constructor() {
        inject(DestroyRef).onDestroy(trackScrollbarWidth());
    }

    protected onKeydown(event: KeyboardEvent): void {
        if (
            event.defaultPrevented ||
            !(event.ctrlKey || event.metaKey) ||
            event.altKey ||
            event.shiftKey ||
            event.key.toLowerCase() !== 'z' ||
            (event.target as Element | null)?.closest?.(OWN_UNDO)
        )
            return;
        if (this.undos.undoLatest()) event.preventDefault();
    }
}
