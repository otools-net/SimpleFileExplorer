import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { icu } from '../../core/i18n/icu';
import { DirectoryListing } from '../../core/models';
import { ListingStore } from '../../library/listing.store';
import { IconComponent } from '../../shared/icon.component';

/** Folders the card lists; the folder's listing shows them all. */
export const FOLDERS_SHOWN = 12;

/** The first page of `path`'s listing, sorted by name: its folders come first. */
function firstPage(api: Api, path: string): Promise<DirectoryListing> {
    return api.browse({ path, offset: 0, limit: FOLDERS_SHOWN + 1, sort: 'name', desc: false });
}

/**
 * Where the session's files start: the top of the library, or the folder further down its folder rules lead to
 * (the server's `access.start`, as the listing's `ListingStore.start`).
 */
export async function startFolder(api: Api): Promise<DirectoryListing> {
    const top = await firstPage(api, '');
    const start = top.access?.start;
    return start ? firstPage(api, start) : top;
}

/**
 * The dashboard's Files: the folder Files opens (the top of the library, or the first folder further down the
 * session may enter), its folders each opening its listing, and how many folders and files it holds.
 */
@Component({
    selector: 'sfe-files-widget',
    imports: [RouterLink, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'files-widget' },
    template: `
        @if (listing(); as listing) {
            <a class="dashboard-files-top" routerLink="/" [queryParams]="store.libraryParams(listing.path)"
               [title]="openHint()" data-start-folder>
                <svg sfeIcon="folder"></svg><span>{{ name() }}</span>
            </a>
            <ul class="quick-link-list">
                @for (folder of folders(); track folder.path) {
                    <li class="quick-link" [attr.data-folder]="folder.path">
                        <a class="quick-link-target" routerLink="/" [queryParams]="store.libraryParams(folder.path)"
                           [title]="'/' + folder.path">
                            <svg sfeIcon="folder"></svg><span class="quick-link-name">{{ folder.name }}</span>
                        </a>
                    </li>
                }
            </ul>
            <p class="dashboard-card-note">{{ counts() }}</p>
        } @else if (failed()) {
            <p class="dashboard-card-note" i18n>The files could not be read.</p>
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class FilesWidgetComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    protected readonly store = inject(ListingStore);

    protected readonly listing = signal<DirectoryListing | null>(null);
    protected readonly failed = signal(false);
    protected readonly folders = computed(() =>
        (this.listing()?.items ?? []).filter((item) => item.kind === 'directory').slice(0, FOLDERS_SHOWN),
    );
    protected readonly name = computed(() => {
        const listing = this.listing();
        return !listing?.path ? this.config.libraryName() : `/${listing.path}`;
    });
    protected readonly counts = computed(() => {
        const listing = this.listing()!;
        const folders = listing.directoryCount;
        const files = listing.fileCount;
        return icu(
            $localize`{folders, plural, =0 {No folders} one {# folder} other {# folders}}, \
{files, plural, =0 {no files} one {# file} other {# files}}.`,
            { folders, files },
        );
    });

    async ngOnInit(): Promise<void> {
        try {
            this.listing.set(await startFolder(this.api));
        } catch {
            this.failed.set(true);
        }
    }

    protected openHint(): string {
        return $localize`Browse the files of ${this.name()}:folder:.`;
    }
}
