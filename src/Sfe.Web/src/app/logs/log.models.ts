/** The severities of the server's log, from the least to the most severe. */
export type LogLevelName = 'trace' | 'debug' | 'information' | 'warning' | 'securityWarning' | 'error' | 'critical';

export interface LogEntry {
    id: number;
    timestamp: string;
    level: LogLevelName;
    /** The logger's name, e.g. Sfe.Core.Indexing.ScanCoordinator. */
    category: string;
    message: string;
    exception: string | null;
    /** The space whose services logged it; null for the server's own entries. */
    tenantId: number | null;
}

export interface LogQuery {
    q: string | null;
    /** Comma-separated LogLevelName values; null = every level. */
    levels: string | null;
    /** ISO moments; `to` excluded. */
    from: string | null;
    to: string | null;
    /** A space's id; 0 = the server's own entries (server administrators only). */
    tenant: number | null;
    before: number | null;
}

export interface LogSearchResult {
    /** Newest first. */
    entries: LogEntry[];
    /** `before` of the next (older) page; null on the last one. */
    next: number | null;
    retentionDays: number;
    writeFiles: boolean;
    /** Where the daily files are (server administrators only). */
    filesDirectory: string | null;
    /** The session sees the whole server's log; otherwise only its space's. */
    allSpaces: boolean;
}
