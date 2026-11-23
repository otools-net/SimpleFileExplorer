import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { UploadInfo } from './file.models';
import { baseName } from '../core/paths';
import { ToastService } from '../core/toast.service';
import { BusyDirective } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { FolderMenu } from '../library/folder-menu';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../menus/context-menu';
import { describeUpload, uploadFolderHint } from './upload-texts';

/**
 * Administration › File management › Uploads: everyone's unfinished uploads in the space (right `settings.files`),
 * with who sends them, how far they got and when they last received something; Cancel removes what one received.
 */
@Component({
    selector: 'sfe-uploads-section',
    imports: [BusyDirective, IconComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel uploads-section' },
    templateUrl: './uploads-section.component.html',
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
    protected readonly describe = describeUpload;
    protected readonly folderHint = uploadFolderHint;
    protected readonly cancelHint = $localize`Stop it and remove what it received (asks first).`;

    async ngOnInit(): Promise<void> {
        try {
            this.items.set(await this.api.uploads(true));
        } catch (error) {
            this.toasts.error($localize`Could not list the uploads: ${(error as Error).message}:reason:`);
        } finally {
            this.loading.set(false);
        }
    }

    protected rootName(): string {
        return this.config.libraryName();
    }

    protected keepHours(): number {
        return this.config.config()?.files?.keepUnfinishedUploadsHours ?? 48;
    }

    /** An upload's menu (a right click): Cancel, then the folder it goes into. */
    protected uploadMenu(item: UploadInfo): MenuTarget {
        return {
            kind: 'job',
            key: `upload-info:${item.id}`,
            name: baseName(item.name),
            entries: () => {
                const entries: MenuEntry[] =
                    this.busy() === null
                        ? [
                              {
                                  id: 'cancel',
                                  label: $localize`Cancel`,
                                  icon: 'close',
                                  title: this.cancelHint,
                                  run: () => void this.cancel(item),
                              },
                          ]
                        : [];
                return [...entries, ...this.folders.targetEntries(item.target)];
            },
        };
    }

    protected async cancel(item: UploadInfo): Promise<void> {
        const name = baseName(item.name);
        const owner = item.ownerName;
        if (!confirm($localize`Cancel the upload of “${name}:name:” by ${owner}:owner:? What it received is removed.`))
            return;
        try {
            this.busy.set(item.id);
            await this.api.cancelUpload(item.id);
            this.items.update((items) => items.filter((i) => i.id !== item.id));
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error($localize`Could not cancel the upload of “${name}:name:”: ${reason}:reason:`);

        } finally {
            this.busy.set(null);
        }
    }
}
