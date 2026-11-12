import type { HostingKind } from '../account/account.models';
import type { ChangelogEntry, MigrationStatus } from '../core/access.models';

/** /api/migration: whether the install waits for its update to this version, and what changed since its version. */
export interface MigrationInfo {
    status: MigrationStatus;
    appVersion: string;
    /** null: unknown (installs before 1.37.0 recorded none). */
    databaseVersion: string | null;
    /** The versions after the install's up to this one, newest first. */
    changes: ChangelogEntry[];
    /** What the update did; empty unless asked with the setup code. */
    log: string[];
    /** Why it stopped; without the setup code, only that the reason is in the server's log. */
    error: string | null;
    codeConfigured: boolean;
    codeTooShort: boolean;
    minimumCodeLength: number;
    hosting: HostingKind;
    codeGenerated?: boolean;
    codeFile?: string | null;
}

/** A backup made on the update page: downloaded once from `url`. */
export interface BackupLink {
    url: string;
    fileName: string;
    size: number;
}
