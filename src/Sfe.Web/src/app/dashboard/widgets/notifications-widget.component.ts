import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { icu } from '../../core/i18n/icu';
import { Notifications } from '../../core/notifications';
import { formatRelative } from '../../tasks/tasks';

/** Notifications the card lists; the bell lists them all. */
export const NOTIFICATIONS_SHOWN = 5;

/**
 * The dashboard's Notifications: the latest messages the notification centre keeps for this browser (newest
 * first), read only: the bell of the header acts on them (their actions, removing, clearing).
 */
@Component({
    selector: 'sfe-notifications-widget',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'notifications-widget' },
    template: `
        <ul class="dashboard-list">
            @for (item of latest(); track item.id) {
                <li class="dashboard-notification notification" [class]="item.level" [class.unread]="!item.read">
                    <span class="notification-text">{{ item.message }}</span>
                    <time class="dashboard-card-note" [attr.datetime]="iso(item.at)" [title]="exact(item.at)">{{
                        ago(item.at)
                    }}</time>
                </li>
            }
        </ul>
        @if (more(); as more) {
            <p class="dashboard-card-note">{{ more }}</p>
        } @else if (!latest().length) {
            <p class="dashboard-card-note" i18n>No notifications. Messages about what you do show here.</p>
        }
    `,
})
export class NotificationsWidgetComponent {
    private readonly notifications = inject(Notifications);

    protected readonly latest = computed(() => this.notifications.list().slice(0, NOTIFICATIONS_SHOWN));
    protected readonly more = computed(() => {
        const count = this.notifications.list().length - NOTIFICATIONS_SHOWN;
        return count > 0
            ? icu($localize`{count, plural, one {# more} other {# more}}: the bell above lists them all.`, { count })
            : null;
    });

    protected ago(at: number): string {
        return formatRelative(this.iso(at));
    }

    protected iso(at: number): string {
        return new Date(at).toISOString();
    }

    protected exact(at: number): string {
        return new Date(at).toLocaleString();
    }
}
