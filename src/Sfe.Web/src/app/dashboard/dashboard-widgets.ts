import { Type } from '@angular/core';
import { ClientConfig } from '../core/config.models';
import { Rights } from '../core/rights';

/**
 * A card of the dashboard. Its code loads when a dashboard showing it opens, so the registry stays small; layouts
 * name it by `id`.
 */
export interface DashboardWidget {
    /** Its name in layouts (the default one, and those saved later): never renamed. */
    readonly id: string;
    /** The card's heading. */
    readonly title: string;
    readonly icon: string;
    /** Shown to this session: what it reads needs these rights (and the server, what `config` says it has). */
    shown(can: (right: string) => boolean, config: ClientConfig | null): boolean;
    /** The card's content, a component without inputs. */
    load(): Promise<Type<unknown>>;
}

/** Every widget the dashboard can show, in the order a list of them is offered. */
export const DASHBOARD_WIDGETS: readonly DashboardWidget[] = [
    {
        id: 'files',
        title: $localize`Files`,
        icon: 'folder',
        shown: (can) => can(Rights.browse),
        load: () => import('./widgets/files-widget.component').then((m) => m.FilesWidgetComponent),
    },
    {
        id: 'places',
        title: $localize`Home and quick links`,
        icon: 'home',
        shown: (can) => can(Rights.homeFolder) || can(Rights.quickLinks),
        load: () => import('./widgets/places-widget.component').then((m) => m.PlacesWidgetComponent),
    },
    {
        id: 'storage',
        title: $localize`Storage`,
        icon: 'storage',
        shown: (can) => can(Rights.browse),
        load: () => import('./widgets/storage-widget.component').then((m) => m.StorageWidgetComponent),
    },
    {
        id: 'uploads',
        title: $localize`Uploads`,
        icon: 'upload',
        shown: (can) => can(Rights.uploadFiles),
        load: () => import('./widgets/uploads-widget.component').then((m) => m.UploadsWidgetComponent),
    },
    {
        id: 'notifications',
        title: $localize`Notifications`,
        icon: 'bell',
        shown: (can) => can(Rights.browse),
        load: () => import('./widgets/notifications-widget.component').then((m) => m.NotificationsWidgetComponent),
    },
    {
        id: 'connections',
        title: $localize`Connections`,
        icon: 'globe',
        shown: (can, config) => Boolean(config?.connections) && can(Rights.useConnections),
        load: () => import('./widgets/connections-widget.component').then((m) => m.ConnectionsWidgetComponent),
    },
    {
        id: 'tags',
        title: $localize`Tag history`,
        icon: 'tag',
        shown: (can) => can(Rights.viewTags),
        load: () => import('./widgets/tag-history-widget.component').then((m) => m.TagHistoryWidgetComponent),
    },
    {
        id: 'scan',
        title: $localize`Index scan`,
        icon: 'scan',
        shown: (can) => can(Rights.scan) && can(Rights.browse),
        load: () => import('./widgets/scan-widget.component').then((m) => m.ScanWidgetComponent),
    },
    {
        id: 'tasks',
        title: $localize`Background tasks`,
        icon: 'clock',
        shown: (can) => can(Rights.viewTasks),
        load: () => import('./widgets/tasks-widget.component').then((m) => m.TasksWidgetComponent),
    },
    {
        id: 'keepAlive',
        title: $localize`Keep alive`,
        icon: 'refresh',
        shown: (can) => can(Rights.keepAliveSettings),
        load: () => import('./widgets/keep-alive-widget.component').then((m) => m.KeepAliveWidgetComponent),
    },
];

export function findWidget(id: string): DashboardWidget | undefined {
    return DASHBOARD_WIDGETS.find((widget) => widget.id === id);
}
