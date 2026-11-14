import type { AdminSubpage } from '../admin/admin-sections';
import { Rights } from '../core/rights';

/** My account › Access tokens: one's own tokens. */
export const MY_TOKENS_PAGE: AdminSubpage = {
    id: 'tokens',
    title: 'Access tokens',
    description: 'Tokens letting your programs and scripts call the API as you, without your password.',
    rights: [Rights.ownTokens],
};

/** Spaces & accounts › Access tokens: every account's tokens. */
export const ALL_TOKENS_PAGE: AdminSubpage = {
    id: 'tokens',
    title: 'Access tokens',
    description: "The accounts' access tokens: where they work, when they were last used; revoke them.",
    rights: [Rights.manageTokens],
};

/** Spaces & accounts › Token settings: the `accessTokens` tab of settings-schema.ts, the whole server's. */
export const TOKEN_SETTINGS_PAGE: AdminSubpage = {
    id: 'tokenSettings',
    title: 'Token settings',
    description: 'Whether accounts may make access tokens, how long a token may live and how many each may hold.',
    settings: 'accessTokens',
    rights: [Rights.serverSettings],
};
