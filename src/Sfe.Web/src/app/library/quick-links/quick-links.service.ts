import { computed, inject, Injectable, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { plural } from '../../core/format';
import { QuickLink } from '../../core/models';
import { baseName } from '../../core/paths';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { ListingStore } from '../listing.store';

/** Drag data of folders and files of the page (a JSON array of paths): dropped on the quick links, they are added. */
export const DRAG_ITEMS = 'application/x-sfe-paths';
/** Drag data of a quick link being moved in its list (its path). */
export const DRAG_LINK = 'application/x-sfe-quick-link';

/**
 * The signed-in user's quick links in the library (the sidebar's first section), kept on the server: adding,
 * moving and removing answer the whole list, which replaces the one shown. Hidden without `account.quickLinks`
 * (visitors without an account never have it).
 */
@Injectable({ providedIn: 'root' })
export class QuickLinksService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private sequence = 0;

    readonly enabled = computed(() => this.config.rights().has(Rights.quickLinks));
    readonly links = signal<QuickLink[]>([]);
    readonly loaded = signal(false);
    readonly paths = computed(() => new Set(this.links().map(link => link.path)));
    /** Folders or files of the page are being dragged: the quick links show they take them. */
    readonly dragging = signal(false);

    has(path: string): boolean {
        return this.paths().has(path);
    }

    async load(): Promise<void> {
        if (!this.enabled())
            return;
        await this.change(() => this.api.quickLinks(), null);
        this.loaded.set(true);
    }

    /** Adds `paths` before the link at `position` (at the end without one); those already there move. */
    add(paths: string[], position: number | null = null): Promise<boolean> {
        const added = paths.filter(path => !this.has(path));
        const message = added.length === 1 ? `Added “${this.nameOf(added[0])}” to the quick links`
            : added.length ? `Added ${plural(added.length, 'item')} to the quick links` : null;
        return this.change(() => this.api.addQuickLinks(paths, position), message);
    }

    /** Moves the link to `path` before the one at `position` of the list (its length: to the end). */
    move(path: string, position: number): Promise<boolean> {
        return this.change(() => this.api.addQuickLinks([path], position), null);
    }

    remove(path: string): Promise<boolean> {
        const name = this.nameOf(path);
        return this.change(() => this.api.removeQuickLink(path), `Removed “${name}” from the quick links`);
    }

    /** Adds the folder or file, or removes it when it is a quick link already (the pin buttons). */
    toggle(path: string): Promise<boolean> {
        return this.has(path) ? this.remove(path) : this.add([path]);
    }

    /**
     * What a drag started on `target` carries to the quick links: the folder or file under it (a listed item, a
     * breadcrumb), or every selected file when it is one of them; null for anything else.
     */
    dragPaths(target: EventTarget | null): string[] | null {
        const element = target instanceof Element ? target.closest<HTMLElement>('[data-path]') : null;
        if (!element || element.closest('.quick-links'))
            return null;
        const path = element.getAttribute('data-path')!;
        return this.store.isSelected(path) ? this.store.selectedFiles().map(item => item.path) : [path];
    }

    /** The name shown for `path`: the link's, else the last segment (the library's name for the root). */
    private nameOf(path: string): string {
        return this.links().find(link => link.path === path)?.name || baseName(path) || this.config.config()?.rootName || '';
    }

    /** Runs a request answering the list; only the last one sent sets it. Says `done` once done, the error otherwise. */
    private async change(request: () => Promise<QuickLink[]>, done: string | null): Promise<boolean> {
        const sequence = ++this.sequence;
        try {
            const links = await request();
            if (sequence === this.sequence)
                this.links.set(links);
            if (done)
                this.toasts.show(done);
            return true;
        } catch (error) {
            this.toasts.error(`Quick links: ${(error as Error).message}`);
            return false;
        }
    }
}
