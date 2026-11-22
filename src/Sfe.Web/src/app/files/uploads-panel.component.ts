import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { baseName } from '../core/paths';
import { checkedOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { FolderMenu } from '../library/folder-menu';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../menus/context-menu';
import { ConfigService } from '../core/config.service';
import { UploadBatch, UploadItem, UploadQueue } from './uploads';
import { quoted } from './file-dialog-texts';
import { cancelFileLabel, uploadState, uploadStatus, uploadTitle } from './upload-texts';

/** Files listed at most per batch (the busiest first); the others are counted. */
const FILES_SHOWN = 6;

/**
 * The uploads of the page, in the operations panel: per batch the progress and speed, the question about a name taken
 * (with "the same for the rest"), and the files under way with pause, resume, retry and cancel; uploads a reload
 * interrupted wait for their file to be picked or dropped again.
 */
@Component({
    selector: 'sfe-uploads-panel',
    imports: [IconComponent, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'uploads-panel', '[hidden]': '!queue.batches().length' },
    templateUrl: './uploads-panel.component.html',
})
export class UploadsPanelComponent {
    protected readonly queue = inject(UploadQueue);
    private readonly config = inject(ConfigService);
    private readonly folders = inject(FolderMenu);

    protected readonly forAll = signal(false);
    protected readonly baseName = baseName;
    protected readonly Math = Math;
    protected readonly cancelFileLabel = cancelFileLabel;
    protected readonly stateText = uploadState;
    protected readonly hints = {
        resume: $localize`Go on sending them.`,
        pause: $localize`Stop sending them for now. They go on where they stopped.`,
        cancel: $localize`Stop. What was not finished is removed from the server.`,
        close: $localize`Take it off the list.`,
    };

    /**
     * An upload's menu (a right click): its buttons (Pause / Resume, Cancel, or Close once done), then the folder it
     * goes into.
     */
    protected batchMenu(batch: UploadBatch): MenuTarget {
        return {
            kind: 'job',
            key: `upload:${batch.key}`,
            name: this.title(batch),
            entries: () => {
                const entries: MenuEntry[] = [];
                if (this.active(batch)) {
                    if (this.paused(batch))
                        entries.push({
                            id: 'resume',
                            label: $localize`Resume`,
                            icon: 'play',
                            title: this.hints.resume,
                            run: () => this.queue.resumeAll(batch),
                        });
                    else if (this.sending(batch))
                        entries.push({
                            id: 'pause',
                            label: $localize`Pause`,
                            icon: 'pause',
                            title: this.hints.pause,
                            run: () => this.queue.pauseAll(batch),
                        });
                    entries.push({
                        id: 'cancel',
                        label: $localize`Cancel`,
                        icon: 'close',
                        title: this.hints.cancel,
                        run: () => void this.queue.cancelAll(batch),
                    });
                } else
                    entries.push({
                        id: 'close',
                        label: $localize`Close`,
                        icon: 'close',
                        title: this.hints.close,
                        run: () => this.queue.dismiss(batch),
                    });
                return [...entries, ...this.folders.targetEntries(batch.target)];
            },
        };
    }

    protected active(batch: UploadBatch): boolean {
        return batch.items.some((item) => !['done', 'skipped', 'cancelled', 'failed'].includes(item.state));
    }

    protected sending(batch: UploadBatch): boolean {
        return batch.items.some((item) => item.state === 'uploading' || item.state === 'queued');
    }

    protected paused(batch: UploadBatch): boolean {
        return !this.sending(batch) && batch.items.some((item) => item.state === 'paused');
    }

    protected interrupted(batch: UploadBatch): boolean {
        return batch.items.some((item) => item.state === 'interrupted');
    }

    protected asking(batch: UploadBatch): number {
        return batch.items.filter((item) => item.state === 'asking').length;
    }

    protected total(batch: UploadBatch): number {
        return this.counted(batch).reduce((sum, item) => sum + item.size, 0);
    }

    protected sent(batch: UploadBatch): number {
        return this.counted(batch).reduce((sum, item) => sum + item.sent, 0);
    }

    protected title(batch: UploadBatch): string {
        const where = quoted(baseName(batch.target) || this.config.libraryName());
        const done = batch.items.filter((item) => item.state === 'done').length;
        const failed = batch.items.filter((item) => item.state === 'failed').length;
        return uploadTitle(this.active(batch), this.counted(batch).length, done, failed, where);
    }

    protected status(batch: UploadBatch): string {
        const counted = this.counted(batch);
        const done = counted.filter((item) => item.state === 'done').length;
        const seconds = (Date.now() - batch.started) / 1000;
        return uploadStatus(
            { done, total: counted.length },
            { sent: this.sent(batch), total: this.total(batch) },
            this.paused(batch),
            seconds > 1 && batch.sentHere ? batch.sentHere / seconds : null,
        );
    }

    /** The files worth a line: those under way, waiting, failed or interrupted (not those done). */
    protected shown(batch: UploadBatch): UploadItem[] {
        return this.lines(batch).slice(0, FILES_SHOWN);
    }

    protected hidden(batch: UploadBatch): number {
        return Math.max(0, this.lines(batch).length - FILES_SHOWN);
    }

    protected answer(batch: UploadBatch, item: UploadItem, choice: 'replace' | 'skip' | 'keepBoth'): void {
        this.queue.answer(batch, item, choice, this.forAll());
        this.forAll.set(false);
    }

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    private lines(batch: UploadBatch): UploadItem[] {
        const order = ['uploading', 'failed', 'paused', 'interrupted', 'asking', 'queued'];
        return batch.items
            .filter((item) => order.includes(item.state))
            .sort((a, b) => order.indexOf(a.state) - order.indexOf(b.state));
    }

    /** What the progress counts: the files not left out. */
    private counted(batch: UploadBatch): UploadItem[] {
        return batch.items.filter((item) => item.state !== 'skipped' && item.state !== 'cancelled');
    }
}
