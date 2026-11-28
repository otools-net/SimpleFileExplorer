import { Type } from '@angular/core';
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
    /** Shown to this session: what it reads needs these rights. */
    shown(can: (right: string) => boolean): boolean;
    /** The card's content, a component without inputs. */
    load(): Promise<Type<unknown>>;
}

/** Every widget the dashboard can show, in the order a list of them is offered. */
export const DASHBOARD_WIDGETS: readonly DashboardWidget[] = [
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
];

export function findWidget(id: string): DashboardWidget | undefined {
    return DASHBOARD_WIDGETS.find((widget) => widget.id === id);
}
