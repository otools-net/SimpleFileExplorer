import {
    ChangeDetectionStrategy,
    Component,
    computed,
    effect,
    ElementRef,
    inject,
    signal,
    viewChild,
} from '@angular/core';
import { NotificationView, Notifications } from '../core/notifications';
import { ToastAction, ToastLevel, ToastService } from '../core/toast.service';
import { Popovers } from '../library/topbar/popovers.service';
import { IconComponent } from '../shared/icon.component';
import { formatRelative } from '../tasks/tasks';

const LEVEL_NAMES: Record<ToastLevel, string> = {
    info: '',
    success: $localize`:a notification's kind, read before it:Done`,
    warning: $localize`:a notification's kind, read before it:Warning`,
    error: $localize`:a notification's kind, read before it:Error`,
};

/**
 * The bell of the top bars (the library's, the administration's) and the notification centre it opens: the toasts
 * kept by `Notifications`, newest first, with their buttons. The count on the bell is what is unread; opening the
 * list reads it all (what was unread stays marked while it is open).
 */
@Component({
    selector: 'sfe-notifications',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'menu notifications',
        '(document:pointerdown)': 'onOutside($event)',
        '(document:keydown.escape)': 'close()',
    },
    templateUrl: './notifications.component.html',
})
export class NotificationsComponent {
    protected readonly notifications = inject(Notifications);
    private readonly toasts = inject(ToastService);
    private readonly popovers = inject(Popovers);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    protected readonly levelNames = LEVEL_NAMES;

    protected readonly isOpen = computed(() => this.popovers.open() === 'notifications');
    protected readonly list = this.notifications.list;
    protected readonly unread = this.notifications.unread;
    /** What was unread when the list opened. */
    protected wasUnread = new Set<string>();
    protected readonly buttonLabel = computed(() => {
        const unread = this.unread();
        return unread
            ? $localize`Notifications: ${unread}:count: unread`
            : $localize`Notifications: the messages of the last 30 days`;
    });

    /** Where the list opens: under the bell, its right edge with the bell's (the top bar stays in place). */
    protected readonly place = signal({ top: 0, right: 0 });
    private readonly layer = viewChild<ElementRef<HTMLElement>>('layer');

    constructor() {
        // Browsers without the popover API (and jsdom) keep it in place, where it works but for phones' sheet.
        effect(() => {
            const layer = this.layer()?.nativeElement;
            if (layer && typeof layer.showPopover === 'function' && !layer.matches(':popover-open'))
                layer.showPopover();
        });
    }

    protected toggle(): void {
        if (!this.isOpen()) {
            this.wasUnread = new Set(this.list().flatMap((item) => (item.read ? [] : [item.id])));
            this.notifications.markAllRead();
            const bell = this.host.getBoundingClientRect();
            this.place.set({ top: bell.bottom + 8, right: document.documentElement.clientWidth - bell.right });
        }
        this.popovers.toggle('notifications');
    }

    protected close(): void {
        if (this.isOpen()) this.popovers.close();
    }

    protected onOutside(event: PointerEvent): void {
        if (this.isOpen() && !this.host.contains(event.target as Node)) this.popovers.close();
    }

    /** Its buttons that can still be used. */
    protected usable(item: NotificationView): ToastAction[] {
        return item.actions.filter((action) => !action.gone?.());
    }

    protected perform(action: ToastAction): void {
        this.popovers.close();
        this.toasts.perform(action);
    }

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
