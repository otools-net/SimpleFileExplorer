import { NgComponentOutlet } from '@angular/common';
import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    OnInit,
    signal,
    Type,
    WritableSignal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { visibleSections } from '../admin/admin-sections';
import { redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ThemeService } from '../core/theme.service';
import { Popovers } from '../library/topbar/popovers.service';
import { NotificationsComponent } from '../notifications/notifications.component';
import { EmptyStateComponent } from '../shared/empty-state.component';
import { IconComponent } from '../shared/icon.component';
import { arrangeLayout, DEFAULT_LAYOUT } from './dashboard-layout';
import { DashboardWidget } from './dashboard-widgets';

/** A card of the page: its widget and, once its code has loaded, its content. */
interface Card {
    widget: DashboardWidget;
    content: WritableSignal<Type<unknown> | null>;
    /** Its code could not be fetched (the server was updated meanwhile). */
    failed: WritableSignal<boolean>;
}

/**
 * `dashboard`: the start page unless the user chose another (`browse.startPage`, dashboard/start-page.ts). Cards
 * from the widget registry in the layout's fixed columns (one column on phones), each card's code loaded when the
 * page opens.
 */
@Component({
    selector: 'sfe-dashboard-page',
    imports: [NgComponentOutlet, RouterLink, IconComponent, NotificationsComponent, EmptyStateComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'dashboard-page', '(document:pointerdown)': 'closePopovers($event)' },
    templateUrl: './dashboard-page.component.html',
})
export class DashboardPageComponent implements OnInit {
    private readonly config = inject(ConfigService);
    private readonly theme = inject(ThemeService);
    private readonly title = inject(Title);
    private readonly popovers = inject(Popovers);

    protected readonly problem = signal<string | null>(null);
    protected readonly columns = signal<Card[][] | null>(null);
    protected readonly empty = computed(() => this.columns()?.every((column) => column.length === 0) ?? false);
    protected readonly adminVisible = computed(() => {
        const rights = this.config.rights();
        return visibleSections((any) => any.some((right) => rights.has(right))).length > 0;
    });
    protected readonly libraryName = this.config.libraryName;

    async ngOnInit(): Promise<void> {
        try {
            const config = this.config.config() ?? (await this.config.load());
            this.theme.setDefault(config.theme);
        } catch (error) {
            if (!redirected(error)) this.problem.set((error as Error).message);
            return;
        }
        this.title.setTitle($localize`Dashboard – SFE`);
        const columns = arrangeLayout(DEFAULT_LAYOUT, (right) => this.config.can(right)).map((column) =>
            column.map((widget) => ({ widget, content: signal<Type<unknown> | null>(null), failed: signal(false) })),
        );
        this.columns.set(columns);
        for (const card of columns.flat()) void this.loadCard(card);
    }

    /** A click outside the popovers (the notifications) and the buttons opening them closes them. */
    protected closePopovers(event: PointerEvent): void {
        if (!(event.target as Element).closest?.('.popover, [aria-expanded]')) this.popovers.close();
    }

    private async loadCard(card: Card): Promise<void> {
        try {
            card.content.set(await card.widget.load());
        } catch {
            card.failed.set(true);
        }
    }
}
