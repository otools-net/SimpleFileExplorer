import { ConnectionKind, ConnectionLevelName } from '../../core/config.models';
import { ConnectRequest, LEVEL_NAMES, SignInKindName } from '../../core/connection.models';
import type { WizardReviewLine } from '../wizard-definition';

/** How far a group of the space uses the connection. */
export type GroupUse = 'read' | 'write';

/** What the add-a-connection wizard asks for (plain values: kept as JSON in the tab across the provider's sign-in). */
export interface AddConnectionDraft {
    /** The kind of storage (`dropbox`); '' until one is picked. */
    kind: string;
    /** Its name (Dropbox), for the review. */
    kindName: string;
    /** Who it is for: one's own account, this space, or the server; null until chosen. */
    level: ConnectionLevelName | null;
    /** The space's groups that use it (library and server levels), by group id. */
    groups: Record<string, GroupUse>;
    /** The groups' names, for the review and the end. */
    groupNames: Record<string, string>;
    name: string;
    /** The folder of the storage shown as the top ('' = all of it). */
    rootPath: string;
    method: SignInKindName | null;
    /** The ids of the way's fields (a name, a password), all to fill in. */
    fieldIds: string[];
    /** The way's fields as typed (a password): never kept in the tab. */
    fields: Record<string, string>;
    /** The code flow: the sign-in begun at the provider, for the request it was begun with (`requestKey`). */
    state: string;
    begunFor: string;
    /** The code the provider showed, typed back. */
    code: string;
}

export function initialDraft(): AddConnectionDraft {
    return {
        kind: '',
        kindName: '',
        level: null,
        groups: {},
        groupNames: {},
        name: '',
        rootPath: '',
        method: null,
        fieldIds: [],
        fields: {},
        state: '',
        begunFor: '',
        code: '',
    };
}

/** What `POST connections/authorize` takes for the draft (the parts not chosen yet left empty). */
export function connectRequest(draft: AddConnectionDraft): ConnectRequest {
    const shared = draft.level === 'library' || draft.level === 'server';
    return {
        kind: draft.kind,
        level: draft.level ?? 'account',
        name: draft.name,
        method: draft.method ?? 'oAuthRedirect',
        rootPath: draft.rootPath || undefined,
        fields: draft.fields,
        groups: shared && Object.keys(draft.groups).length ? draft.groups : undefined,
        return: 'wizard',
    };
}

/** The request a code-flow sign-in is begun for: a change of an earlier step means beginning it again. */
export function requestKey(draft: AddConnectionDraft): string {
    return JSON.stringify({ ...connectRequest(draft), fields: undefined });
}

/** The draft as kept in the tab while the browser is at the provider: no secret typed. */
export function withoutSecrets(draft: AddConnectionDraft): AddConnectionDraft {
    return { ...draft, fields: {}, code: '' };
}

/** The kinds this server offers now (with a way to sign in). */
export function kindOf(kinds: readonly ConnectionKind[], kind: string): ConnectionKind | undefined {
    return kinds.find((k) => k.kind === kind);
}

/** The draft's way to sign in set to `method` of `kind`: its fields to fill, nothing typed or begun. */
export function withMethod(kind: ConnectionKind | undefined, method: string | undefined): Partial<AddConnectionDraft> {
    const way = kind?.signIn.find((m) => m.kind === method) ?? kind?.signIn[0];
    return {
        method: (way?.kind as SignInKindName | undefined) ?? null,
        fieldIds: way?.fields.map((field) => field.id) ?? [],
        fields: {},
        state: '',
        begunFor: '',
        code: '',
    };
}

/** The sign-in step is filled in: a way chosen, and its fields (or the code of a sign-in begun for this draft). */
export function signInReady(draft: AddConnectionDraft): boolean {
    if (draft.method === 'oAuthRedirect') return true;
    if (draft.method === 'oAuthCode')
        return draft.state !== '' && draft.begunFor === requestKey(draft) && draft.code.trim() !== '';
    return draft.method !== null && draft.fieldIds.every((id) => (draft.fields[id] ?? '').trim() !== '');
}

/** How the pages name how far a group uses it. */
export const GROUP_USES: Record<GroupUse, string> = {
    read: $localize`:a group uses a connection:Read only`,
    write: $localize`:a group uses a connection:Read & write`,
};

/** Who uses it, in lines: only its owner, or the groups chosen and how far. */
export function usesLines(draft: AddConnectionDraft): WizardReviewLine[] {
    const level = { label: $localize`For`, value: draft.level ? LEVEL_NAMES[draft.level] : '' };
    if (draft.level === 'account' || !draft.level) return [level];
    const groups = Object.entries(draft.groups).map(([id, use]) => ({
        label: draft.groupNames[id] ?? id,
        value: GROUP_USES[use],
    }));
    return groups.length
        ? [level, ...groups]
        : [level, { label: $localize`Groups`, value: $localize`None yet: only the server's administrators use it` }];
}
