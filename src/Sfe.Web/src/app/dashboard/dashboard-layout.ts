import { ClientConfig } from '../core/config.models';
import { DashboardWidget, findWidget } from './dashboard-widgets';

/**
 * Where the dashboard's cards go: fixed columns, each a list of widget ids from top to bottom (no free grid). On a
 * phone the columns are shown one after the other.
 */
export interface DashboardLayout {
    columns: string[][];
}

/** The columns of every layout. */
export const DASHBOARD_COLUMNS = 2;

/** The layout of an account that has made none of its own. */
export const DEFAULT_LAYOUT: DashboardLayout = {
    columns: [
        ['files', 'places', 'notifications'],
        ['storage', 'uploads', 'connections', 'tags'],
    ],
};

/**
 * The widgets of `layout` this session is shown, column by column: ids no widget has (a layout saved by a later
 * version, a widget removed) and widgets it may not see are left out, each widget once, extra columns appended to
 * the last one.
 */
export function arrangeLayout(
    layout: DashboardLayout,
    can: (right: string) => boolean,
    config: ClientConfig | null = null,
): DashboardWidget[][] {
    const seen = new Set<string>();
    const columns: DashboardWidget[][] = Array.from({ length: DASHBOARD_COLUMNS }, () => []);
    layout.columns.forEach((ids, index) => {
        const column = columns[Math.min(index, DASHBOARD_COLUMNS - 1)];
        for (const id of ids) {
            const widget = findWidget(id);
            if (!widget || seen.has(id) || !widget.shown(can, config)) continue;
            seen.add(id);
            column.push(widget);
        }
    });
    return columns;
}
