import { inject, Injectable } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ListingStore } from '../library/listing.store';

/**
 * Downloading listed items (agents/docs/features/archives.md): one file as it is, several or a folder as a zip the
 * server writes while it is sent. The browser saves both like any download, from a link.
 */
@Injectable({ providedIn: 'root' })
export class FileDownloads {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);

    /** May save `item` (a folder: the files in it) to this device. */
    canDownload(item: LibraryItem): boolean {
        return this.store.can(Rights.download, item) && this.store.can(Rights.viewFiles, item);
    }

    /** What a download of `items` gets: a file as it is, else a zip. */
    isZip(items: LibraryItem[]): boolean {
        const allowed = items.filter(item => this.canDownload(item));
        return allowed.length > 1 || allowed.some(item => item.kind === 'directory');
    }

    /** Downloads those of `items` the session may download. False when none may be. */
    async download(items: LibraryItem[]): Promise<boolean> {
        const allowed = items.filter(item => this.canDownload(item));
        if (!allowed.length)
            return false;
        if (!this.isZip(allowed)) {
            save(this.config.links.download(allowed[0].path), allowed[0].name);
            return true;
        }
        try {
            const link = await this.api.createArchive(allowed.map(item => item.path));
            save(link.url, link.fileName);
            this.toasts.show(`Downloading ${link.fileName}: your browser shows its progress.`);
        } catch (error) {
            this.toasts.error(`Could not download: ${(error as Error).message}`);
        }
        return true;
    }
}

/** Lets the browser download `url` as a file, without leaving the page. */
function save(url: string, fileName: string): void {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
}
