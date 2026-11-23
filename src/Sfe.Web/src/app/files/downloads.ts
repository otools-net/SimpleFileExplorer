import { inject, Injectable } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ListingStore } from '../library/listing.store';
import { itemCount, quoted } from './file-dialog-texts';

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
        const allowed = items.filter((item) => this.canDownload(item));
        return allowed.length > 1 || allowed.some((item) => item.kind === 'directory');
    }

    /** Downloads those of `items` the session may download. False when none may be. */
    async download(items: LibraryItem[]): Promise<boolean> {
        const allowed = items.filter((item) => this.canDownload(item));
        if (!allowed.length) return false;
        if (!this.isZip(allowed)) {
            save(this.config.links.download(allowed[0].path), allowed[0].name);
            return true;
        }
        try {
            const paths = allowed.map((item) => item.path);
            const place = this.config.place();
            const link = await (place ? this.api.createArchive(paths, place.id) : this.api.createArchive(paths));
            save(link.url, link.fileName);
            this.toasts.show($localize`Downloading ${quoted(link.fileName)}:zip:. Your browser shows its progress.`);
        } catch (error) {
            this.toasts.error(
                $localize`Could not download ${itemCount(allowed.length)}:items:: ${(error as Error).message}:reason:`,
            );
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
