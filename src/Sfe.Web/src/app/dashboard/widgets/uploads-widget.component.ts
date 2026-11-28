import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { baseName } from '../../core/paths';
import { UsableConnections } from '../../core/usable-connections';
import { quoted } from '../../files/file-dialog-texts';
import { uploadStatus, uploadTitle } from '../../files/upload-texts';
import { UploadBatch, UploadItem, UploadQueue } from '../../files/uploads';
import { ListingStore } from '../../library/listing.store';

/** What a batch's progress counts: the files not left out. */
function counted(batch: UploadBatch): UploadItem[] {
    return batch.items.filter((item) => item.state !== 'skipped' && item.state !== 'cancelled');
}

function active(batch: UploadBatch): boolean {
    return batch.items.some((item) => !['done', 'skipped', 'cancelled', 'failed'].includes(item.state));
}

/**
 * The dashboard's Uploads: the uploads of this tab (they go on while the dashboard is open) and those a reload
 * interrupted, listed from the server as the operations panel does. Each opens the folder it goes into, where the
 * panel pauses, resumes or cancels it.
 */
@Component({
    selector: 'sfe-uploads-widget',
    imports: [RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'uploads-widget' },
    template: `
        <ul class="dashboard-list">
            @for (batch of queue.batches(); track batch.key) {
                <li class="dashboard-upload" [class.active]="isActive(batch)">
                    <a routerLink="/" [queryParams]="store.browseParams(batch.target, batch.connection)"
                       [title]="hint">{{ title(batch) }}</a>
                    <div class="storage-meter" role="progressbar" aria-valuemin="0" aria-valuemax="100"
                         [attr.aria-valuenow]="percent(batch)" [attr.aria-label]="title(batch)">
                        <span [style.width.%]="percent(batch)"></span>
                    </div>
                    <span class="dashboard-card-note">{{ status(batch) }}</span>
                </li>
            }
        </ul>
        @if (!queue.batches().length) {
            <p class="dashboard-card-note" i18n>No uploads under way. Drop files on a folder of the listing to send
                them.</p>
        }
    `,
})
export class UploadsWidgetComponent implements OnInit {
    protected readonly queue = inject(UploadQueue);
    protected readonly store = inject(ListingStore);
    private readonly config = inject(ConfigService);
    private readonly connections = inject(UsableConnections);
    protected readonly hint = $localize`Open the folder they go into: its operations panel pauses or cancels them.`;

    ngOnInit(): void {
        void this.queue.resume();
    }

    protected isActive(batch: UploadBatch): boolean {
        return active(batch);
    }

    protected title(batch: UploadBatch): string {
        const files = counted(batch);
        const done = files.filter((item) => item.state === 'done').length;
        const failed = files.filter((item) => item.state === 'failed').length;
        return uploadTitle(active(batch), files.length, done, failed, quoted(this.folderName(batch)));
    }

    protected status(batch: UploadBatch): string {
        const files = counted(batch);
        const done = files.filter((item) => item.state === 'done').length;
        const sent = files.reduce((sum, item) => sum + item.sent, 0);
        const total = files.reduce((sum, item) => sum + item.size, 0);
        const paused = files.some((item) => item.state === 'paused');
        return uploadStatus({ done, total: files.length }, { sent, total }, paused, null);
    }

    protected percent(batch: UploadBatch): number {
        const files = counted(batch);
        const total = files.reduce((sum, item) => sum + item.size, 0);
        const sent = files.reduce((sum, item) => sum + item.sent, 0);
        return total ? Math.round((sent / total) * 100) : 100;
    }

    private folderName(batch: UploadBatch): string {
        if (batch.target) return baseName(batch.target);
        return batch.connection === null ? this.config.libraryName() : this.connections.name(batch.connection);
    }
}
