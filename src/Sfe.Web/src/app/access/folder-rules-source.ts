import { Api } from '../core/api.service';
import { FolderRuleChange } from '../core/access.models';
import { ConnectionRule, ConnectionRuleView } from '../core/connection.models';
import { FolderPicker } from '../core/models';
import { FolderAccessReport, FolderRuleView, GroupInfo, UserInfo } from './access.models';

/** A group a rule can be for, as the card shows it. */
export type RuleGroup = Pick<GroupInfo, 'id' | 'name'> & Partial<Pick<GroupInfo, 'builtIn'>>;

/** An account a rule can be for, as the card shows it. */
export type RuleMember = Pick<UserInfo, 'id' | 'userName' | 'displayName'>;

/**
 * Where the folder access card (`sfe-folder-access`) reads and changes rules: the library's own folders, or those of
 * a connection. What a source can't do (re-point, tell who has access) is left out, and the card hides it.
 */
export interface FolderRulesSource {
    /** How the card names the root folder. */
    readonly rootName: string;
    load(): Promise<FolderRuleView[]>;
    /** Sets one rule, or with `level` null removes it; answers every rule. */
    set(change: FolderRuleChange): Promise<FolderRuleView[]>;
    /** Every sub-folder of `path` (null: the root), whatever rules apply to the asker. */
    folders(path: string | null): Promise<FolderPicker>;
    /** Moves the rules of a folder gone to `to`; answers every rule. */
    repoint?(from: string, to: string): Promise<FolderRuleView[]>;
    /** Who may see and change the folder `path`. */
    report?(path: string): Promise<FolderAccessReport>;
}

/** The rules of a space's library (`space`: another space than the session's, for server administrators). */
export function libraryRules(api: Api, space: number | null): FolderRulesSource {
    return {
        rootName: 'Library root',
        load: () => api.folderRules(space),
        set: (change) => api.setFolderRule(change, space),
        folders: (path) => api.spaceFolders(path, space),
        repoint: (from, to) => api.repointFolderRules(from, to, space),
        report: (path) => api.folderAccess(path, space),
    };
}

/**
 * The rules the session's library sets in a connection (`connections.library`). The server replaces them all at
 * once: a change is made to the rules last read and the whole list sent. Their folders are not checked at the
 * provider, so none shows as gone.
 */
export class ConnectionRules implements FolderRulesSource {
    private rules: ConnectionRuleView[] = [];

    constructor(
        private readonly api: Api,
        private readonly connectionId: number,
        readonly rootName: string,
    ) {}

    async load(): Promise<FolderRuleView[]> {
        return this.keep(await this.api.connectionRules(this.connectionId));
    }

    async set(change: FolderRuleChange): Promise<FolderRuleView[]> {
        const same = (rule: ConnectionRule) =>
            rule.path === change.path && rule.groupId === change.groupId && rule.userId === change.userId;
        const kept: ConnectionRule[] = this.rules.filter((rule) => !same(rule)).map(ConnectionRules.input);
        if (change.level !== null) {
            const itemId = this.rules.find(same)?.itemId ?? null;
            kept.push({ ...change, level: change.level, itemId });
        }
        return this.keep(await this.api.setConnectionRules(this.connectionId, kept));
    }

    folders(path: string | null): Promise<FolderPicker> {
        return this.api.connectionFolders(this.connectionId, path ?? '');
    }

    private keep(rules: ConnectionRuleView[]): FolderRuleView[] {
        this.rules = rules;
        return rules.map(({ itemId: _, ...rule }) => ({ ...rule, exists: true, created: '' }));
    }

    private static input({ path, groupId, userId, level, itemId }: ConnectionRule): ConnectionRule {
        return { path, groupId, userId, level, itemId };
    }
}
