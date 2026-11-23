import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { icu } from '../core/i18n/icu';
import { MissingFileInfo } from './tracking.models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { TagChipComponent } from '../tags/tag-chip.component';
import { StatusBoxComponent, TrackingFileComponent } from './tracking-parts';
import { missingStatus, missingWas } from './tracking-texts';

/**
 * Tagged files scans no longer find: relink them to one of their candidates or another file, or forget them.
 * `changed` is emitted when tags moved to other files.
 */
@Component({
    selector: 'sfe-missing-panel',
    imports: [BusyDirective, IconComponent, StatusBoxComponent, TagChipComponent, TrackingFileComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel', role: 'tabpanel' },
    templateUrl: './missing-panel.component.html',
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

    protected readonly status = computed(() => missingStatus(this.total(), this.enabled(), this.failure()));
    /** For the tab. */
    readonly badge = computed(() => (this.total() ? this.total().toLocaleString() : ''));

    protected readonly was = missingWas;

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
            if (current !== this.generation) return;
            if (!append) {
                this.total.set(result.total ?? 0);
                this.enabled.set(result.enabled);
                this.failure.set(null);
            }
            this.items.update((items) => [...items, ...result.items]);
            this.next = result.next;
            this.hasMore.set(result.hasMore);
        } catch (error) {
            if (current === this.generation) this.failure.set((error as Error).message);
        } finally {
            if (current === this.generation) this.loading.set(false);
        }
    }

    protected more(): void {
        void whileBusy(this.loadingMore, () => this.load({ append: true }));
    }

    private removeItem(item: MissingFileInfo): void {
        this.items.update((items) => items.filter((other) => other !== item));
        this.total.update((total) => Math.max(0, total - 1));
    }

    protected async relink(item: MissingFileInfo, path: string, action = path): Promise<void> {
        this.busy.set(`${item.id}:${action}`);
        try {
            await this.api.relinkMissingFile(item.id, path);
            this.changed.emit();
            this.toasts.show($localize`The tags of “${item.name}:name:” moved to “${path}:path:”.`);
            this.removeItem(item);
        } catch (error) {
            const gone = error instanceof ApiError && error.status === 404;
            this.toasts.error(
                gone
                    ? $localize`This file was already relinked or forgotten.`
                    : $localize`Could not relink “${item.name}:name:”: ${(error as Error).message}:reason:`,
            );
            if (gone) this.removeItem(item);
        } finally {
            this.busy.set(null);
        }
    }

    protected choose(item: MissingFileInfo): void {
        const question = $localize`Which file gets the tags of “${item.name}:name:”? \
Type its path from the top of the library.`;
        const path = prompt(question, item.path);
        if (path?.trim()) void this.relink(item, path.trim().replace(/^\/+/, ''), 'choose');
    }

    protected async forget(item: MissingFileInfo): Promise<void> {
        if (!confirm($localize`Forget “${item.name}:name:” and its tags?`)) return;
        this.busy.set(`${item.id}:forget`);
        try {
            await this.api.forgetMissingFiles(item.id);
        } catch (error) {
            if (!(error instanceof ApiError && error.status === 404)) {
                const reason = (error as Error).message;
                this.toasts.error($localize`Could not forget “${item.name}:name:”: ${reason}:reason:`);
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
            if (result.relinked) this.changed.emit();
            this.toasts.show(
                result.relinked
                    ? icu(
                          $localize`{relinked, plural, one {# file relinked;} other {# files relinked;}} \
{remaining, plural, one {# file still missing.} other {# files still missing.}}`,
                          { relinked: result.relinked, remaining: result.remaining },
                      )
                    : $localize`No missing file has a single likely new place.`,
            );
        } catch (error) {
            this.toasts.error($localize`Could not relink the missing files: ${(error as Error).message}:reason:`);
        }
        void this.load();
    }

    protected async forgetAll(): Promise<void> {
        const question = icu(
            $localize`{count, plural, one {Forget the missing file and its tags?} \
other {Forget all # missing files and their tags?}}`,
            { count: this.total() },
        );
        if (!confirm(question)) return;
        try {
            await whileBusy(this.forgettingAll, () => this.api.forgetMissingFiles());
        } catch (error) {
            this.toasts.error($localize`Could not forget the missing files: ${(error as Error).message}:reason:`);
        }
        void this.load();
    }
}
