/*
 * Personal access tokens: programs calling the API as an account (`Authorization: Bearer`). The server's
 * AccessTokenModels.cs; agents/docs/development/api-tokens-plan.md.
 */

/** A token as lists show it (`me/tokens`, `access/tokens`): never its secret. */
export interface AccessTokenInfo {
    id: number;
    userId: number;
    userName: string;
    name: string;
    /** Its first characters, to recognise it. */
    prefix: string;
    /** The only library it works in; null: every library its account reaches. */
    tenantId: number | null;
    /** The rights it is limited to; empty: all of its account's. */
    rights: string[];
    /** `personal` (the only kind for now). */
    kind: string;
    created: string;
    /** Null: it never expires. */
    expires: string | null;
    lastUsed: string | null;
    /** The address it was last used from. */
    lastUsedFrom: string | null;
    revoked: string | null;
    revokedById: number | null;
}

/** A new token, as its account asks for it; left out: the longest life allowed, every library, every right. */
export interface AccessTokenDraft {
    name: string;
    expires?: string | null;
    tenantId?: number | null;
    rights?: string[];
}

/** A token just made, with the whole token (`secret`): shown once, never given again. */
export interface CreatedAccessToken {
    token: AccessTokenInfo;
    secret: string;
}

/** What `api/config` may tell about tokens beyond whether they work (`accessTokens: true`). */
export interface AccessTokensConfig {
    enabled: boolean;
    /** Longest life of a new token, in days; 0: a token may never expire; unknown: the server checks it. */
    maxLifetimeDays?: number;
}
