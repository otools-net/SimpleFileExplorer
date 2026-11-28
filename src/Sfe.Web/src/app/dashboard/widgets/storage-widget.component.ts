import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { StorageUsage } from '../../library/library.models';
import { storageHint, storageLevel, storageLine, usedShare } from '../../library/sidebar/storage-texts';

/**
 * The dashboard's Storage: the space used and free on the disk holding the library's top folder, as the sidebar's
 * bar shows it (orange from 85 % used, red from 95 %). Read once when the dashboard opens.
 */
@Component({
    selector: 'sfe-storage-widget',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'storage-widget' },
    template: `
        @if (usage(); as usage) {
            <div class="storage-bar" [class.nearly-full]="level() === 'nearly-full'" [class.full]="level() === 'full'"
                 [title]="hint()">
                @if (percent(); as percent) {
                    <div class="storage-meter" role="meter" aria-valuemin="0" aria-valuemax="100"
                         [attr.aria-valuenow]="percent" [attr.aria-label]="hint()">
                        <span [style.width.%]="percent"></span>
                    </div>
                }
                <span class="storage-line">{{ line() }}</span>
            </div>
        } @else if (failed()) {
            <p class="dashboard-card-note" i18n>The space used could not be read.</p>
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class StorageWidgetComponent implements OnInit {
    private readonly api = inject(Api);

    protected readonly usage = signal<StorageUsage | null>(null);
    protected readonly failed = signal(false);
    private readonly share = computed(() => {
        const usage = this.usage();
        return usage ? usedShare(usage) : null;
    });
    protected readonly level = computed(() => storageLevel(this.share()));
    protected readonly percent = computed(() => {
        const share = this.share();
        return share === null ? null : Math.max(1, Math.round(share * 100));
    });
    protected readonly line = computed(() => {
        const usage = this.usage();
        return usage ? storageLine(usage) : '';
    });
    protected readonly hint = computed(() => {
        const usage = this.usage();
        return usage ? storageHint(usage, null) : '';
    });

    async ngOnInit(): Promise<void> {
        try {
            this.usage.set(await this.api.storage(null, ''));
        } catch {
            this.failed.set(true);
        }
    }
}
