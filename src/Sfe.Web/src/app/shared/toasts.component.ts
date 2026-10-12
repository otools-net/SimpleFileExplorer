import { ChangeDetectionStrategy, Component, effect, ElementRef, inject } from '@angular/core';
import { ToastService } from '../core/toast.service';

/** The toasts of ToastService, in the corner of the page and above open dialogs. */
@Component({
    selector: 'sfe-toasts',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'toasts', role: 'status', popover: 'manual' },
    template: `
        @for (toast of toasts(); track toast.id) {
            <div class="toast" [class.error]="toast.error">{{ toast.message }}</div>
        }
    `,
})
export class ToastsComponent {
    protected readonly toasts = inject(ToastService).toasts;

    constructor() {
        const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
        let count = 0;
        // Modal dialogs sit in the top layer, above any z-index: showing the container again as a popover puts it
        // on top of them.
        effect(() => {
            const shown = this.toasts().length;
            if (shown > count && host.showPopover) {
                if (host.matches(':popover-open'))
                    host.hidePopover();
                host.showPopover();
            }
            count = shown;
        });
    }
}
