import { Directive, effect, ElementRef, inject, input, Renderer2, WritableSignal } from '@angular/core';

/**
 * A button busy with a request (`[sfeBusy]="busy()"`): dimmed with a spinner, and marked busy. The button's own
 * `[disabled]` binding should include the busy state, so a slow request can't be sent twice.
 */
@Directive({
    selector: 'button[sfeBusy]',
    host: { '[class.busy]': 'sfeBusy()', '[attr.aria-busy]': 'sfeBusy() || null' },
})
export class BusyDirective {
    readonly sfeBusy = input(false);

    constructor() {
        const button = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;
        const renderer = inject(Renderer2);
        let spinner: HTMLElement | null = null;
        effect(() => {
            if (this.sfeBusy() && !spinner) {
                spinner = renderer.createElement('span') as HTMLElement;
                spinner.className = 'button-spinner';
                spinner.setAttribute('aria-hidden', 'true');
                button.prepend(spinner);
            } else if (!this.sfeBusy() && spinner) {
                spinner.remove();
                spinner = null;
            }
        });
    }
}

/** Runs `task` with `busy` set until it settles (resolved or rejected); returns the task's promise. */
export async function whileBusy<T>(busy: WritableSignal<boolean>, task: () => Promise<T>): Promise<T> {
    busy.set(true);
    try {
        return await task();
    } finally {
        busy.set(false);
    }
}
