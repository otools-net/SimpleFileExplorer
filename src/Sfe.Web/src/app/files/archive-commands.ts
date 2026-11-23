import { inject, Injectable } from '@angular/core';
import { isExtractable } from '../formats/formats';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileDownloads } from './downloads';
import { extractEachTitle, stemOf } from './extract.dialog';
import { FileTransfers } from './file-transfers';
import { ItemCommand, ItemCommandId, ItemMenuEntry, ItemScope, namesOf, refused } from './item-scope';

/**
 * The commands of zips and downloads (Extract to “…”, Extract each here, Extract…, Compress…, Download), for
 * `ItemCommands`: the extractions under "Archive ▸" with Compress…, Download on its own.
 */
@Injectable({ providedIn: 'root' })
export class ArchiveCommands {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly transfers = inject(FileTransfers);
    private readonly downloads = inject(FileDownloads);

    readonly commands: Partial<Record<ItemCommandId, ItemCommand>> = {
        extractBeside: (scope) => this.extractBeside(scope),
        extractEach: (scope) => this.extractEach(scope),
        extract: (scope) => this.extract(scope),
        compress: (scope) => this.compress(scope),
        download: (scope) => this.download(scope),
    };

    /** Beside the archives, in the folder listed: not in search results (whose folders differ). */
    private besideApplies(scope: ItemScope): boolean {
        return (
            !this.store.searching() &&
            !this.store.folderAccess()?.pathOnly &&
            scope.targets.every((target) => isExtractable(target))
        );
    }

    private extractBeside(scope: ItemScope): ItemMenuEntry | null {
        const { item } = scope;
        if (scope.targets.length !== 1 || !this.besideApplies(scope)) return null;
        const entry: ItemMenuEntry = {
            id: 'extractBeside',
            label: $localize`Extract to “${stemOf(item.name)}:folder:”`,
            icon: 'extract',
            title: $localize`Extract it here, in the background, into a new folder named after it \
(or after the one folder it holds).`,
            run: () =>
                void this.transfers.extract({ archive: item.path, target: this.store.route().path, newFolder: true }),
        };
        return refused(entry, this.transfers.canExtractBeside(item), 'extract', [item.name]);
    }

    /** Several archives (all of them), each into a folder of its own beside it. */
    private extractEach(scope: ItemScope): ItemMenuEntry | null {
        if (scope.targets.length < 2 || !this.besideApplies(scope)) return null;
        const entry: ItemMenuEntry = {
            id: 'extractEach',
            label: $localize`Extract each here`,
            icon: 'extract',
            title: extractEachTitle(scope.targets.length),
            run: () => {
                void this.transfers.extractEach(scope.targets);
                this.store.clearSelection();
            },
        };
        return refused(entry, this.transfers.canExtractEach(scope.targets), 'extract', namesOf(scope));
    }

    private extract({ item, targets }: ItemScope): ItemMenuEntry | null {
        if (targets.length !== 1 || !isExtractable(item) || !this.dialogs.extract()) return null;
        const entry: ItemMenuEntry = {
            id: 'extract',
            label: $localize`Extract…`,
            icon: 'extract',
            title: $localize`Extract “${item.name}:name:” into a folder picked next, with its password if it has one.`,
            run: () => this.dialogs.extract()?.open({ path: item.path, name: item.name }),
        };
        return refused(entry, this.transfers.canExtract(item), 'extract', [item.name]);
    }

    /** Those of the items that may be read go in the zip. */
    private compress(scope: ItemScope): ItemMenuEntry | null {
        if (!this.dialogs.compress()) return null;
        const allowed = scope.targets.filter((target) => this.transfers.canCompress(target));
        const entry: ItemMenuEntry = {
            id: 'compress',
            label: $localize`Compress…`,
            icon: 'compress',
            title:
                scope.targets.length > 1
                    ? $localize`Put the selected items in a zip file in this folder, with a password if you want one.`
                    : $localize`Put “${scope.item.name}:name:” in a zip file in this folder, \
with a password if you want one.`,
            run: () => this.dialogs.compress()?.open(allowed),
        };
        return refused(entry, allowed.length > 0, 'compress', namesOf(scope));
    }

    /** One file as it is; a folder or several items as one zip. */
    private download(scope: ItemScope): ItemMenuEntry {
        const zip = this.downloads.isZip(scope.targets);
        const entry: ItemMenuEntry = {
            id: 'download',
            label: zip ? $localize`Download as zip` : $localize`Download`,
            icon: 'download',
            title: zip
                ? scope.targets.length > 1
                    ? $localize`Save the selected items to this device, in one zip file \
(folders with everything in them).`
                    : $localize`Save this folder and everything in it to this device, as a zip file.`
                : $localize`Save this file to this device.`,
            run: () => void this.downloads.download(scope.targets),
        };
        const allowed = scope.targets.some((target) => this.downloads.canDownload(target));
        return refused(entry, allowed, 'download', namesOf(scope));
    }
}
