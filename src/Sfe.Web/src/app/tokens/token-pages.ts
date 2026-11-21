import type { AdminSubpage } from '../admin/admin-sections';
import { Rights } from '../core/rights';

/** My account › Access tokens: one's own tokens. */
export const MY_TOKENS_PAGE: AdminSubpage = {
    id: 'tokens',
    title: $localize`Access tokens`,
    description: $localize`Tokens that let your programs and scripts use the API as you, without your password.`,
    rights: [Rights.ownTokens],
};

/** Spaces & accounts › Access tokens: every account's tokens. */
export const ALL_TOKENS_PAGE: AdminSubpage = {
    id: 'tokens',
    title: $localize`Access tokens`,
    description: $localize`The accounts' access tokens: where they work and when they were last used; revoke them.`,
    rights: [Rights.manageTokens],
};

/** Spaces & accounts › Token settings: the `accessTokens` tab of settings-schema.ts, the whole server's. */
export const TOKEN_SETTINGS_PAGE: AdminSubpage = {
    id: 'tokenSettings',
    title: $localize`Token settings`,
    description: $localize`Whether accounts may make access tokens, how long a token may last and how many each \
account may have.`,
    settings: 'accessTokens',
    rights: [Rights.serverSettings],
};
