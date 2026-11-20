import type { SettingTab } from '../settings/settings-schema';

/**
 * The settings of personal access tokens (Security:AccessTokens), a tab of settings-schema.ts kept here: the whole
 * server's, for its administrators; Sfe:Locked (security.accessTokens.*) in the configuration fixes them.
 */
export const TOKEN_SETTINGS: SettingTab = {
    id: 'accessTokens',
    title: $localize`Access tokens`,
    intro: $localize`Tokens let programs call the API as the account that made them (My account › Access tokens). \
These are the whole server's settings.`,
    groups: [
        {
            title: $localize`Access tokens`,
            fields: [
                {
                    key: 'security.accessTokens.enabled',
                    label: $localize`Allow access tokens`,
                    type: 'checkbox',
                    hint: $localize`Off: no token can be made, and those already made are refused. They are kept, \
and work again once this is back on.`,
                },
                {
                    key: 'security.accessTokens.maxLifetimeDays',
                    label: $localize`Longest life (days)`,
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint: $localize`A new token expires after at most this many days. 0 lets a token never expire.`,
                },
                {
                    key: 'security.accessTokens.maxPerAccount',
                    label: $localize`Tokens per account`,
                    type: 'number',
                    min: 1,
                    max: 1000,
                    hint: $localize`How many tokens an account may hold at once (those neither revoked nor \
expired).`,
                },
            ],
        },
    ],
};
