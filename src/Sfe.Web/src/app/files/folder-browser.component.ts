import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { FolderLevelName, UsableConnection } from '../core/connection.models';
import { DirectoryListing } from '../core/models';
import { IconComponent } from '../shared/icon.component';

/** Folders listed at most in a folder of the picker (open one to go further). */
export const FOLDERS_SHOWN = 200;

/** A step of the way to the folder chosen; `top`: the list of places. */
export interface FolderCrumb {
    path: string;
    name: string;
    connection: number | null;
    top?: boolean;
}

/**
 * The folder picked by clicking through the library, in the dialogs that put something into a folder (Move to…,
 * Copy to…, Extract…, Compress…): the folder chosen, the way to it, its subfolders, and whether a right applies there.
 * Move to… and Copy to… also offer the connections the session may write to (`offer`): a top level lists the library
 * and them. The dialog holds it (`folderBrowser()`), <sfe-folder-browser> shows it.
 */
export class FolderBrowser {
    /** The folder chosen. */
    readonly target = signal('');
    /** The connection browsed; null for the library. */
    readonly connection = signal<number | null>(null);
    /** The connections offered beside the library; null: the library only (no top level). */
    readonly places = signal<UsableConnection[] | null>(null);
    /** The top level (the library and the connections) is shown: no folder is chosen. */
    readonly top = signal(false);
    readonly listing = signal<DirectoryListing | null>(null);
    readonly loading = signal(false);
    /** Why the place browsed can't be read (a connection to sign in to again...); null when it was. */
    readonly problem = signal<string | null>(null);
    /** The session's access to the connection browsed. */
    private readonly access = signal<FolderLevelName | null>(null);

    readonly folders = computed(() => this.listing()?.items.filter((item) => item.kind === 'directory') ?? []);
    readonly truncated = computed(() => (this.listing()?.directoryCount ?? 0) > this.folders().length);
    /** The name of the place browsed: the library's or the connection's. */
    readonly placeName = computed(() => this.nameOf(this.connection()));
    readonly crumbs = computed<FolderCrumb[]>(() => {
        const connection = this.connection();
        const places = $localize`:the library and the connections:Places`;
        const top: FolderCrumb[] = this.places() ? [{ path: '', name: places, connection: null, top: true }] : [];
        if (this.top()) return top;
        const root = { path: '', name: this.placeName(), connection };
        const parts = this.target() ? this.target().split('/') : [];
        const steps = parts.map((name, index) => ({ path: parts.slice(0, index + 1).join('/'), name, connection }));
        return [...top, root, ...steps];
    });

    constructor(
        private readonly api: Api,
        private readonly config: ConfigService,
    ) {}

    /**
     * `right` may be used in the folder shown (a connection's: written to; the library's: its folder rules, else the
     * person's rights there); false before it is read.
     */
    allows(right: string): boolean {
        const listing = this.listing();
        if (!listing) return false;
        if (this.connection() !== null) return this.access() === 'write';
        return listing.access
            ? !listing.access.pathOnly && listing.access.rights.includes(right)
            : this.config.canInLibrary(right);
    }

    /** The library's name, or the connection's. */
    nameOf(connection: number | null): string {
        if (connection === null) return this.config.libraryName();
        return this.places()?.find((place) => place.id === connection)?.name ?? this.config.config()?.rootName ?? '';
    }

    /** Offers the connections the session writes to beside the library: the picker gets its top level. */
    offer(connections: UsableConnection[]): void {
        this.places.set(connections.filter((connection) => connection.access === 'write'));
    }

    /** Forgets the folder read: until the next `browse`, nothing is allowed. */
    reset(target = '', connection: number | null = null): void {
        this.target.set(target);
        this.connection.set(connection);
        this.top.set(false);
        this.listing.set(null);
        this.problem.set(null);
    }

    /** Shows the library and the connections: a place is opened next. */
    showTop(): void {
        this.reset();
        this.top.set(true);
    }

    go(crumb: FolderCrumb): void {
        if (crumb.top) this.showTop();
        else void this.browse(crumb.path, crumb.connection);
    }

    async browse(path: string, connection = this.connection()): Promise<void> {
        this.target.set(path);
        this.connection.set(connection);
        this.top.set(false);
        this.loading.set(true);
        this.problem.set(null);
        try {
            const listing = await this.read(path, connection);
            if (this.target() === path && this.connection() === connection) this.listing.set(listing);
        } catch (error) {
            // A folder gone meanwhile: back to the top; a place that can't be read at all: why.
            if (path && !(error instanceof ApiError && error.status === 409)) await this.browse('', connection);
            else if (this.target() === path && this.connection() === connection) this.refused(error);
        } finally {
            this.loading.set(false);
        }
    }

    /** Nothing is listed, so nothing is allowed there; the reason is shown instead. */
    private refused(error: unknown): void {
        this.listing.set(null);
        this.problem.set(error instanceof Error ? error.message : String(error));
    }

    private async read(path: string, connection: number | null): Promise<DirectoryListing> {
        const query = { path, offset: 0, limit: FOLDERS_SHOWN, sort: 'name' as const, desc: false };
        if (connection === null) return this.api.browse(query);
        const page = await this.api.connectionBrowse(connection, query);
        this.access.set(page.access);
        return page.listing;
    }
}

/** A folder browser for the dialog creating it (in its injection context). */
export function folderBrowser(): FolderBrowser {
    return new FolderBrowser(inject(Api), inject(ConfigService));
}

/** The way to the folder chosen (each step goes back to it) and its subfolders (each opens), or the places. */
@Component({
    selector: 'sfe-folder-browser',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './folder-browser.component.html',
})
export class FolderBrowserComponent {
    private readonly config = inject(ConfigService);
    readonly browser = input.required<FolderBrowser>();
    /** Why a folder can't be opened (a folder moved can't take itself); null when it can. */
    readonly closedBecause = input<(path: string) => string | null>(() => null);
    protected readonly shown = FOLDERS_SHOWN;

    /** The icon of a connection's kind (`config.connections.kinds`). */
    protected kindIcon(connection: UsableConnection): string {
        return this.config.config()?.connections?.kinds.find((kind) => kind.kind === connection.kind)?.icon ?? 'globe';
    }

    protected placeTitle(connection: UsableConnection): string {
        return connection.state === 'needsReconnecting'
            ? $localize`This connection needs signing in again.`
            : $localize`Open “${connection.name}:connection:”.`;
    }

    protected openTitle(name: string): string {
        return $localize`Open “${name}:folder:”.`;
    }
}
