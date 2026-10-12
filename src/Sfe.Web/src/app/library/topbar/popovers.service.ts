import { Injectable, signal } from '@angular/core';

export type Popover = 'menu' | 'search-help';

/** The top bar's popovers (the ⋮ menu, the search syntax help): one open at a time. */
@Injectable({ providedIn: 'root' })
export class Popovers {
    readonly open = signal<Popover | null>(null);

    toggle(popover: Popover): void {
        this.open.update(open => open === popover ? null : popover);
    }

    close(): void {
        this.open.set(null);
    }
}
