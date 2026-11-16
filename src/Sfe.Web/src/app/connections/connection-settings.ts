import type { SettingTab } from '../settings/settings-schema';

/**
 * The settings of connections (Connections:*), a tab of settings-schema.ts kept here: the whole server's, for its
 * administrators; Sfe:Locked (connections.*) in the configuration fixes them. Its cache of previews:
 * agents/docs/features/connection-previews.md.
 */
export const CONNECTION_SETTINGS: SettingTab = {
    id: 'connections',
    title: 'Connections',
    intro:
        'Connections to storage outside the libraries (Dropbox). The server itself calls the provider, ' +
        "so these are the whole server's settings; Sfe:Locked (connections.*) in the configuration fixes them.",
    groups: [
        {
            title: 'What is allowed',
            fields: [
                {
                    key: 'connections.enabled',
                    label: 'Allow connections',
                    type: 'checkbox',
                    hint:
                        'Off: no connection is listed, opened or made, whatever the rights say. ' +
                        'Those already made are kept.',
                },
                {
                    key: 'connections.providers',
                    label: 'Kinds allowed',
                    type: 'list',
                    placeholder: 'dropbox',
                    hint: 'One per line; empty allows every kind this server knows.',
                },
                {
                    key: 'connections.levels',
                    label: 'Levels allowed',
                    type: 'list',
                    placeholder: 'account\nlibrary\nserver',
                    hint:
                        'One per line: account (private to who made it), library (a space and its groups), ' +
                        'server (offered to spaces). Connections of a level taken out are kept but not usable.',
                },
            ],
        },
        {
            title: 'Dropbox',
            fields: [
                {
                    key: 'connections.dropbox.appKey',
                    label: 'App key',
                    type: 'text',
                    hint:
                        "The key of the app you registered in Dropbox's App Console; empty: Dropbox is not " +
                        'offered. Its secret, optional, is set apart and never shown again.',
                },
            ],
        },
        {
            title: 'Cache of previews',
            fields: [
                {
                    key: 'connections.cache.maxSizeMegabytes',
                    label: 'Cache size (MB)',
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint:
                        'Thumbnails and files of connections kept on this server for previews (videos converted, ' +
                        "pictures the provider doesn't thumbnail), for every space; 0 turns it off.",
                },
                {
                    key: 'connections.cache.maxFileMegabytes',
                    label: 'Largest file downloaded (MB)',
                    type: 'number',
                    min: 1,
                    max: 1048576,
                    hint: "A larger file plays only when the browser plays it, and gets only its provider's thumbnail.",
                },
                {
                    key: 'connections.cache.maxAgeDays',
                    label: 'Days kept unused',
                    type: 'number',
                    min: 1,
                    max: 3650,
                    hint: 'What nobody looked at for this long is removed.',
                },
            ],
        },
    ],
};
