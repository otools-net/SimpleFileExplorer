import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { TagSummary, TagUsage } from '../../core/tag.models';
import { SidebarService } from '../../library/sidebar/sidebar.service';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { tagQuery } from '../../tags/tags';

interface HistoryList {
    key: 'searched' | 'viewed';
    title: string;
    usages: TagUsage[];
}

/**
 * The dashboard's Tag history: the tags last searched and those of the files last viewed, as the sidebar lists them
 * (`api/tags/sidebar`); a tag finds its files. Says so when the history is switched off.
 */
@Component({
    selector: 'sfe-tag-history-widget',
    imports: [TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tag-history-widget' },
    template: `
        @if (sidebar.data(); as data) {
            @if (!data.historyEnabled) {
                <p class="dashboard-card-note" i18n>The tag history is switched off (Administration › Tags).</p>
            } @else {
                @for (list of lists(); track list.key) {
                    <h3 class="dashboard-card-subtitle">{{ list.title }}</h3>
                    <ul class="sidebar-tags" [attr.data-history]="list.key">
                        @for (usage of list.usages; track usage.tag.id) {
                            <li>
                                <sfe-tag-chip [tag]="usage.tag" [title]="findHint(usage.tag)" [clickable]="true"
                                              (pick)="search($event)" />
                            </li>
                        } @empty {
                            <li class="dashboard-card-note" i18n>None yet.</li>
                        }
                    </ul>
                }
            }
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class TagHistoryWidgetComponent implements OnInit {
    protected readonly sidebar = inject(SidebarService);
    private readonly router = inject(Router);

    protected readonly lists = computed<HistoryList[]>(() => {
        const data = this.sidebar.data();
        return [
            { key: 'searched', title: $localize`Last searched`, usages: data?.searched ?? [] },
            { key: 'viewed', title: $localize`Last viewed`, usages: data?.viewed ?? [] },
        ];
    });

    ngOnInit(): void {
        void this.sidebar.reload();
    }

    protected findHint(tag: TagSummary): string {
        return $localize`Find the files tagged “${tag.displayName}:tag:”.`;
    }

    protected search(tag: TagSummary): void {
        void this.router.navigate(['/'], { queryParams: { q: tagQuery(tag) } });
    }
}
