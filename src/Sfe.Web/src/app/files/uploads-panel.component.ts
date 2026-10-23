import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { formatSize, joinParts, plural } from '../core/format';
import { baseName } from '../core/paths';
import { checkedOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { FolderMenu } from '../library/folder-menu';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../menus/context-menu';
import { ConfigService } from '../core/config.service';
import { UploadBatch, UploadItem, UploadQueue } from './uploads';

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
    template: `
        @for (batch of queue.batches(); track batch.key) {
            <section class="operation upload" [class.done]="!active(batch)" [attr.data-upload]="batch.key" aria-live="polite" [sfeContextMenu]="batchMenu(batch)">
                <header class="operation-head">
                    <svg sfeIcon="upload"></svg>
                    <span class="operation-title">{{ title(batch) }}</span>
                    @if (active(batch)) {
                        @if (paused(batch)) {
                            <button type="button" class="button small" title="Go on sending them" (click)="queue.resumeAll(batch)">Resume</button>
                        } @else if (sending(batch)) {
                            <button type="button" class="button small" title="Stop sending for now: they go on where they stopped" (click)="queue.pauseAll(batch)">Pause</button>
                        }
                        <button type="button" class="button small" title="Stop: what was not finished is removed from the server" (click)="queue.cancelAll(batch)">Cancel</button>
                    } @else {
                        <button type="button" class="icon-button ghost small" aria-label="Close" title="Close" (click)="queue.dismiss(batch)">
                            <svg sfeIcon="close"></svg>
                        </button>
                    }
                </header>
                @if (sending(batch) || paused(batch)) {
                    <progress [max]="Math.max(1, total(batch))" [value]="sent(batch)"></progress>
                    <p class="operation-status">{{ status(batch) }}</p>
                }
                @if (queue.question(batch); as asked) {
                    <div class="operation-conflict" role="group" aria-label="Name taken">
                        <p>“{{ baseName(asked.name) }}” is already there.</p>
                        <div class="operation-choices">
                            @if (batch.canReplace) {
                                <button type="button" class="button small danger" title="The file there goes to the trash" (click)="answer(batch, asked, 'replace')">Replace</button>
                            }
                            <button type="button" class="button small" title="Don't upload this one" (click)="answer(batch, asked, 'skip')">Skip</button>
                            <button type="button" class="button small primary" title="Keep both: this one gets a number added to its name" (click)="answer(batch, asked, 'keepBoth')">Keep both</button>
                        </div>
                        @if (asking(batch) > 1) {
                            <label class="check">
                                <input type="checkbox" [checked]="forAll()" (change)="forAll.set(checked($event))">
                                <span>The same for the {{ asking(batch) - 1 }} other names taken</span>
                            </label>
                        }
                    </div>
                }
                @if (shown(batch); as files) {
                    @if (files.length) {
                        <ul class="upload-files">
                            @for (item of files; track item.key) {
                                <li class="upload-file" [attr.data-state]="item.state">
                                    <span class="upload-name" [title]="item.name">{{ baseName(item.name) }}</span>
                                    <span class="upload-state">{{ stateText(item) }}</span>
                                    @if (item.state === 'failed' && item.file) {
                                        <button type="button" class="button small" title="Try again from where it stopped" (click)="queue.resumeItem(item)">Retry</button>
                                    } @else if (item.state === 'paused') {
                                        <button type="button" class="button small" title="Go on sending it" (click)="queue.resumeItem(item)">Resume</button>
                                    } @else if (item.state === 'uploading' || item.state === 'queued') {
                                        <button type="button" class="button small" title="Stop sending it for now" (click)="queue.pause(item)">Pause</button>
                                    }
                                    @if (!['done', 'skipped', 'cancelled'].includes(item.state)) {
                                        <button type="button" class="icon-button ghost small" [attr.aria-label]="'Cancel ' + item.name" title="Cancel this file" (click)="queue.cancel(item)">
                                            <svg sfeIcon="close"></svg>
                                        </button>
                                    }
                                </li>
                            }
                        </ul>
                        @if (hidden(batch) > 0) {
                            <p class="operation-status">and {{ plural(hidden(batch), 'more file') }}</p>
                        }
                    }
                }
                @if (interrupted(batch)) {
                    <p class="field-hint">Interrupted: pick or drop the same files into this folder again to go on where they stopped.</p>
                }
            </section>
        }
    `,
})
export class UploadsPanelComponent {
    protected readonly queue = inject(UploadQueue);
    private readonly config = inject(ConfigService);
    private readonly folders = inject(FolderMenu);

