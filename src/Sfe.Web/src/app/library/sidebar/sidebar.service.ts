import { inject, Injectable, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { TagSidebar, TrashCount } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';

const PREFERENCE = 'sfe.sidebar';

/**
 * The tag sidebar's lists and whether it is open. On phones (`narrow`) it is a drawer over the page: its state is
 * not a preference there.
 */
@Injectable({ providedIn: 'root' })
export class SidebarService {
    private readonly api = inject(Api);
    private readonly narrowQuery = matchMedia('(max-width: 760px)');
    private timer = 0;
    private sequence = 0;
    private trashTimer = 0;
    private trashSequence = 0;

    readonly data = signal<TagSidebar | null>(null);
    readonly open = signal(this.preferredOpen());
    /** What the trash holds, for its link; null until read. */
    readonly trash = signal<TrashCount | null>(null);

    constructor() {
        this.narrowQuery.addEventListener('change', () => this.open.set(this.preferredOpen()));
    }

    get narrow(): boolean {
        return this.narrowQuery.matches;
    }

    toggle(): void {
        this.setOpen(!this.open());
    }

    setOpen(open: boolean, { remember = true } = {}): void {
        this.open.set(open);
        if (remember && !this.narrow)
            writePreference(PREFERENCE, open ? 'open' : 'closed');
    }

    /** The drawer closes when something in it was picked (phones only). */
    closeIfOverlay(): void {
        if (this.narrow)
            this.setOpen(false, { remember: false });
    }

    /** Reloads the lists, a moment later so that bursts of changes make one request. */
    refresh(): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => this.reload(), 300);
    }

    async reload(): Promise<void> {
        clearTimeout(this.timer);
        const request = ++this.sequence;
        try {
            const data = await this.api.tagSidebar();
            if (request === this.sequence)
                this.data.set(data);
        } catch {
            // The index may be unavailable: keep what is shown.
        }
    }

    /** Reads the trash's count again, a moment later: emptying it tells many changes. */
    refreshTrash(): void {
        clearTimeout(this.trashTimer);
        this.trashTimer = setTimeout(() => this.reloadTrash(), 300);
    }

    async reloadTrash(): Promise<void> {
        clearTimeout(this.trashTimer);
        const request = ++this.trashSequence;
        try {
            const count = await this.api.trashCount();
            if (request === this.trashSequence)
                this.trash.set(count);
        } catch {
            // Keep what is shown.
        }
    }

    private preferredOpen(): boolean {
        return !this.narrowQuery.matches && readPreference(PREFERENCE) !== 'closed';
    }
}
