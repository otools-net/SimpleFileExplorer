import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { formatDate, formatSize, plural } from '../core/format';
import { MissingFileInfo } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { TagChipComponent } from '../tags/tag-chip.component';
import { StatusBoxComponent, TrackingFileComponent } from './tracking-parts';

/**
 * Tagged files scans no longer find: relink them to one of their candidates or another file, or forget them.
 * `changed` is emitted when tags moved to other files.
 */
@Component({
    selector: 'sfe-missing-panel',
    imports: [BusyDirective, IconComponent, StatusBoxComponent, TagChipComponent, TrackingFileComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel', role: 'tabpanel' },
    template: `
        <p class="settings-intro">Tagged files that a scan no longer found. When a scan indexes the same file somewhere else, moved, renamed or back on its drive, its tags follow it on their own. Files with several possible new places, or none yet, wait here.</p>
        <sfe-status-box [state]="status().state" [text]="status().text" [detail]="status().detail">
            <div class="settings-action inline">
                <button type="button" class="button small" title="Give the tags of each missing file with exactly one likely new place to that file"
                        [sfeBusy]="relinking()" [disabled]="relinking() || !enabled() || !total()" (click)="relinkAll()">Relink now</button>
                <button type="button" class="button small danger" title="Forget every missing file and its tags (asks first)"
                        [sfeBusy]="forgettingAll()" [disabled]="forgettingAll() || !total()" (click)="forgetAll()">Forget all</button>
            </div>
        </sfe-status-box>
        <ul class="tracking-missing-list">
            @if (loading()) {
                <li class="tag-loading"><span class="spinner"></span></li>
            }
            @for (item of items(); track item.id) {
                <li class="tracking-missing">
                    <div class="tracking-missing-head">
                        <span class="tracking-thumb"><svg sfeIcon="file"></svg></span>
                        <span class="tracking-file">
                            <strong class="tracking-name" [title]="item.path">{{ item.name }}</strong>
                            <span class="tracking-was">{{ was(item) }}</span>
                            @if (item.tags.length) {
                                <span class="tracking-tags">
                                    @for (tag of item.tags; track tag.id) {
                                        <sfe-tag-chip [tag]="tag" />
                                    }
                                </span>
                            }
                        </span>
                        <span class="tracking-actions">
                            <button type="button" class="button small" title="Type the path of the file it became" [sfeBusy]="busy() === item.id + ':choose'" [disabled]="busy() !== null" (click)="choose(item)">Choose file…</button>
                            <button type="button" class="button small" title="Forget this missing file and its tags (asks first)" [sfeBusy]="busy() === item.id + ':forget'" [disabled]="busy() !== null" (click)="forget(item)">Forget</button>
                        </span>
                    </div>
                    @if (item.candidates.length) {
                        <div class="tracking-candidates">
                            <p class="tracking-sub">{{ item.candidates.length === 1 ? 'Possible new place' : 'Possible new places' }}</p>
                            <ul class="tracking-list">
                                @for (candidate of item.candidates; track candidate.path) {
                                    <li [sfeTrackingFile]="candidate" (openFolder)="openFolder.emit($event)">
                                        <span class="tracking-actions">
                                            <button type="button" class="button small primary" title="This is the moved file: give it the tags"
                                                    [sfeBusy]="busy() === item.id + ':' + candidate.path" [disabled]="busy() !== null" (click)="relink(item, candidate.path)">Relink here</button>
                                        </span>
                                    </li>
                                }
                            </ul>
                        </div>
                    } @else {
                        <p class="tracking-sub">No identical file is indexed. Scan the folder it went to, or choose the file.</p>
                    }
                </li>
            }
        </ul>
        <button type="button" class="button" title="Show the next page" [hidden]="!hasMore()" [sfeBusy]="loadingMore()" [disabled]="loadingMore()" (click)="more()">Load more</button>
    `,
})
export class MissingPanelComponent {
    readonly openFolder = output<string>();
    readonly changed = output<void>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    protected readonly items = signal<MissingFileInfo[]>([]);
    protected readonly total = signal(0);
    protected readonly enabled = signal(true);
    protected readonly hasMore = signal(false);
    protected readonly loading = signal(false);
    protected readonly loadingMore = signal(false);
    protected readonly relinking = signal(false);
    protected readonly forgettingAll = signal(false);
    /** `{id}:{action}` of the request in flight on one item. */
    protected readonly busy = signal<string | null>(null);
    private readonly failure = signal<string | null>(null);
    private next: number | null = null;
    private generation = 0;

    protected readonly status = computed(() => {
        const failure = this.failure();
        if (failure)
            return { state: 'unavailable', text: `Could not read the missing files: ${failure}`, detail: '' };
        const total = this.total();
        return {
            state: !this.enabled() ? 'unavailable' : total ? 'found' : 'idle',
            text: total ? `${plural(total, 'tagged file')} missing` : 'No tagged file is missing.',
            detail: this.enabled()
                ? 'Relinking runs after every scan; “Relink now” runs it again, for instance after scanning by hand.'
                : 'Keeping the tags of moved files is turned off (see the Settings tab): files no longer found lose their tags.',
        };
    });
    /** For the tab. */
    readonly badge = computed(() => this.total() ? this.total().toLocaleString() : '');

    protected was(item: MissingFileInfo): string {
        const slash = item.path.lastIndexOf('/');
        return `Was in ${slash > 0 ? item.path.slice(0, slash) : 'the library root'} · ${formatSize(item.size)} · missing since ${formatDate(item.missingSince)}`;
    }

    async load({ append = false } = {}): Promise<void> {
        const current = append ? this.generation : ++this.generation;
        if (!append) {
            this.items.set([]);
            this.hasMore.set(false);
            this.loading.set(true);
            this.next = null;
        }
        try {
            const result = await this.api.missingFiles({ after: append ? this.next : null });
            if (current !== this.generation)
                return;
            if (!append) {
                this.total.set(result.total ?? 0);
                this.enabled.set(result.enabled);
                this.failure.set(null);
            }
            this.items.update(items => [...items, ...result.items]);
            this.next = result.next;
            this.hasMore.set(result.hasMore);
        } catch (error) {
            if (current === this.generation)
                this.failure.set((error as Error).message);
        } finally {
            if (current === this.generation)
                this.loading.set(false);
        }
    }

    protected more(): void {
        void whileBusy(this.loadingMore, () => this.load({ append: true }));
    }

    private removeItem(item: MissingFileInfo): void {
        this.items.update(items => items.filter(other => other !== item));
        this.total.update(total => Math.max(0, total - 1));
    }

    protected async relink(item: MissingFileInfo, path: string, action = path): Promise<void> {
        this.busy.set(`${item.id}:${action}`);
        try {
            await this.api.relinkMissingFile(item.id, path);
            this.changed.emit();
            this.toasts.show(`Tags of “${item.name}” moved to “${path}”.`);
            this.removeItem(item);
        } catch (error) {
            const gone = error instanceof ApiError && error.status === 404;
            this.toasts.error(gone ? 'This file was already relinked or forgotten.' : `Could not relink: ${(error as Error).message}`);
            if (gone)
                this.removeItem(item);
        } finally {
            this.busy.set(null);
        }
    }

    protected choose(item: MissingFileInfo): void {
        const path = prompt(`Give the tags of “${item.name}” to which file? Path from the library root:`, item.path);
        if (path?.trim())
            void this.relink(item, path.trim().replace(/^\/+/, ''), 'choose');
    }

    protected async forget(item: MissingFileInfo): Promise<void> {
        if (!confirm(`Forget “${item.name}” and its tags?`))
            return;
        this.busy.set(`${item.id}:forget`);
        try {
            await this.api.forgetMissingFiles(item.id);
        } catch (error) {
            if (!(error instanceof ApiError && error.status === 404)) {
                this.toasts.error(`Could not forget it: ${(error as Error).message}`);
                return;
            }
        } finally {
            this.busy.set(null);
        }
        this.removeItem(item);
    }

    protected async relinkAll(): Promise<void> {
        try {
            const result = await whileBusy(this.relinking, () => this.api.relinkMissingFiles());
            if (result.relinked)
                this.changed.emit();
            this.toasts.show(result.relinked
                ? `${plural(result.relinked, 'file')} relinked; ${plural(result.remaining, 'file')} still missing.`
                : 'No missing file has a single likely new place.');
        } catch (error) {
            this.toasts.error(`Could not relink: ${(error as Error).message}`);
        }
        void this.load();
    }

    protected async forgetAll(): Promise<void> {
        if (!confirm(`Forget all ${plural(this.total(), 'missing file')} and their tags?`))
            return;
        try {
            await whileBusy(this.forgettingAll, () => this.api.forgetMissingFiles());
        } catch (error) {
            this.toasts.error(`Could not forget them: ${(error as Error).message}`);
        }
        void this.load();
    }
}