    protected readonly forAll = signal(false);
    protected readonly baseName = baseName;
    protected readonly plural = plural;
    protected readonly Math = Math;

    /** An upload's menu (a right click): its buttons (Pause / Resume, Cancel, or Close once done), then the folder it goes into. */
    protected batchMenu(batch: UploadBatch): MenuTarget {
        return {
            kind: 'job', key: `upload:${batch.key}`, name: this.title(batch),
            entries: () => {
                const entries: MenuEntry[] = [];
                if (this.active(batch)) {
                    if (this.paused(batch))
                        entries.push({ id: 'resume', label: 'Resume', icon: 'play', title: 'Go on sending them', run: () => this.queue.resumeAll(batch) });
                    else if (this.sending(batch))
                        entries.push({ id: 'pause', label: 'Pause', icon: 'pause', title: 'Stop sending for now: they go on where they stopped', run: () => this.queue.pauseAll(batch) });
                    entries.push({ id: 'cancel', label: 'Cancel', icon: 'close', title: 'Stop: what was not finished is removed from the server', run: () => void this.queue.cancelAll(batch) });
                } else
                    entries.push({ id: 'close', label: 'Close', icon: 'close', title: 'Take it off the list', run: () => this.queue.dismiss(batch) });
                return [...entries, ...this.folders.targetEntries(batch.target)];
            },
        };
    }

    protected active(batch: UploadBatch): boolean {
        return batch.items.some(item => !['done', 'skipped', 'cancelled', 'failed'].includes(item.state));
    }

    protected sending(batch: UploadBatch): boolean {
        return batch.items.some(item => item.state === 'uploading' || item.state === 'queued');
    }

    protected paused(batch: UploadBatch): boolean {
        return !this.sending(batch) && batch.items.some(item => item.state === 'paused');
    }

    protected interrupted(batch: UploadBatch): boolean {
        return batch.items.some(item => item.state === 'interrupted');
    }

    protected asking(batch: UploadBatch): number {
        return batch.items.filter(item => item.state === 'asking').length;
    }

    protected total(batch: UploadBatch): number {
        return this.counted(batch).reduce((sum, item) => sum + item.size, 0);
    }

    protected sent(batch: UploadBatch): number {
        return this.counted(batch).reduce((sum, item) => sum + item.sent, 0);
    }

    protected title(batch: UploadBatch): string {
        const where = `“${baseName(batch.target) || this.config.config()?.rootName || 'the library'}”`;
        const done = batch.items.filter(item => item.state === 'done').length;
        if (this.active(batch))
            return `Uploading ${plural(this.counted(batch).length, 'file')} to ${where}`;
        const failed = batch.items.filter(item => item.state === 'failed').length;
        return joinParts([`${plural(done, 'file')} uploaded to ${where}`, failed ? `${failed} failed` : null], ', ');
    }

    protected status(batch: UploadBatch): string {
        const counted = this.counted(batch);
        const done = counted.filter(item => item.state === 'done').length;
        const seconds = (Date.now() - batch.started) / 1000;
        const speed = seconds > 1 && batch.sentHere ? `${formatSize(batch.sentHere / seconds)}/s` : null;
        return joinParts([`${done} of ${counted.length}`, `${formatSize(this.sent(batch))} of ${formatSize(this.total(batch))}`,
            this.paused(batch) ? 'paused' : speed]);
    }

    protected stateText(item: UploadItem): string {
        switch (item.state) {
            case 'uploading': return item.size ? `${Math.floor(item.sent * 100 / item.size)}%` : '…';
            case 'queued': return 'waiting';
            case 'asking': return 'name taken';
            case 'paused': return `paused at ${item.size ? Math.floor(item.sent * 100 / item.size) : 0}%`;
            case 'interrupted': return `${item.size ? Math.floor(item.sent * 100 / item.size) : 0}%, interrupted`;
            case 'failed': return item.message ?? 'failed';
            default: return item.state;
        }
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
        return batch.items.filter(item => order.includes(item.state)).sort((a, b) => order.indexOf(a.state) - order.indexOf(b.state));
    }

    /** What the progress counts: the files not left out. */
    private counted(batch: UploadBatch): UploadItem[] {
        return batch.items.filter(item => item.state !== 'skipped' && item.state !== 'cancelled');
    }
}
