/*
 * The wording and the filters of the Logs page: what the boxes hold, turned into the query of api/logs.
 */

import { LogLevelName, LogQuery } from '../core/models';

/** From the least to the most severe; the filter lists them in this order. */
export const LOG_LEVELS: LogLevelName[] = ['trace', 'debug', 'information', 'warning', 'securityWarning', 'error', 'critical'];

export const LEVEL_NAMES: Record<LogLevelName, string> = {
    trace: 'Trace',
    debug: 'Debug',
    information: 'Info',
    warning: 'Warning',
    securityWarning: 'Security warning',
    error: 'Error',
    critical: 'Critical',
};

/** All spaces (server administrators), only the server's own entries, or one space by id. */
export type SpaceFilter = 'all' | 'server' | number;

export interface LogFilters {
    text: string;
    /** None ticked = every level. */
    levels: ReadonlySet<LogLevelName>;
    /** Local days, as a date box gives them (yyyy-mm-dd); empty = no bound. */
    fromDay: string;
    toDay: string;
    space: SpaceFilter;
}

export const NO_FILTERS: LogFilters = { text: '', levels: new Set(), fromDay: '', toDay: '', space: 'all' };

/** The start of a local day (yyyy-mm-dd), `days` later, as an ISO moment; null for an empty or broken box. */
export function dayStart(day: string, days = 0): string | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
    if (!match)
        return null;
    const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + days);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** The query of api/logs: the "to" day is included (up to the start of the next one). */
export function toQuery(filters: LogFilters, before: number | null = null): LogQuery {
    return {
        q: filters.text.trim() || null,
        levels: filters.levels.size ? LOG_LEVELS.filter(level => filters.levels.has(level)).join(',') : null,
        from: dayStart(filters.fromDay),
        to: dayStart(filters.toDay, 1),
        tenant: filters.space === 'all' ? null : filters.space === 'server' ? 0 : filters.space,
        before,
    };
}

export function hasFilters(filters: LogFilters): boolean {
    return !!filters.text.trim() || filters.levels.size > 0 || !!filters.fromDay || !!filters.toDay || filters.space !== 'all';
}

/** The last part of a logger's name: `ScanCoordinator` for `Sfe.Core.Indexing.ScanCoordinator`. */
export function shortCategory(category: string): string {
    const name = category.split('[')[0];
    return name.slice(name.lastIndexOf('.') + 1) || category;
}
