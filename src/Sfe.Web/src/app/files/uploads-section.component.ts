import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate, formatSize } from '../core/format';
import { UploadInfo } from '../core/models';
import { baseName } from '../core/paths';
import { ToastService } from '../core/toast.service';
import { BusyDirective } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { FolderMenu } from '../library/folder-menu';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../menus/context-menu';

/**
 * Administration › File management › Uploads: everyone's unfinished uploads in the space (right `settings.files`),
 * with who sends them, how far they got and when they last received something; Cancel removes what one received.
 */
@Component({
    selector: 'sfe-uploads-section',
    imports: [BusyDirective, IconComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel uploads-section' },
    template: `
        <p class="settings-intro">
            Being sent, paused, or cut off: their sender goes on by picking the same file again. Those that receive nothing for
            {{ keepHours() }} hours are removed by themselves.
        </p>
        <ul class="tracking-missing-list">
            @if (loading()) {
                <li class="tag-loading"><span class="spinner"></span></li>
            } @else if (!items().length) {
                <li class="muted uploads-empty">No upload under way.</li>
            }
            @for (item of items(); track item.id) {
                <li class="tracking-missing" [attr.data-id]="item.id" [sfeContextMenu]="uploadMenu(item)">
                    <div class="tracking-missing-head">
                        <span class="tracking-thumb"><svg sfeIcon="upload"></svg></span>
                        <span class="tracking-file">
                            <strong class="tracking-name" [title]="item.name">{{ baseName(item.name) }}</strong>
                            <span class="tracking-was">{{ describe(item) }}</span>
                            <span class="tracking-was">Into
                                <a routerLink="/" [queryParams]="{ path: item.target || null }" [title]="'Open ' + (item.target || 'the library')">{{ item.target || rootName() }}</a>
                            </span>
                        </span>
                        <span class="tracking-actions">
                            <button type="button" class="button small" title="Stop it and remove what it received (asks first)"
                                    [sfeBusy]="busy() === item.id" [disabled]="busy() !== null" (click)="cancel(item)">Cancel</button>
                        </span>
                    </div>
                </li>
            }
        </ul>
    `,
})
export class UploadsSectionComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly folders = inject(FolderMenu);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);

    protected readonly items = signal<UploadInfo[]>([]);
    protected readonly loading = signal(true);
    protected readonly busy = signal<string | null>(null);
    protected readonly baseName = baseName;

    async ngOnInit(): Promise<void> {
        try {
            this.items.set(await this.api.uploads(true));
        } catch (error) {
            this.toasts.error(`Could not list the uploads: ${(error as Error).message}`);
        } finally {
            this.loading.set(false);
        }
    }

    protected rootName(): string {
        return this.config.config()?.rootName ?? 'the library';
    }

    protected keepHours(): number {
        return this.config.config()?.files?.keepUnfinishedUploadsHours ?? 48;
    }

    protected describe(item: UploadInfo): string {
        const percent = item.size ? Math.floor(item.received * 100 / item.size) : 100;
        return `${formatSize(item.received)} of ${formatSize(item.size)} (${percent}%) · by ${item.ownerName} · last received ${formatDate(item.lastActivity)}`;
    }

    /** An upload's menu (a right click): Cancel, then the folder it goes into. */
    protected uploadMenu(item: UploadInfo): MenuTarget {
        return {
            kind: 'job', key: `upload-info:${item.id}`, name: baseName(item.name),
            entries: () => {
                const entries: MenuEntry[] = this.busy() === null
                    ? [{ id: 'cancel', label: 'Cancel', icon: 'close', title: 'Stop it and remove what it received (asks first)', run: () => void this.cancel(item) }]
                    : [];
                return [...entries, ...this.folders.targetEntries(item.target)];
            },
        };
    }

    protected async cancel(item: UploadInfo): Promise<void> {
        if (!confirm(`Cancel the upload of “${baseName(item.name)}” by ${item.ownerName}? What it received is removed.`))
            return;
        try {
            this.busy.set(item.id);
            await this.api.cancelUpload(item.id);
            this.items.update(items => items.filter(i => i.id !== item.id));
        } catch (error) {
            this.toasts.error(`Could not cancel: ${(error as Error).message}`);
        } finally {
            this.busy.set(null);
        }
    }
}
